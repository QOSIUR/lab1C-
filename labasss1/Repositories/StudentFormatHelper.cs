using System.IO;
using System.Security.Cryptography;
using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>Общие для всех форматов проверки и запись файла.</summary>
internal static class StudentFormatHelper
{
    /// <summary>Длина контрольной суммы SHA-256 в конце бинарного и байтового файлов.</summary>
    public const int HashLength = 32;

    /// <summary>
    /// Собирает студента из прочитанных полей. Одинаковые проверки во всех форматах гарантируют,
    /// что «прочиталось» означает «данные целы», а не просто «байты разобрались».
    /// </summary>
    public static Student CreateStudent(Guid id, string lastName, string firstName, string? patronymic,
        string group, bool expelled, DateTime? expelledAt, IEnumerable<(string Subject, int Grade)> grades)
    {
        if (string.IsNullOrWhiteSpace(lastName)) throw new FormatException("У студента не указана фамилия.");
        if (string.IsNullOrWhiteSpace(firstName)) throw new FormatException($"У студента «{lastName}» не указано имя.");
        if (string.IsNullOrWhiteSpace(group)) throw new FormatException($"У студента «{lastName}» не указана группа.");
        if (expelled != expelledAt.HasValue)
            throw new FormatException($"У студента «{lastName}» дата отчисления не соответствует признаку отчисления.");

        var student = new Student(lastName, firstName, group, patronymic) { Id = id };
        foreach (var (subject, grade) in grades)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new FormatException($"У студента «{lastName}» пустое название предмета.");
            if (grade is < SubjectGrade.MinGrade or > SubjectGrade.MaxGrade)
                throw new FormatException($"У студента «{lastName}» недопустимая оценка {grade}.");
            if (student.Grades.Any(g => string.Equals(g.Subject, subject.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new FormatException($"У студента «{lastName}» предмет «{subject}» указан дважды.");
            student.Grades.Add(new SubjectGrade(subject, grade));
        }

        if (expelled) student.Expel(expelledAt);
        return student;
    }

    /// <summary>Проверяет, что идентификаторы студентов не повторяются.</summary>
    public static List<Student> EnsureUniqueIds(List<Student> students)
    {
        var duplicate = students.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new FormatException($"Идентификатор {duplicate.Key} встречается дважды.");
        return students;
    }

    public static byte[] ComputeHash(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    /// <summary>Проверяет контрольную сумму в конце файла и возвращает длину данных без неё.</summary>
    public static int VerifyHash(byte[] content)
    {
        if (content.Length < HashLength) throw new FormatException("Файл слишком короткий.");

        var dataLength = content.Length - HashLength;
        var expected = ComputeHash(content.AsSpan(0, dataLength));
        if (!expected.AsSpan().SequenceEqual(content.AsSpan(dataLength)))
            throw new FormatException("Не совпадает контрольная сумма — файл повреждён.");
        return dataLength;
    }

    /// <summary>
    /// Пишет файл через временный и подменяет оригинал, чтобы при сбое не потерять старые данные.
    /// </summary>
    public static void WriteAtomically(string path, Action<Stream> write)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            write(stream);
        File.Move(tempPath, path, overwrite: true);
    }
}
