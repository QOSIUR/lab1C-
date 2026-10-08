using labasss1.Models;

namespace labasss1.Services;

/// <summary>
/// Правила корректного студента — единые для карточки на экране и для чтения файлов:
/// фамилия, имя и группа обязательны; у каждой оценки есть предмет; оценки от 2 до 5;
/// один предмет не указывается дважды. Текст сравнивается после нормализации.
/// </summary>
public static class StudentValidator
{
    /// <summary>Возвращает текст первой найденной ошибки или null, если данные корректны.</summary>
    public static string? Validate(StudentDraft draft)
    {
        if (TextNormalizer.IsBlank(draft.LastName)) return "Не указана фамилия.";
        if (TextNormalizer.IsBlank(draft.FirstName)) return "Не указано имя.";
        if (TextNormalizer.IsBlank(draft.Group)) return "Не указана группа.";

        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < draft.Grades.Count; i++)
        {
            var grade = draft.Grades[i];
            var subject = TextNormalizer.Normalize(grade.Subject);
            if (subject.Length == 0)
                return $"Не указано название предмета в строке {i + 1} таблицы оценок.";
            if (grade.Grade is < SubjectGrade.MinGrade or > SubjectGrade.MaxGrade)
                return $"Оценка по предмету «{subject}» должна быть от {SubjectGrade.MinGrade} до {SubjectGrade.MaxGrade}.";
            if (!subjects.Add(subject))
                return $"Предмет «{subject}» указан несколько раз.";
        }

        return null;
    }
}
