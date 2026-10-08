using System.IO;
using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>Результат загрузки: список студентов и сообщения о восстановленных файлах.</summary>
public record StudentLoadResult(List<Student> Students, IReadOnlyList<string> Messages);

/// <summary>
/// Хранит список студентов сразу в трёх взаимозаменяемых файлах с одним именем:
/// текстовом (.txt), бинарном (.bin) и байтовом (.dat). Сохранение всегда пишет все три.
/// При загрузке отсутствующий, повреждённый или отличающийся от остальных файл
/// сразу же создаётся заново по содержимому остальных.
/// </summary>
public class StudentFileRepository
{
    private readonly IReadOnlyList<IStudentFileFormat> _formats =
    [
        new TextStudentFormat(),
        new BinaryStudentFormat(),
        new ByteStudentFormat(),
    ];

    /// <summary>Расширения всех форматов — для фильтра диалога выбора файла.</summary>
    public IEnumerable<string> Extensions => _formats.Select(f => f.Extension);

    /// <summary>
    /// Пути трёх файлов набора. Можно передать путь к любому из них (или без расширения) —
    /// набор определяется папкой и именем файла.
    /// </summary>
    public IReadOnlyList<string> GetFilePaths(string path) =>
        _formats.Select(f => PathFor(path, f)).ToList();

    /// <summary>
    /// Загружает студентов из набора файлов и выравнивает набор. Если исправных файлов нет —
    /// <see cref="FileNotFoundException"/> (нет ни одного) или <see cref="FormatException"/> (все повреждены).
    /// </summary>
    public StudentLoadResult Load(string path)
    {
        var attempts = _formats.Select(f => TryRead(PathFor(path, f), f)).ToList();
        var valid = attempts.Where(a => a.Students != null).ToList();

        if (valid.Count == 0)
        {
            if (attempts.All(a => a.Missing))
                throw new FileNotFoundException(
                    $"Не найден ни один файл списка: {string.Join(", ", attempts.Select(a => Path.GetFileName(a.Path)))}.");

            throw new FormatException("Все файлы списка повреждены, восстановить не из чего:\n" +
                                      string.Join("\n", attempts.Select(a => $"{Path.GetFileName(a.Path)}: {a.Problem}")));
        }

        // Эталон — содержимое, на котором сходится большинство файлов; при равенстве — самый свежий.
        var reference = valid
            .GroupBy(a => a.Fingerprint)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(a => a.LastWriteUtc))
            .First();
        var students = reference.First().Students!;

        var messages = new List<string>();
        foreach (var attempt in attempts.Where(a => a.Fingerprint != reference.Key))
        {
            var reason = attempt.Missing ? "отсутствовал"
                : attempt.Students == null ? $"повреждён ({attempt.Problem})"
                : "отличался от остальных";
            var name = Path.GetFileName(attempt.Path);
            try
            {
                attempt.Format.Write(attempt.Path, students);
                messages.Add($"Файл {name} {reason} — создан заново.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                messages.Add($"Файл {name} {reason}, но пересоздать его не удалось: {ex.Message}");
            }
        }

        return new StudentLoadResult(students, messages);
    }

    /// <summary>Записывает список во все три файла набора.</summary>
    public void Save(string path, IReadOnlyCollection<Student> students)
    {
        var errors = new List<string>();
        foreach (var format in _formats)
        {
            var filePath = PathFor(path, format);
            try
            {
                format.Write(filePath, students);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new IOException(string.Join("; ", errors));
    }

    private static string PathFor(string path, IStudentFileFormat format) =>
        Path.ChangeExtension(path.Trim(), format.Extension);

    private static ReadAttempt TryRead(string path, IStudentFileFormat format)
    {
        if (!File.Exists(path))
            return new ReadAttempt(path, format, null, true, "файл отсутствует", DateTime.MinValue);

        try
        {
            var students = format.Read(path);
            return new ReadAttempt(path, format, students, false, null, File.GetLastWriteTimeUtc(path));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            return new ReadAttempt(path, format, null, false, ex.Message, DateTime.MinValue);
        }
    }

    private record ReadAttempt(string Path, IStudentFileFormat Format, List<Student>? Students, bool Missing,
        string? Problem, DateTime LastWriteUtc)
    {
        /// <summary>Текстовое представление содержимого: равные отпечатки — одинаковые данные.</summary>
        public string? Fingerprint { get; } = Students == null ? null : TextStudentFormat.Serialize(Students);
    }
}
