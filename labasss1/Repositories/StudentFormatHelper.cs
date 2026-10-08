using System.IO;
using System.Security.Cryptography;
using labasss1.Models;
using labasss1.Services;

namespace labasss1.Repositories;

/// <summary>Общие для всех форматов проверки и запись файла.</summary>
internal static class StudentFormatHelper
{
    /// <summary>Длина контрольной суммы SHA-256 в конце бинарного и байтового файлов.</summary>
    public const int HashLength = 32;

    /// <summary>
    /// Предел размера файла. Настоящий список занимает килобайты; файл больше — заведомо испорчен,
    /// и читать его в память целиком нельзя (огромный файл обрушил бы программу).
    /// </summary>
    public const long MaxFileSize = 64L * 1024 * 1024;

    /// <summary>Считает файл повреждённым, если он больше <see cref="MaxFileSize"/>.</summary>
    public static void EnsureReasonableSize(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxFileSize)
            throw new FormatException($"Файл слишком большой ({length / (1024 * 1024)} МБ) — он повреждён.");
    }

    /// <summary>
    /// Собирает студента из прочитанных полей. Те же правила, что и для карточки на экране
    /// (<see cref="StudentValidator"/>), гарантируют, что «прочиталось» означает «данные целы»,
    /// а не просто «байты разобрались».
    /// </summary>
    public static Student CreateStudent(Guid id, string lastName, string firstName, string? patronymic,
        string group, bool expelled, DateTime? expelledAt, IEnumerable<(string Subject, int Grade)> grades)
    {
        var draft = new StudentDraft(lastName, firstName, patronymic, group,
            grades.Select(g => new GradeDraft(g.Subject, g.Grade)).ToList());
        var error = StudentValidator.Validate(draft);
        if (error != null)
            throw new FormatException($"Студент «{TextNormalizer.Normalize(lastName)}»: {error}");

        // Это правило касается только файла: в карточке признак и дату отчисления не вводят руками.
        if (expelled != expelledAt.HasValue)
            throw new FormatException($"Студент «{lastName}»: дата отчисления не соответствует признаку отчисления.");

        var student = new Student { Id = id };
        draft.ApplyTo(student);
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
