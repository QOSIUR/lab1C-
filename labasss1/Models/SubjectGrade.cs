using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace labasss1.Models;

/// <summary>
/// Оценка студента по одному предмету.
/// </summary>
public class SubjectGrade : INotifyPropertyChanged
{
    public const int MinGrade = 2;
    public const int MaxGrade = 5;

    private string _subject = string.Empty;
    private int _grade = MinGrade;

    public SubjectGrade() { }

    public SubjectGrade(string subject, int grade)
    {
        Subject = subject;
        Grade = grade;
    }

    /// <summary>Название предмета.</summary>
    public string Subject
    {
        get => _subject;
        set => SetField(ref _subject, TextNormalizer.Normalize(value));
    }

    /// <summary>Оценка по пятибалльной шкале (2–5).</summary>
    public int Grade
    {
        get => _grade;
        set
        {
            if (value is < MinGrade or > MaxGrade)
                throw new ArgumentOutOfRangeException(nameof(value), $"Оценка должна быть от {MinGrade} до {MaxGrade}.");
            SetField(ref _grade, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
