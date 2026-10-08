using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace labasss1.Models;

/// <summary>
/// Студент. Отчисленные студенты не удаляются: у них выставляется <see cref="IsExpelled"/>,
/// и они отображаются в отдельном окне.
/// </summary>
public class Student : INotifyPropertyChanged
{
    private string _lastName = string.Empty;
    private string _firstName = string.Empty;
    private string? _patronymic;
    private string _group = string.Empty;
    private bool _isExpelled;
    private DateTime? _expelledAt;

    /// <summary>Уникальный идентификатор (нужен, т.к. ФИО могут совпадать).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Фамилия (обязательно).</summary>
    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetField(ref _lastName, TextNormalizer.Normalize(value)))
                OnPropertyChanged(nameof(FullName));
        }
    }

    /// <summary>Имя (обязательно).</summary>
    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetField(ref _firstName, TextNormalizer.Normalize(value)))
                OnPropertyChanged(nameof(FullName));
        }
    }

    /// <summary>Отчество (необязательно).</summary>
    public string? Patronymic
    {
        get => _patronymic;
        set
        {
            var text = TextNormalizer.Normalize(value);
            var normalized = text.Length == 0 ? null : text;
            if (SetField(ref _patronymic, normalized))
                OnPropertyChanged(nameof(FullName));
        }
    }

    /// <summary>Учебная группа.</summary>
    public string Group
    {
        get => _group;
        set => SetField(ref _group, TextNormalizer.Normalize(value));
    }

    /// <summary>Оценки по предметам (предметов может быть несколько).</summary>
    public ObservableCollection<SubjectGrade> Grades { get; } = new();

    /// <summary>Отчислен ли студент.</summary>
    public bool IsExpelled
    {
        get => _isExpelled;
        private set => SetField(ref _isExpelled, value);
    }

    /// <summary>Дата отчисления (null, если студент не отчислен).</summary>
    public DateTime? ExpelledAt
    {
        get => _expelledAt;
        private set => SetField(ref _expelledAt, value);
    }

    /// <summary>ФИО одной строкой, отчество — только если указано.</summary>
    public string FullName =>
        Patronymic is null ? $"{LastName} {FirstName}" : $"{LastName} {FirstName} {Patronymic}";

    /// <summary>Средний балл по всем предметам (null, если оценок нет).</summary>
    public double? AverageGrade => Grades.Count == 0 ? null : Grades.Average(g => g.Grade);

    public Student()
    {
        Grades.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AverageGrade));
    }

    public Student(string lastName, string firstName, string group, string? patronymic = null) : this()
    {
        LastName = lastName;
        FirstName = firstName;
        Patronymic = patronymic;
        Group = group;
    }

    /// <summary>Ставит или обновляет оценку по предмету.</summary>
    public void SetGrade(string subject, int grade)
    {
        var existing = Grades.FirstOrDefault(g =>
            string.Equals(g.Subject, TextNormalizer.Normalize(subject), StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Grades.Add(new SubjectGrade(subject, grade));
        }
        else
        {
            existing.Grade = grade;
            OnPropertyChanged(nameof(AverageGrade));
        }
    }

    /// <summary>
    /// Отчисляет студента (запись остаётся, переносится в список отчисленных).
    /// <paramref name="at"/> передаётся при загрузке из файла, иначе берётся текущее время.
    /// </summary>
    public void Expel(DateTime? at = null)
    {
        if (IsExpelled) return;
        IsExpelled = true;
        var date = at ?? DateTime.Now;
        // Храним с точностью до секунды — как в текстовом файле, чтобы все форматы совпадали.
        ExpelledAt = new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerSecond);
    }

    /// <summary>Восстанавливает студента.</summary>
    public void Restore()
    {
        if (!IsExpelled) return;
        IsExpelled = false;
        ExpelledAt = null;
    }

    public override string ToString() => $"{FullName} ({Group})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
