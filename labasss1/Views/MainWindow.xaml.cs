using System.ComponentModel;
using System.IO;
using System.Windows;
using labasss1.Services;
using labasss1.ViewModels;
using labasss1.Views;
using Microsoft.Win32;

namespace labasss1;

/// <summary>
/// Главное окно. Вся логика — в командах <see cref="MainViewModel"/>, на которые одинаково ссылаются
/// кнопки, меню и горячие клавиши. Окно лишь выполняет то, что требует WPF: диалоги и другие окна.
/// </summary>
public partial class MainWindow : Window, IUiService
{
    // Любой из трёх файлов задаёт весь набор: читаются и пишутся все форматы с этим именем.
    private const string FileFilter =
        "Файлы списка студентов (*.txt;*.bin;*.dat)|*.txt;*.bin;*.dat|" +
        "Текстовый (*.txt)|*.txt|Бинарный (*.bin)|*.bin|Байтовый (*.dat)|*.dat|Все файлы (*.*)|*.*";

    private ExpelledWindow? _expelledWindow;
    private HelpWindow? _helpWindow;

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public string? PickFile(string title, string currentPath, bool mustExist)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = FileFilter,
            DefaultExt = ".txt",
            // Выходного файла может ещё не быть — он создастся при первом сохранении.
            CheckFileExists = mustExist,
        };
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(currentPath));
            if (Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
                dialog.FileName = Path.GetFileName(currentPath);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // В поле введён некорректный путь — просто открываем диалог в папке по умолчанию.
        }

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public void CloseMainWindow() => Close();

    public void ShowHelp()
    {
        if (_helpWindow != null)
        {
            _helpWindow.Activate();
            return;
        }

        // Немодальное окно: справку можно держать открытой рядом с программой.
        _helpWindow = new HelpWindow { Owner = this };
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show();
    }

    public void ShowAbout() =>
        MessageBox.Show(this,
            "Карточки студентов\n\n" +
            "Ведение списка студентов: карточки с ФИО, группой и оценками, отчисление и восстановление, " +
            "хранение в трёх взаимозаменяемых файлах (.txt, .bin, .dat).\n\n" +
            "Подробное описание функций — «Справка → Руководство пользователя» (F1).",
            "О программе", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// При закрытии запоминаем состояние программы. Если чекбокс снят — только сам чекбокс,
    /// чтобы следующий запуск был обычным.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        var state = new AppState { RestoreOnStartup = ViewModel.RestoreOnStartup };
        if (state.RestoreOnStartup)
        {
            state.IsCyclic = ViewModel.IsCyclic;
            state.AllowSameLastName = ViewModel.AllowSameLastName;
            state.ShowExpelled = ViewModel.ShowExpelled;
            state.InputPath = ViewModel.InputPath;
            state.OutputPath = ViewModel.OutputPath;
            state.Card = ViewModel.CaptureCard();
            state.MainWindow = CapturePlacement();
            state.ExpelledWindowOpen = _expelledWindow != null;
        }
        AppStateStore.TrySave(state);
    }

    /// <summary>Возвращает окну сохранённые положение и размер, если они видны на текущих мониторах.</summary>
    public void ApplyPlacement(WindowPlacement placement)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(placement.Left, placement.Top,
            Math.Max(placement.Width, MinWidth), Math.Max(placement.Height, MinHeight));

        // Монитор могли отключить — тогда окно оказалось бы за пределами экрана.
        if (screen.IntersectsWith(bounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        if (placement.Maximized) WindowState = WindowState.Maximized;
    }

    private WindowPlacement CapturePlacement()
    {
        // У развёрнутого окна Left/Top/Width/Height — размеры экрана, обычные берём из RestoreBounds.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    public void ShowExpelledWindow()
    {
        if (_expelledWindow != null)
        {
            _expelledWindow.Activate();
            return;
        }

        _expelledWindow = new ExpelledWindow { Owner = this, DataContext = DataContext };
        _expelledWindow.Closed += (_, _) => _expelledWindow = null;
        _expelledWindow.Show();
    }
}
