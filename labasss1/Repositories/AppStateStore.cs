using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace labasss1.Repositories;

/// <summary>Снимок состояния программы, сохраняемый при закрытии.</summary>
public class AppState
{
    /// <summary>Чекбокс «восстанавливать состояние при следующем запуске». Запоминается всегда.</summary>
    public bool RestoreOnStartup { get; set; }

    /// <summary>Чекбокс цикличной прокрутки списка.</summary>
    public bool IsCyclic { get; set; }

    /// <summary>Чекбокс «разрешить одинаковые фамилии в группе» (по умолчанию разрешено).</summary>
    public bool AllowSameLastName { get; set; } = true;

    /// <summary>Чекбокс «показывать отчисленных при просмотре списка».</summary>
    public bool ShowExpelled { get; set; }

    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }

    /// <summary>Открытая карточка вместе с несохранёнными правками.</summary>
    public CardState? Card { get; set; }

    public WindowPlacement? MainWindow { get; set; }
    public bool ExpelledWindowOpen { get; set; }
}

public class CardState
{
    /// <summary>Id студента; null — новая карточка.</summary>
    public Guid? StudentId { get; set; }

    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Patronymic { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public List<GradeState> Grades { get; set; } = new();
}

public class GradeState
{
    public string Subject { get; set; } = string.Empty;
    public int Grade { get; set; }
}

public class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>Читает и пишет <see cref="AppState"/> в JSON-файл в профиле пользователя.</summary>
public static class AppStateStore
{
    // Кириллицу пишем как есть, а не \uXXXX, чтобы файл можно было прочитать глазами.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "labasss1", "state.json");

    /// <summary>Возвращает сохранённое состояние; при отсутствии или порче файла — состояние по умолчанию.</summary>
    public static AppState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppState();
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath), Options) ?? new AppState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppState();
        }
    }

    /// <summary>Сохраняет состояние. Ошибка записи не мешает закрытию программы.</summary>
    public static bool TrySave(AppState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, Options));
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
