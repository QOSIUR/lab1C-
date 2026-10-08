using System.IO;
using System.Text;
using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>
/// Байтовый формат (.dat): файл читается и пишется через FileStream массивами байтов,
/// каждое значение раскладывается на байты вручную (числа — big-endian).
/// <code>
/// byte[4] сигнатура "STDT"   byte версия (2)   byte[8] номер сохранения   byte[4] количество студентов
/// студент: byte[16] Id, 4 строки (Фамилия, Имя, Отчество, Группа),
///          byte Отчислен (0/1), byte[8] дата отчисления в тиках (0 — нет),
///          byte[4] количество оценок, затем пары (строка Предмет, byte Оценка)
/// строка:  byte[4] длина в байтах + байты UTF-8
/// byte[32] SHA-256 всего, что выше
/// </code>
/// В версии 1 номера сохранения нет.
/// </summary>
public class ByteStudentFormat : IStudentFileFormat
{
    private static readonly byte[] Signature = "STDT"u8.ToArray();
    private const byte Version = 2;

    public string Extension => ".dat";
    public string Description => "байтовый";

    public StudentFileContent Read(string path)
    {
        // Без этой проверки файл больше 2 ГБ обрушивал программу при выделении массива.
        StudentFormatHelper.EnsureReasonableSize(path);

        byte[] content;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            content = new byte[stream.Length];
            var read = 0;
            while (read < content.Length)
            {
                var n = stream.Read(content, read, content.Length - read);
                if (n == 0) throw new FormatException("Файл неожиданно закончился.");
                read += n;
            }
        }

        var dataLength = StudentFormatHelper.VerifyHash(content);
        var reader = new ByteReader(content, dataLength);

        if (!reader.ReadBytes(Signature.Length).SequenceEqual(Signature))
            throw new FormatException("Неверная сигнатура файла.");
        var version = reader.ReadByte();
        if (version is not (1 or Version)) throw new FormatException($"Неподдерживаемая версия файла: {version}.");

        var revision = version >= 2 ? reader.ReadInt64() : 0;
        if (revision < 0) throw new FormatException("Некорректный номер сохранения.");

        var count = reader.ReadInt32();
        if (count < 0) throw new FormatException("Отрицательное количество студентов.");

        var students = new List<Student>(Math.Min(count, 10_000));
        for (var i = 0; i < count; i++)
            students.Add(ReadStudent(reader));

        if (!reader.AtEnd) throw new FormatException("После списка студентов остались лишние данные.");
        return new StudentFileContent(StudentFormatHelper.EnsureUniqueIds(students), revision);
    }

    public void Write(string path, StudentFileContent content)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Signature);
        bytes.Add(Version);
        AddInt64(bytes, content.Revision);
        AddInt32(bytes, content.Students.Count);
        foreach (var s in content.Students)
            AddStudent(bytes, s);

        var data = bytes.ToArray();
        var hash = StudentFormatHelper.ComputeHash(data);
        StudentFormatHelper.WriteAtomically(path, stream =>
        {
            stream.Write(data, 0, data.Length);
            stream.Write(hash, 0, hash.Length);
        });
    }

    private static void AddStudent(List<byte> bytes, Student s)
    {
        bytes.AddRange(s.Id.ToByteArray());
        AddString(bytes, s.LastName);
        AddString(bytes, s.FirstName);
        AddString(bytes, s.Patronymic ?? string.Empty);
        AddString(bytes, s.Group);
        bytes.Add(s.IsExpelled ? (byte)1 : (byte)0);
        AddInt64(bytes, s.ExpelledAt?.Ticks ?? 0L);
        AddInt32(bytes, s.Grades.Count);
        foreach (var g in s.Grades)
        {
            AddString(bytes, g.Subject);
            bytes.Add((byte)g.Grade);
        }
    }

    private static Student ReadStudent(ByteReader reader)
    {
        var id = new Guid(reader.ReadBytes(16));
        var lastName = reader.ReadString();
        var firstName = reader.ReadString();
        var patronymic = reader.ReadString();
        var group = reader.ReadString();
        var expelled = reader.ReadByte() switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException("Признак отчисления должен быть 0 или 1."),
        };
        var ticks = reader.ReadInt64();
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) throw new FormatException("Некорректная дата отчисления.");

        var gradeCount = reader.ReadInt32();
        if (gradeCount < 0) throw new FormatException("Отрицательное количество оценок.");
        var grades = new List<(string, int)>();
        for (var i = 0; i < gradeCount; i++)
            grades.Add((reader.ReadString(), reader.ReadByte()));

        return StudentFormatHelper.CreateStudent(id, lastName, firstName, patronymic, group,
            expelled, ticks == 0 ? null : new DateTime(ticks), grades);
    }

    private static void AddInt32(List<byte> bytes, int value)
    {
        for (var shift = 24; shift >= 0; shift -= 8)
            bytes.Add((byte)(value >> shift));
    }

    private static void AddInt64(List<byte> bytes, long value)
    {
        for (var shift = 56; shift >= 0; shift -= 8)
            bytes.Add((byte)(value >> shift));
    }

    private static void AddString(List<byte> bytes, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        AddInt32(bytes, encoded.Length);
        bytes.AddRange(encoded);
    }

    /// <summary>Последовательное чтение значений из массива байтов с проверкой границ.</summary>
    private class ByteReader
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

        private readonly byte[] _data;
        private readonly int _length;
        private int _position;

        public ByteReader(byte[] data, int length)
        {
            _data = data;
            _length = length;
        }

        public bool AtEnd => _position == _length;

        public byte ReadByte() => ReadBytes(1)[0];

        public byte[] ReadBytes(int count)
        {
            if (count < 0 || count > _length - _position)
                throw new FormatException("Файл неожиданно закончился.");
            var result = _data.AsSpan(_position, count).ToArray();
            _position += count;
            return result;
        }

        public int ReadInt32()
        {
            var b = ReadBytes(4);
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }

        public long ReadInt64()
        {
            var b = ReadBytes(8);
            long value = 0;
            foreach (var x in b)
                value = (value << 8) | x;
            return value;
        }

        public string ReadString()
        {
            var length = ReadInt32();
            try
            {
                return StrictUtf8.GetString(ReadBytes(length));
            }
            catch (DecoderFallbackException)
            {
                throw new FormatException("Строка содержит некорректные байты UTF-8.");
            }
        }
    }
}
