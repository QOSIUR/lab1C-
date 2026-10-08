using System.Globalization;
using System.IO;
using System.Text;
using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>
/// Текстовый формат (.txt): StreamWriter/StreamReader, каждый студент — блок строк «Ключ=Значение».
/// Строка Count в начале позволяет заметить обрезанный файл, Revision — номер сохранения
/// (в файлах старой версии этих строк может не быть).
/// <code>
/// # Список студентов
/// Revision=638640000000000000
/// Count=1
///
/// [Student]
/// Id=3f2a...
/// LastName=Иванов
/// FirstName=Иван
/// Patronymic=Иванович
/// Group=ИС-21
/// Expelled=false
/// ExpelledAt=
/// Grade=Математика|5
/// Grade=Физика|4
/// </code>
/// </summary>
public class TextStudentFormat : IStudentFileFormat
{
    private const string StudentHeader = "[Student]";
    private const string CountKey = "Count";
    private const string RevisionKey = "Revision";
    private const char GradeSeparator = '|';
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    public string Extension => ".txt";
    public string Description => "текстовый";

    public StudentFileContent Read(string path)
    {
        StudentFormatHelper.EnsureReasonableSize(path);

        var students = new List<Student>();
        int? expectedCount = null;
        long? revision = null;
        StudentRecord? current = null;

        using var reader = new StreamReader(path, Encoding.UTF8);
        var lineNumber = 0;
        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line == StudentHeader)
            {
                if (current != null) students.Add(current.ToStudent());
                current = new StudentRecord(lineNumber);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
                throw new FormatException($"Строка {lineNumber}: ожидается «Ключ=Значение».");
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (current == null)
            {
                // До первого студента допустимы только заголовочные строки, каждая не более одного раза.
                if (key == CountKey && expectedCount == null)
                {
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                        throw new FormatException($"Строка {lineNumber}: некорректное количество студентов.");
                    expectedCount = count;
                }
                else if (key == RevisionKey && revision == null)
                {
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rev))
                        throw new FormatException($"Строка {lineNumber}: некорректный номер сохранения.");
                    revision = rev;
                }
                else
                {
                    throw new FormatException($"Строка {lineNumber}: данные вне блока {StudentHeader}.");
                }
                continue;
            }

            current.Set(key, value, lineNumber);
        }

        if (current != null) students.Add(current.ToStudent());

        // В файлах старой версии строки Count нет — их принимаем без этой проверки.
        if (expectedCount != null && expectedCount != students.Count)
            throw new FormatException(
                $"Заявлено студентов: {expectedCount}, прочитано: {students.Count} — файл обрезан или испорчен.");

        return new StudentFileContent(StudentFormatHelper.EnsureUniqueIds(students), revision ?? 0);
    }

    public void Write(string path, StudentFileContent content)
    {
        var text = Serialize(content.Students, content.Revision);
        StudentFormatHelper.WriteAtomically(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.Write(text);
        });
    }

    /// <summary>
    /// Текстовое представление списка. Без ревизии используется для сравнения содержимого файлов
    /// разных форматов: одинаковый текст — одинаковые данные.
    /// </summary>
    public static string Serialize(IReadOnlyCollection<Student> students, long? revision = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Список студентов");
        if (revision != null)
            sb.AppendLine($"{RevisionKey}={revision.Value.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{CountKey}={students.Count}");

        foreach (var s in students)
        {
            sb.AppendLine();
            sb.AppendLine(StudentHeader);
            sb.AppendLine($"Id={s.Id}");
            sb.AppendLine($"LastName={Clean(s.LastName)}");
            sb.AppendLine($"FirstName={Clean(s.FirstName)}");
            sb.AppendLine($"Patronymic={Clean(s.Patronymic)}");
            sb.AppendLine($"Group={Clean(s.Group)}");
            sb.AppendLine($"Expelled={(s.IsExpelled ? "true" : "false")}");
            sb.AppendLine($"ExpelledAt={s.ExpelledAt?.ToString(DateFormat, CultureInfo.InvariantCulture)}");
            foreach (var g in s.Grades)
                sb.AppendLine($"Grade={Clean(g.Subject)}{GradeSeparator}{g.Grade}");
        }

        return sb.ToString();
    }

    private static string Clean(string? value) =>
        (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>Промежуточное представление блока из файла.</summary>
    private class StudentRecord
    {
        private readonly int _headerLine;
        private Guid? _id;
        private string _lastName = string.Empty;
        private string _firstName = string.Empty;
        private string? _patronymic;
        private string _group = string.Empty;
        private bool _expelled;
        private DateTime? _expelledAt;
        private readonly List<(string Subject, int Grade)> _grades = new();

        public StudentRecord(int headerLine)
        {
            _headerLine = headerLine;
        }

        public void Set(string key, string value, int lineNumber)
        {
            switch (key)
            {
                case "Id":
                    if (!Guid.TryParse(value, out var id))
                        throw new FormatException($"Строка {lineNumber}: некорректный Id.");
                    _id = id;
                    break;
                case "LastName": _lastName = value; break;
                case "FirstName": _firstName = value; break;
                case "Patronymic": _patronymic = value; break;
                case "Group": _group = value; break;
                case "Expelled":
                    if (!bool.TryParse(value, out _expelled))
                        throw new FormatException($"Строка {lineNumber}: Expelled должно быть true или false.");
                    break;
                case "ExpelledAt":
                    if (value.Length == 0) { _expelledAt = null; break; }
                    if (!DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var date))
                        throw new FormatException($"Строка {lineNumber}: некорректная дата отчисления.");
                    _expelledAt = date;
                    break;
                case "Grade":
                    var sep = value.LastIndexOf(GradeSeparator);
                    if (sep <= 0 || !int.TryParse(value[(sep + 1)..], out var grade))
                        throw new FormatException($"Строка {lineNumber}: ожидается «Grade=Предмет|оценка(2–5)».");
                    _grades.Add((value[..sep].Trim(), grade));
                    break;
                default:
                    throw new FormatException($"Строка {lineNumber}: неизвестный ключ «{key}».");
            }
        }

        public Student ToStudent()
        {
            if (_id == null)
                throw new FormatException($"Строка {_headerLine}: у студента нет Id.");
            try
            {
                return StudentFormatHelper.CreateStudent(_id.Value, _lastName, _firstName, _patronymic, _group,
                    _expelled, _expelledAt, _grades);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"Строка {_headerLine}: {ex.Message}", ex);
            }
        }
    }
}
