using System.IO;
using System.Windows;
using labasss1.Repositories;
using labasss1.Services;
using labasss1.ViewModels;

namespace labasss1;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // По умолчанию in и out — один и тот же набор файлов рядом с программой
        // (students.txt, students.bin, students.dat); пути можно сменить в окне.
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "students.txt");
        var repository = new StudentFileRepository();

        var state = AppStateStore.Load();
        var restore = state.RestoreOnStartup;
        var inputPath = restore && !string.IsNullOrWhiteSpace(state.InputPath) ? state.InputPath : defaultPath;
        var outputPath = restore && !string.IsNullOrWhiteSpace(state.OutputPath) ? state.OutputPath : defaultPath;

        var messages = new List<string>();
        var loaded = TryLoad(repository, inputPath, messages, out var error);
        if (loaded == null && inputPath != defaultPath)
        {
            // Сохранённый путь больше не читается — не застреваем на нём, берём набор по умолчанию.
            messages.Add($"Не удалось загрузить сохранённый набор «{inputPath}»: {error} Загружен набор по умолчанию.");
            restore = false;
            inputPath = outputPath = defaultPath;
            loaded = TryLoad(repository, inputPath, messages, out error);
        }

        if (loaded == null)
        {
            // Не запускаемся с пустым списком, иначе первое же сохранение затрёт файлы.
            MessageBox.Show($"Не удалось прочитать список студентов:\n{error}",
                "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Окно создаём раньше ViewModel: оно же реализует диалоги и открытие окон (IUiService).
        var window = new MainWindow();
        var viewModel = new MainViewModel(repository, window, loaded, inputPath, outputPath)
        {
            RestoreOnStartup = state.RestoreOnStartup,
            IsCyclic = restore && state.IsCyclic,
            AllowSameLastName = !restore || state.AllowSameLastName,
            // До RestoreCard: иначе открытая перед закрытием карточка отчисленного не найдётся.
            ShowExpelled = restore && state.ShowExpelled,
        };
        if (restore && state.Card != null)
            viewModel.RestoreCard(state.Card);
        if (messages.Count > 0)
            viewModel.StatusMessage = string.Join(" ", messages.Prepend(viewModel.StatusMessage ?? string.Empty)).Trim();

        window.DataContext = viewModel;
        if (restore && state.MainWindow != null)
            window.ApplyPlacement(state.MainWindow);
        MainWindow = window;
        window.Show();

        if (restore && state.ExpelledWindowOpen)
            window.ShowExpelledWindow();
    }

    /// <summary>
    /// Загружает набор файлов; если файлов нет — сразу создаёт пустые.
    /// Возвращает null, если набор прочитать нельзя (текст ошибки — в <paramref name="error"/>).
    /// </summary>
    private static List<Models.Student>? TryLoad(StudentFileRepository repository, string path,
        List<string> messages, out string? error)
    {
        error = null;
        try
        {
            var result = repository.Load(path);
            messages.AddRange(result.Messages);
            return result.Students;
        }
        catch (FileNotFoundException)
        {
            try
            {
                repository.Save(path, []);
                messages.Add("Файлы списка не найдены — созданы пустые.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                messages.Add($"Не удалось создать файлы списка: {ex.Message}");
            }
            return [];
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return null;
        }
    }
}
