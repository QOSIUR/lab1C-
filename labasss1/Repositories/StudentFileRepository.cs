using System.IO;
using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>Результат загрузки: список студентов и сообщения о восстановленных файлах.</summary>
public record StudentLoadResult(List<Student> Students, IReadOnlyList<string> Messages);

/// <summary>
/// Хранит список студентов сразу в трёх взаимозаменяемых файлах с одним именем:
/// текстовом (.txt), бинарном (.bin) и байтовом (.dat). Сохранение всегда пишет все три
/// и проверяет, что записалось. При загрузке и проверке (<see cref="Repair"/>) отсутствующий,
/// повреждённый, устаревший или отличающийся файл сразу же создаётся заново по остальным.
/// </summary>
public class StudentFileRepository
{
    private readonly IReadOnlyList<IStudentFileFormat> _formats =
    [
        new TextStudentFormat(),
        new BinaryStudentFormat(),
        new ByteStudentFormat(),
    ];

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
        var attempts = ReadAll(path);
        var reference = ChooseReference(attempts);
        if (reference == null)
        {
            if (attempts.All(a => a.State == FileState.Missing))
                throw new FileNotFoundException(
                    $"Не найден ни один файл списка: {string.Join(", ", attempts.Select(a => a.Name))}.");

            throw new FormatException("Все файлы списка повреждены или недоступны, восстановить не из чего:\n" +
                                      string.Join("\n", attempts.Select(a => $"{a.Name}: {a.Problem}")));
        }

        return new StudentLoadResult(reference.Content!.Students, RepairOthers(attempts, reference));
    }

    /// <summary>
    /// Проверяет набор и чинит файлы, которые отсутствуют, повреждены или расходятся с остальными.
    /// Если исправных файлов не осталось совсем, записывает <paramref name="fallback"/>
    /// (список, который сейчас открыт в программе), а без него — только сообщает о проблеме.
    /// Возвращает сообщения о том, что было сделано; пустой список — набор в порядке.
    /// </summary>
    public IReadOnlyList<string> Repair(string path, IReadOnlyCollection<Student>? fallback)
    {
        var attempts = ReadAll(path);
        var reference = ChooseReference(attempts);
        if (reference != null)
            return RepairOthers(attempts, reference);

        if (fallback == null)
            return attempts.All(a => a.State == FileState.Missing)
                ? []
                : [$"Все файлы набора «{Path.GetFileNameWithoutExtension(path)}» повреждены или недоступны — восстановить не из чего."];

        try
        {
            Save(path, fallback);
            return [$"Файлы набора «{Path.GetFileNameWithoutExtension(path)}» пропали или повреждены — созданы заново из открытого списка."];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"Файлы набора пропали или повреждены, создать заново не удалось: {ex.Message}"];
        }
    }

    /// <summary>
    /// Записывает список во все три файла набора с новым номером сохранения, затем читает их
    /// обратно и сверяет. Если хоть один файл не записался или не совпал — <see cref="IOException"/>.
    /// </summary>
    public void Save(string path, IReadOnlyCollection<Student> students)
    {
        var content = new StudentFileContent(students.ToList(), NextRevision(path));
        var expected = Fingerprint(content.Students);

        var errors = new List<string>();
        foreach (var format in _formats)
        {
            var filePath = PathFor(path, format);
            try
            {
                format.Write(filePath, content);
                var written = format.Read(filePath);
                if (written.Revision != content.Revision || Fingerprint(written.Students) != expected)
                    errors.Add($"{Path.GetFileName(filePath)}: после записи содержимое не совпадает");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new IOException(string.Join("; ", errors));
    }

    /// <summary>
    /// Номер нового сохранения: текущее время, но строго больше номеров в уже существующих файлах —
    /// на случай, если часы компьютера перевели назад.
    /// </summary>
    private long NextRevision(string path)
    {
        var existing = ReadAll(path).Where(a => a.Content != null).Select(a => a.Content!.Revision).DefaultIfEmpty(0).Max();
        return Math.Max(DateTime.UtcNow.Ticks, existing + 1);
    }

    private List<ReadAttempt> ReadAll(string path) =>
        _formats.Select(f => TryRead(PathFor(path, f), f)).ToList();

    /// <summary>
    /// Эталон, по которому чинятся остальные файлы:
    /// 1) самое новое сохранение (если запись прервалась, новые данные есть только в части файлов);
    /// 2) среди файлов одного сохранения — содержимое, на котором сходится большинство;
    /// 3) при равенстве — файл, изменённый последним.
    /// </summary>
    private static ReadAttempt? ChooseReference(List<ReadAttempt> attempts)
    {
        var valid = attempts.Where(a => a.Content != null).ToList();
        if (valid.Count == 0) return null;

        var newest = valid.Max(a => a.Content!.Revision);
        return valid
            .Where(a => a.Content!.Revision == newest)
            .GroupBy(a => a.Fingerprint)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(a => a.LastWriteUtc))
            .First()
            .OrderByDescending(a => a.LastWriteUtc)
            .First();
    }

    /// <summary>Перезаписывает по эталону все файлы, которые с ним не совпадают.</summary>
    private static List<string> RepairOthers(List<ReadAttempt> attempts, ReadAttempt reference)
    {
        var content = reference.Content!;
        var messages = new List<string>();
        foreach (var attempt in attempts)
        {
            if (attempt.Content != null && attempt.Content.Revision == content.Revision &&
                attempt.Fingerprint == reference.Fingerprint)
                continue;

            var reason = attempt.State switch
            {
                FileState.Missing => "отсутствовал",
                FileState.Corrupt => $"повреждён ({attempt.Problem})",
                FileState.Unavailable => $"недоступен ({attempt.Problem})",
                _ when attempt.Content!.Revision < content.Revision => "устарел (не записался при последнем сохранении)",
                _ => "отличался от остальных",
            };
            try
            {
                attempt.Format.Write(attempt.Path, content);
                messages.Add($"Файл {attempt.Name} {reason} — создан заново.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                messages.Add($"Файл {attempt.Name} {reason}, пересоздать его не удалось: {ex.Message}");
            }
        }

        return messages;
    }

    private static string PathFor(string path, IStudentFileFormat format) =>
        Path.ChangeExtension(path.Trim(), format.Extension);

    private static string Fingerprint(IReadOnlyCollection<Student> students) => TextStudentFormat.Serialize(students);

    private static ReadAttempt TryRead(string path, IStudentFileFormat format)
    {
        try
        {
            if (!File.Exists(path))
                return new ReadAttempt(path, format, null, FileState.Missing, "файл отсутствует", DateTime.MinValue);

            var content = format.Read(path);
            return new ReadAttempt(path, format, content, FileState.Ok, null, File.GetLastWriteTimeUtc(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && ex is not FileNotFoundException)
        {
            // Файл есть, но прочитать его нельзя (занят другой программой, нет прав) — это не повреждение.
            return new ReadAttempt(path, format, null, FileState.Unavailable, ex.Message, DateTime.MinValue);
        }
        catch (FileNotFoundException)
        {
            // Файл удалили между проверкой и чтением.
            return new ReadAttempt(path, format, null, FileState.Missing, "файл отсутствует", DateTime.MinValue);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Любая другая ошибка разбора (в том числе неожиданная) означает, что файл повреждён:
            // его нужно восстановить по остальным, а не ронять программу.
            return new ReadAttempt(path, format, null, FileState.Corrupt, ex.Message, DateTime.MinValue);
        }
    }

    private enum FileState { Ok, Missing, Corrupt, Unavailable }

    private record ReadAttempt(string Path, IStudentFileFormat Format, StudentFileContent? Content, FileState State,
        string? Problem, DateTime LastWriteUtc)
    {
        public string Name => System.IO.Path.GetFileName(Path);

        /// <summary>Текстовое представление содержимого: равные отпечатки — одинаковые данные.</summary>
        public string? Fingerprint { get; } = Content == null ? null : TextStudentFormat.Serialize(Content.Students);
    }
}
