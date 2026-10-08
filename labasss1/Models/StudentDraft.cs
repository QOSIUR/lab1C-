namespace labasss1.Models;

/// <summary>
/// Данные студента «как ввели»: из карточки на экране или из файла. Ещё не проверены —
/// проверка в <see cref="Services.StudentValidator"/>, перенос в <see cref="Student"/> — <see cref="ApplyTo"/>.
/// </summary>
public record StudentDraft(
    string LastName,
    string FirstName,
    string? Patronymic,
    string Group,
    IReadOnlyList<GradeDraft> Grades)
{
    /// <summary>Записывает данные в студента (оценки заменяются целиком). Вызывать после успешной проверки.</summary>
    public void ApplyTo(Student student)
    {
        student.LastName = LastName;
        student.FirstName = FirstName;
        student.Patronymic = Patronymic;
        student.Group = Group;

        student.Grades.Clear();
        foreach (var g in Grades)
            student.Grades.Add(new SubjectGrade(g.Subject, g.Grade));
    }
}

/// <summary>Оценка по предмету «как ввели».</summary>
public record GradeDraft(string Subject, int Grade);
