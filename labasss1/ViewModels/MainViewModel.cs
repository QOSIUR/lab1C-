using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using labasss1.Models;
using labasss1.Repositories;
using labasss1.Services;

namespace labasss1.ViewModels;

/// <summary>
/// Листание карточек студентов (1 карточка = 1 студент). Студенты читаются из входного (in) файла,
/// а карточка сохраняется в выходной (out) файл только при переходе на другую карточку.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly StudentFileRepository _repository;
    private readonly IUiService _ui;
    private readonly ObservableCollection<Student> _students;
    private StudentSetWatcher? _watcher;
    private StudentCardViewModel _currentCard = StudentCardViewModel.CreateEmpty();
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _restoreOnStartup;
    private bool _isCyclic;
    private bool _allowSameLastName = true;
    private bool _showExpelled;
    private string _inputPath;
    private string _outputPath;

    public MainViewModel(StudentFileRepository repository, IUiService ui, IEnumerable<Student> students,
        string inputPath, string outputPath)
    {
        _repository = repository;
        _ui = ui;
        _students = new ObservableCollection<Student>(students);
        _inputPath = inputPath;
        _outputPath = outputPath;

        var expelledView = new ListCollectionView(_students)
        {
            Filter = o => ((Student)o).IsExpelled,
            IsLiveFiltering = true,
        };
        expelledView.LiveFilteringProperties.Add(nameof(Student.IsExpelled));
        ExpelledStudents = expelledView;

        FirstCommand = new RelayCommand(GoFirst, () => CurrentIndex > 0);
        PreviousCommand = new RelayCommand(GoPrevious, CanGoPrevious);
        NextCommand = new RelayCommand(GoNext, CanGoNext);
        LastCommand = new RelayCommand(GoLast, CanGoLast);
        AddCommand = new RelayCommand(AddStudent);
        ExpelCommand = new RelayCommand(ExpelCurrent,
            () => !CurrentCard.IsNew && !CurrentCard.IsBlank && !CurrentCard.IsReadOnly);
        RestoreCurrentCommand = new RelayCommand(() => RestoreStudents([CurrentCard.Source!]),
            () => CurrentCard.IsReadOnly);
        RestoreCommand = new RelayCommand(o => RestoreStudents(ToStudents(o)), o => ToStudents(o).Count > 0);
        DeleteCommand = new RelayCommand(o => DeleteStudents(ToStudents(o)), o => ToStudents(o).Count > 0);
        LoadCommand = new RelayCommand(Load, () => !string.IsNullOrWhiteSpace(InputPath));
        BrowseInputCommand = new RelayCommand(BrowseInput);
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        ShowExpelledWindowCommand = new RelayCommand(_ui.ShowExpelledWindow);
        ExitCommand = new RelayCommand(_ui.CloseMainWindow);
        HelpCommand = new RelayCommand(_ui.ShowHelp);
        AboutCommand = new RelayCommand(_ui.ShowAbout);

        ShowFirstOrEmpty();
        WatchFiles();
    }

    public StudentCardViewModel CurrentCard
    {
        get => _currentCard;
        private set => SetField(ref _currentCard, value);
    }

    /// <summary>Отчисленные студенты — для отдельного окна.</summary>
    public ICollectionView ExpelledStudents { get; }

    /// <summary>
    /// Путь к входному набору файлов (.txt/.bin/.dat с одним именем), из которого читаются студенты.
    /// </summary>
    public string InputPath
    {
        get => _inputPath;
        set { if (SetField(ref _inputPath, value)) WatchFiles(); }
    }

    /// <summary>Путь к выходному набору файлов: изменения сохраняются сразу во все три формата.</summary>
    public string OutputPath
    {
        get => _outputPath;
        set { if (SetField(ref _outputPath, value)) WatchFiles(); }
    }

    /// <summary>Начинает следить за файлами входного и выходного наборов (вместо прежних).</summary>
    private void WatchFiles()
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            var paths = new[] { InputPath, OutputPath }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .SelectMany(_repository.GetFilePaths);
            _watcher = new StudentSetWatcher(paths, CheckFiles);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
        {
            // Путь ещё вводится или некорректен — следить пока не за чем.
        }
    }

    /// <summary>
    /// Файлы набора изменились снаружи, пока программа работает: сразу же восстанавливаем
    /// отсутствующие и повреждённые по остальным. Если выходной набор пропал целиком —
    /// записываем его из открытого списка.
    /// </summary>
    private void CheckFiles()
    {
        var messages = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(OutputPath))
                messages.AddRange(_repository.Repair(OutputPath, fallback: _students));
            if (!string.IsNullOrWhiteSpace(InputPath) && !SameSet(InputPath, OutputPath))
                messages.AddRange(_repository.Repair(InputPath, fallback: null));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
        {
            messages.Add($"Не удалось проверить файлы списка: {ex.Message}");
        }

        if (messages.Count > 0)
            StatusMessage = string.Join(" ", messages);
    }

    private bool SameSet(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(_repository.GetFilePaths(a)[0]),
                Path.GetFullPath(_repository.GetFilePaths(b)[0]), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// Чекбокс: при следующем запуске вернуть программу в то же состояние, что и перед закрытием.
    /// </summary>
    public bool RestoreOnStartup
    {
        get => _restoreOnStartup;
        set => SetField(ref _restoreOnStartup, value);
    }

    /// <summary>
    /// Чекбокс цикличной прокрутки: «Вперёд» с последней карточки ведёт на первую, «Назад» с первой —
    /// на последнюю. Если сброшен, «Вперёд» с последней карточки открывает новую пустую карточку.
    /// </summary>
    public bool IsCyclic
    {
        get => _isCyclic;
        set => SetField(ref _isCyclic, value);
    }

    /// <summary>
    /// Чекбокс: можно ли завести в группе студента с фамилией, которая в этой группе уже есть.
    /// </summary>
    public bool AllowSameLastName
    {
        get => _allowSameLastName;
        set => SetField(ref _allowSameLastName, value);
    }

    /// <summary>
    /// Чекбокс: показывать отчисленных студентов при листании карточек (только для просмотра).
    /// </summary>
    public bool ShowExpelled
    {
        get => _showExpelled;
        set
        {
            if (!SetField(ref _showExpelled, value)) return;

            // Открытая карточка отчисленного пропадает из списка — переходим на ближайшего обучающегося.
            if (!value && CurrentCard.Source is { IsExpelled: true } hidden)
            {
                var next = _students.Skip(_students.IndexOf(hidden) + 1).FirstOrDefault(s => !s.IsExpelled);
                if (next != null) ShowCard(StudentCardViewModel.FromStudent(next));
                else ShowLastOrEmpty();
                return;
            }
            OnPropertyChanged(nameof(PositionText));
        }
    }

    /// <summary>Информационное сообщение (например, о восстановленных файлах).</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string PositionText => CurrentCard.IsNew
        ? $"Новая карточка (всего студентов: {VisibleStudents.Count})"
        : $"Карточка {CurrentIndex + 1} из {VisibleStudents.Count}" + (CurrentCard.IsReadOnly ? " (отчислен)" : "");

    public RelayCommand FirstCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand LastCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand ExpelCommand { get; }

    /// <summary>Восстановить отчисленных. Параметр — один студент или список выделенных.</summary>
    public RelayCommand RestoreCommand { get; }

    /// <summary>Безвозвратно удалить отчисленных. Параметр — один студент или список выделенных.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Восстановить студента, чья карточка открыта (при показе отчисленных в списке).</summary>
    public RelayCommand RestoreCurrentCommand { get; }

    public RelayCommand LoadCommand { get; }

    /// <summary>Выбрать входной набор файлов в диалоге и сразу загрузить его.</summary>
    public RelayCommand BrowseInputCommand { get; }

    /// <summary>Выбрать выходной набор файлов в диалоге (файлы могут ещё не существовать).</summary>
    public RelayCommand BrowseOutputCommand { get; }

    public RelayCommand ShowExpelledWindowCommand { get; }
    public RelayCommand ExitCommand { get; }

    /// <summary>Справка по функциям программы.</summary>
    public RelayCommand HelpCommand { get; }

    public RelayCommand AboutCommand { get; }

    private void BrowseInput()
    {
        var path = _ui.PickFile("Выбор входного (in) файла", InputPath, mustExist: true);
        if (path == null) return;

        InputPath = path;
        // Выбранный файл сразу загружаем — так привычнее, чем нажимать ещё и «Загрузить».
        Load();
    }

    private void BrowseOutput()
    {
        var path = _ui.PickFile("Выбор выходного (out) файла", OutputPath, mustExist: false);
        if (path != null) OutputPath = path;
    }

    /// <summary>Студенты, по которым листаются карточки: обучающиеся, а с чекбоксом 4 — и отчисленные.</summary>
    private List<Student> VisibleStudents => _students.Where(s => ShowExpelled || !s.IsExpelled).ToList();

    /// <summary>Позиция текущей карточки среди видимых; новая карточка стоит после последней.</summary>
    private int CurrentIndex => CurrentCard.Source is { } s
        ? VisibleStudents.IndexOf(s)
        : VisibleStudents.Count;

    // «Вперёд» с последней карточки открывает новую пустую (или первую при цикличной прокрутке).
    // С пустой новой карточки идти некуда, разве что по кругу на первую.
    private bool CanGoNext() =>
        !(CurrentCard.IsNew && CurrentCard.IsBlank) || (IsCyclic && VisibleStudents.Count > 0);

    // «Назад» с первой карточки — только по кругу на последнюю.
    private bool CanGoPrevious() => CurrentIndex > 0 || (IsCyclic && VisibleStudents.Count > 0);

    // С заполненной новой карточки «в конец» сохраняет её, и она сама становится последней.
    private bool CanGoLast() => CurrentCard.IsNew
        ? !CurrentCard.IsBlank || VisibleStudents.Count > 0
        : CurrentIndex < VisibleStudents.Count - 1;

    private void GoFirst()
    {
        if (!TryCommitCurrentCard()) return;
        ShowFirstOrEmpty();
    }

    private void GoLast()
    {
        if (!TryCommitCurrentCard()) return;
        ShowLastOrEmpty();
    }

    private void GoNext()
    {
        var index = CurrentIndex;
        var wasNew = CurrentCard.IsNew;
        var willBeDeleted = CurrentCard.IsMarkedForDeletion;
        if (!TryCommitCurrentCard()) return;

        // Новая карточка после сохранения стала последней; очищенную стёрли, и следующий
        // студент сдвинулся на её место.
        var visible = VisibleStudents;
        var next = wasNew ? visible.Count : willBeDeleted ? index : index + 1;
        if (next < visible.Count)
            ShowCard(StudentCardViewModel.FromStudent(visible[next]));
        else if (IsCyclic && visible.Count > 0)
            ShowCard(StudentCardViewModel.FromStudent(visible[0]));
        else
            ShowCard(StudentCardViewModel.CreateEmpty());
    }

    private void GoPrevious()
    {
        var index = CurrentIndex;
        if (!TryCommitCurrentCard()) return;

        // После сохранения новая карточка становится последней, а очищенная стирается,
        // поэтому позицию берём до сохранения.
        var visible = VisibleStudents;
        if (index - 1 >= 0 && index - 1 < visible.Count)
            ShowCard(StudentCardViewModel.FromStudent(visible[index - 1]));
        else if (IsCyclic)
            ShowLastOrEmpty();
        else
            ShowFirstOrEmpty();
    }

    private void AddStudent()
    {
        if (!TryCommitCurrentCard()) return;
        ShowCard(StudentCardViewModel.CreateEmpty());
    }

    private void ExpelCurrent()
    {
        var index = CurrentIndex;
        if (!TryCommitCurrentCard()) return;

        var student = CurrentCard.Source!;
        student.Expel();
        if (!TrySave()) return;

        // Показываем студента, который встал на место отчисленного, иначе предыдущего.
        var visible = VisibleStudents;
        if (visible.Count == 0)
            ShowCard(StudentCardViewModel.CreateEmpty());
        else
            ShowCard(StudentCardViewModel.FromStudent(visible[Math.Min(index, visible.Count - 1)]));
    }

    private void RestoreStudents(IReadOnlyList<Student> students)
    {
        // Восстановление возвращает студента в группу, поэтому однофамильцев проверяем и здесь.
        var skipped = new List<string>();
        foreach (var s in students)
        {
            if (!AllowSameLastName && FindNamesake(s.LastName, s.Group, except: s) != null)
            {
                skipped.Add(s.FullName);
                continue;
            }
            s.Restore();
        }

        if (skipped.Count < students.Count) TrySave();

        // Открыта карточка восстановленного студента — она перестаёт быть только для просмотра.
        if (CurrentCard.Source is { IsExpelled: false } current && students.Contains(current))
            ShowCard(StudentCardViewModel.FromStudent(current));

        if (skipped.Count > 0)
            ErrorMessage = $"Не восстановлены — в группе уже есть студент с такой фамилией: {string.Join(", ", skipped)}.";
        OnPropertyChanged(nameof(PositionText));
    }

    /// <summary>
    /// Если однофамильцы в группе запрещены, возвращает текст ошибки для карточки, которая
    /// заводит студента с уже занятой в группе фамилией. Проверяем только новую карточку
    /// или смену фамилии/группы — уже существующие однофамильцы не мешают листать список.
    /// </summary>
    private string? CheckNamesake(StudentCardViewModel card)
    {
        if (AllowSameLastName) return null;

        var lastName = TextNormalizer.Normalize(card.LastName);
        var group = TextNormalizer.Normalize(card.Group);
        if (card.Source is { } source && SameText(source.LastName, lastName) && SameText(source.Group, group))
            return null;

        var namesake = FindNamesake(lastName, group, except: card.Source);
        return namesake == null
            ? null
            : $"В группе «{group}» уже есть студент с фамилией «{namesake.LastName}» ({namesake.FullName}). " +
              "Чтобы добавить однофамильца, отметьте «Разрешить одинаковые фамилии в одной группе».";
    }

    /// <summary>Обучающийся студент той же группы с той же фамилией (без учёта регистра).</summary>
    private Student? FindNamesake(string lastName, string group, Student? except) =>
        _students.FirstOrDefault(s => !s.IsExpelled && s != except &&
                                      SameText(s.LastName, TextNormalizer.Normalize(lastName)) && SameText(s.Group, TextNormalizer.Normalize(group)));

    private static bool SameText(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    private void DeleteStudents(IReadOnlyList<Student> students)
    {
        // Удалить могут и открытую сейчас карточку отчисленного — тогда покажем соседнюю.
        var currentDeleted = CurrentCard.Source is { } current && students.Contains(current);
        var index = CurrentIndex;

        foreach (var s in students) _students.Remove(s);
        TrySave();

        if (currentDeleted)
        {
            var visible = VisibleStudents;
            ShowCard(visible.Count == 0
                ? StudentCardViewModel.CreateEmpty()
                : StudentCardViewModel.FromStudent(visible[Math.Min(index, visible.Count - 1)]));
        }
        OnPropertyChanged(nameof(PositionText));
    }

    /// <summary>
    /// Приводит параметр команды к списку отчисленных студентов. Копия нужна, потому что
    /// SelectedItems таблицы меняется, пока мы восстанавливаем или удаляем студентов.
    /// </summary>
    private static List<Student> ToStudents(object? parameter) => parameter switch
    {
        Student s => s.IsExpelled ? [s] : [],
        System.Collections.IEnumerable items => items.OfType<Student>().Where(s => s.IsExpelled).ToList(),
        _ => [],
    };

    /// <summary>Снимок открытой карточки, включая правки, которые ещё не записаны в файлы.</summary>
    public CardState CaptureCard() => CurrentCard.ToState();

    /// <summary>
    /// Открывает карточку из снимка. Если студента из снимка уже нет среди обучающихся
    /// (файлы изменились), остаётся первая карточка.
    /// </summary>
    public void RestoreCard(CardState state)
    {
        Student? source = null;
        if (state.StudentId is { } id)
        {
            source = VisibleStudents.FirstOrDefault(s => s.Id == id);
            if (source == null)
            {
                StatusMessage = "Студент, открытый перед закрытием, не найден — показана первая карточка.";
                return;
            }
        }

        ShowCard(StudentCardViewModel.FromState(source, state));
    }

    /// <summary>Заменяет список студентов содержимым входного файла.</summary>
    private void Load()
    {
        // Незаписанную карточку сначала сохраняем, чтобы правки не пропали.
        if (!TryCommitCurrentCard()) return;

        StudentLoadResult loaded;
        try
        {
            loaded = _repository.Load(InputPath);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            ErrorMessage = $"Не удалось загрузить список: {ex.Message}";
            return;
        }

        _students.Clear();
        foreach (var s in loaded.Students) _students.Add(s);
        ShowFirstOrEmpty();
        StatusMessage = loaded.Messages.Count > 0
            ? string.Join(" ", loaded.Messages)
            : $"Загружено студентов: {loaded.Students.Count}.";
    }

    /// <summary>
    /// Сохраняет текущую карточку: переносит данные в студента и записывает файл.
    /// Пустая новая карточка просто отбрасывается, а очищенная (Clear) стирает запись студента.
    /// </summary>
    private bool TryCommitCurrentCard()
    {
        var card = CurrentCard;
        if (card.IsNew && card.IsBlank) return true;

        // Карточка отчисленного только для просмотра — сохранять нечего.
        if (card.IsReadOnly) return true;

        if (card.IsMarkedForDeletion)
        {
            _students.Remove(card.Source!);
            CurrentCard = StudentCardViewModel.CreateEmpty();
            return TrySave();
        }

        var error = card.Validate() ?? CheckNamesake(card);
        if (error != null)
        {
            ErrorMessage = error;
            return false;
        }

        var student = card.ApplyTo();
        if (card.IsNew)
        {
            _students.Add(student);
            // Дальше карточка работает с уже сохранённым студентом.
            CurrentCard = StudentCardViewModel.FromStudent(student);
            OnPropertyChanged(nameof(PositionText));
        }

        return TrySave();
    }

    private bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            ErrorMessage = "Не указан путь к выходному (out) файлу.";
            return false;
        }

        try
        {
            _repository.Save(OutputPath, _students);
            ErrorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            ErrorMessage = $"Не удалось сохранить файл «{OutputPath}»: {ex.Message}";
            return false;
        }
    }

    private void ShowFirstOrEmpty()
    {
        var first = VisibleStudents.FirstOrDefault();
        ShowCard(first == null ? StudentCardViewModel.CreateEmpty() : StudentCardViewModel.FromStudent(first));
    }

    private void ShowLastOrEmpty()
    {
        var last = VisibleStudents.LastOrDefault();
        ShowCard(last == null ? StudentCardViewModel.CreateEmpty() : StudentCardViewModel.FromStudent(last));
    }

    private void ShowCard(StudentCardViewModel card)
    {
        CurrentCard = card;
        ErrorMessage = null;
        OnPropertyChanged(nameof(PositionText));
    }
}
