using System.Collections.ObjectModel;
using labasss1.Models;
using labasss1.Repositories;

namespace labasss1.ViewModels;

/// <summary>
/// Карточка студента — буфер редактирования. Изменения попадают в <see cref="Student"/>
/// только при переходе на другую карточку: тогда <see cref="ToDraft"/> передаётся реестру студентов.
/// </summary>
public class StudentCardViewModel : ViewModelBase
{
    private string _lastName = string.Empty;
    private string _firstName = string.Empty;
    private string _patronymic = string.Empty;
    private string _group = string.Empty;
    private GradeRowViewModel? _selectedGrade;

    private StudentCardViewModel(Student? source)
    {
        Source = source;
        AddGradeCommand = new RelayCommand(() =>
        {
            var row = new GradeRowViewModel();
            Grades.Add(row);
            SelectedGrade = row;
        }, () => !IsReadOnly);
        RemoveGradeCommand = new RelayCommand(
            () => Grades.Remove(SelectedGrade!),
            () => SelectedGrade != null && !IsReadOnly);
        ClearCommand = new RelayCommand(Clear, () => !IsBlank && !IsReadOnly);

        // Пустота карточки зависит и от строк оценок, поэтому следим за ними тоже.
        Grades.CollectionChanged += (_, e) =>
        {
            foreach (var row in e.NewItems?.OfType<GradeRowViewModel>() ?? [])
                row.PropertyChanged += (_, _) => OnBlanknessChanged();
            OnBlanknessChanged();
        };
    }

    /// <summary>Студент, которого редактирует карточка; null — новая, ещё не сохранённая карточка.</summary>
    public Student? Source { get; }

    public bool IsNew => Source == null;

    /// <summary>
    /// Карточка отчисленного студента (видна при включённом показе отчисленных) — только для просмотра:
    /// изменить его можно, лишь восстановив.
    /// </summary>
    public bool IsReadOnly => Source?.IsExpelled == true;

    /// <summary>Подпись для карточки отчисленного.</summary>
    public string ExpelledText => Source?.ExpelledAt is { } date
        ? $"Студент отчислен {date:dd.MM.yyyy}. Карточка только для просмотра — чтобы изменить её, восстановите студента."
        : string.Empty;

    public string LastName { get => _lastName; set { if (SetField(ref _lastName, value)) OnBlanknessChanged(); } }
    public string FirstName { get => _firstName; set { if (SetField(ref _firstName, value)) OnBlanknessChanged(); } }
    public string Patronymic { get => _patronymic; set { if (SetField(ref _patronymic, value)) OnBlanknessChanged(); } }
    public string Group { get => _group; set { if (SetField(ref _group, value)) OnBlanknessChanged(); } }

    public ObservableCollection<GradeRowViewModel> Grades { get; } = new();

    public GradeRowViewModel? SelectedGrade
    {
        get => _selectedGrade;
        set => SetField(ref _selectedGrade, value);
    }

    public RelayCommand AddGradeCommand { get; }
    public RelayCommand RemoveGradeCommand { get; }

    /// <summary>Кнопка Clear: стирает все поля и оценки карточки (в файл пока ничего не пишется).</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>
    /// В карточке ничего не заполнено. Пустая новая карточка молча отбрасывается,
    /// а очищенная карточка существующего студента при переходе удаляет его запись.
    /// </summary>
    public bool IsBlank =>
        TextNormalizer.IsBlank(LastName) && TextNormalizer.IsBlank(FirstName) &&
        TextNormalizer.IsBlank(Patronymic) && TextNormalizer.IsBlank(Group) &&
        Grades.All(g => TextNormalizer.IsBlank(g.Subject));

    /// <summary>Карточка существующего студента очищена — при переходе запись будет стёрта.</summary>
    public bool IsMarkedForDeletion => !IsNew && IsBlank;

    private void Clear()
    {
        LastName = string.Empty;
        FirstName = string.Empty;
        Patronymic = string.Empty;
        Group = string.Empty;
        SelectedGrade = null;
        Grades.Clear();
    }

    private void OnBlanknessChanged() => OnPropertyChanged(nameof(IsMarkedForDeletion));

    public static StudentCardViewModel CreateEmpty() => new(null);

    /// <summary>Снимок карточки вместе с несохранёнными правками — для восстановления при запуске.</summary>
    public CardState ToState() => new()
    {
        StudentId = Source?.Id,
        LastName = LastName,
        FirstName = FirstName,
        Patronymic = Patronymic,
        Group = Group,
        Grades = Grades.Select(g => new GradeState { Subject = g.Subject, Grade = g.Grade }).ToList(),
    };

    /// <summary>Восстанавливает карточку из снимка; <paramref name="source"/> — студент из списка или null.</summary>
    public static StudentCardViewModel FromState(Student? source, CardState state)
    {
        var card = new StudentCardViewModel(source)
        {
            LastName = state.LastName ?? string.Empty,
            FirstName = state.FirstName ?? string.Empty,
            Patronymic = state.Patronymic ?? string.Empty,
            Group = state.Group ?? string.Empty,
        };
        foreach (var g in state.Grades ?? [])
        {
            // Файл состояния могли поправить вручную — оценку приводим к допустимой.
            var grade = Math.Clamp(g.Grade, SubjectGrade.MinGrade, SubjectGrade.MaxGrade);
            card.Grades.Add(new GradeRowViewModel { Subject = g.Subject ?? string.Empty, Grade = grade });
        }
        return card;
    }

    public static StudentCardViewModel FromStudent(Student student)
    {
        var card = new StudentCardViewModel(student)
        {
            LastName = student.LastName,
            FirstName = student.FirstName,
            Patronymic = student.Patronymic ?? string.Empty,
            Group = student.Group,
        };
        foreach (var g in student.Grades)
            card.Grades.Add(new GradeRowViewModel { Subject = g.Subject, Grade = g.Grade });
        return card;
    }

    /// <summary>
    /// Введённые в карточку данные. Проверку и сохранение выполняет <see cref="Services.StudentRegistry"/>.
    /// </summary>
    public StudentDraft ToDraft() => new(
        LastName, FirstName, Patronymic, Group,
        Grades.Select(g => new GradeDraft(g.Subject, g.Grade)).ToList());
}

public class GradeRowViewModel : ViewModelBase
{
    private string _subject = string.Empty;
    private int _grade = SubjectGrade.MaxGrade;

    /// <summary>Допустимые оценки для выпадающего списка.</summary>
    public static IReadOnlyList<int> AllowedGrades { get; } =
        Enumerable.Range(SubjectGrade.MinGrade, SubjectGrade.MaxGrade - SubjectGrade.MinGrade + 1).ToList();

    public string Subject { get => _subject; set => SetField(ref _subject, value); }
    public int Grade { get => _grade; set => SetField(ref _grade, value); }
}
