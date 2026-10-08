using System.IO;
using System.Text;
using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>
/// Бинарный формат (.bin): типизированные значения через BinaryWriter/BinaryReader.
/// <code>
/// int32  сигнатура "STDB"        int32 версия      int32 количество студентов
/// студент: byte[16] Id, string Фамилия, string Имя, string Отчество, string Группа,
///          bool Отчислен, int64 дата отчисления в тиках (0 — нет),
///          int32 количество оценок, затем пары (string Предмет, int32 Оценка)
/// byte[32] SHA-256 всего, что выше
/// </code>
/// Строки BinaryWriter пишет в UTF-8 с префиксом длины.
/// </summary>
public class BinaryStudentFormat : IStudentFileFormat
{
    private const int Signature = 0x42445453; // "STDB" в little-endian
    private const int Version = 1;

    public string Extension => ".bin";
    public string Description => "бинарный";

    public List<Student> Read(string path)
    {
        var content = File.ReadAllBytes(path);
        var dataLength = StudentFormatHelper.VerifyHash(content);

        using var reader = new BinaryReader(new MemoryStream(content, 0, dataLength), Encoding.UTF8);
        try
        {
            if (reader.ReadInt32() != Signature) throw new FormatException("Неверная сигнатура файла.");
            var version = reader.ReadInt32();
            if (version != Version) throw new FormatException($"Неподдерживаемая версия файла: {version}.");

            var count = reader.ReadInt32();
            if (count < 0) throw new FormatException("Отрицательное количество студентов.");

            var students = new List<Student>(Math.Min(count, 10_000));
            for (var i = 0; i < count; i++)
                students.Add(ReadStudent(reader));

            if (reader.BaseStream.Position != dataLength)
                throw new FormatException("После списка студентов остались лишние данные.");
            return StudentFormatHelper.EnsureUniqueIds(students);
        }
        catch (EndOfStreamException)
        {
            throw new FormatException("Файл неожиданно закончился.");
        }
    }

    public void Write(string path, IReadOnlyCollection<Student> students)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Signature);
            writer.Write(Version);
            writer.Write(students.Count);
            foreach (var s in students)
                WriteStudent(writer, s);
        }

        var data = buffer.ToArray();
        var hash = StudentFormatHelper.ComputeHash(data);
        StudentFormatHelper.WriteAtomically(path, stream =>
        {
            using var writer = new BinaryWriter(stream);
            writer.Write(data);
            writer.Write(hash);
        });
    }

    private static void WriteStudent(BinaryWriter writer, Student s)
    {
        writer.Write(s.Id.ToByteArray());
        writer.Write(s.LastName);
        writer.Write(s.FirstName);
        writer.Write(s.Patronymic ?? string.Empty);
        writer.Write(s.Group);
        writer.Write(s.IsExpelled);
        writer.Write(s.ExpelledAt?.Ticks ?? 0L);
        writer.Write(s.Grades.Count);
        foreach (var g in s.Grades)
        {
            writer.Write(g.Subject);
            writer.Write(g.Grade);
        }
    }

    private static Student ReadStudent(BinaryReader reader)
    {
        var idBytes = reader.ReadBytes(16);
        if (idBytes.Length != 16) throw new EndOfStreamException();
        var id = new Guid(idBytes);
        var lastName = reader.ReadString();
        var firstName = reader.ReadString();
        var patronymic = reader.ReadString();
        var group = reader.ReadString();
        var expelled = reader.ReadBoolean();
        var ticks = reader.ReadInt64();
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) throw new FormatException("Некорректная дата отчисления.");

        var gradeCount = reader.ReadInt32();
        if (gradeCount < 0) throw new FormatException("Отрицательное количество оценок.");
        var grades = new List<(string, int)>();
        for (var i = 0; i < gradeCount; i++)
            grades.Add((reader.ReadString(), reader.ReadInt32()));

        return StudentFormatHelper.CreateStudent(id, lastName, firstName, patronymic, group,
            expelled, ticks == 0 ? null : new DateTime(ticks), grades);
    }
}
