using System.Collections.ObjectModel;
using labasss1.Models;

namespace labasss1.Services;

/// <summary>Итог восстановления: кого вернули в список обучающихся, а кого нельзя (однофамилец в группе).</summary>
public record RestoreResult(IReadOnlyList<Student> Restored, IReadOnlyList<Student> Skipped);

/// <summary>
/// Список студентов и бизнес-операции над ним: добавить или изменить студента, удалить его запись,
/// отчислить, восстановить, безвозвратно удалить отчисленных. Здесь же правило однофамильцев.
/// Класс ничего не знает ни об окнах, ни о файлах — сохранением занимается вызывающий код.
/// </summary>
public class StudentRegistry
{
    public StudentRegistry(IEnumerable<Student> students)
    {
        Students = new ObservableCollection<Student>(students);
    }

    /// <summary>
    /// Все студенты в порядке списка, включая отчисленных. Коллекция наблюдаемая — для привязки окон;
    /// изменять её нужно только через методы реестра, чтобы соблюдались правила.
    /// </summary>
    public ObservableCollection<Student> Students { get; }

    /// <summary>
    /// Можно ли завести в группе обучающегося с фамилией, которая в этой группе уже есть (чекбокс 3).
    /// </summary>
    public bool AllowSameLastName { get; set; } = true;

    /// <summary>
    /// Проверяет, можно ли сохранить данные как нового студента (<paramref name="existing"/> = null)
    /// или как изменение существующего. Возвращает текст ошибки или null.
    /// </summary>
    public string? CheckCanSave(StudentDraft draft, Student? existing)
    {
        if (existing is { IsExpelled: true })
            return "Отчисленного студента изменить нельзя — сначала восстановите его.";

        return StudentValidator.Validate(draft) ?? CheckNamesake(draft, existing);
    }

    /// <summary>
    /// Сохраняет данные: изменяет существующего студента или добавляет нового в конец списка.
    /// Если правила нарушены — <see cref="InvalidOperationException"/> (проверяйте заранее через <see cref="CheckCanSave"/>).
    /// </summary>
    public Student Save(StudentDraft draft, Student? existing)
    {
        var error = CheckCanSave(draft, existing);
        if (error != null) throw new InvalidOperationException(error);

        var student = existing ?? new Student();
        draft.ApplyTo(student);
        if (existing == null) Students.Add(student);
        return student;
    }

    /// <summary>Удаляет запись обучающегося студента (карточку очистили кнопкой Clear и ушли с неё).</summary>
    public void Remove(Student student) => Students.Remove(student);

    /// <summary>Отчисляет студента: запись остаётся и переходит в список отчисленных.</summary>
    public void Expel(Student student) => student.Expel();

    /// <summary>
    /// Восстанавливает отчисленных. Восстановление возвращает студента в группу, поэтому при запрете
    /// однофамильцев студент, чья фамилия в группе уже занята, не восстанавливается.
    /// </summary>
    public RestoreResult Restore(IEnumerable<Student> students)
    {
        var restored = new List<Student>();
        var skipped = new List<Student>();
        foreach (var s in students.Where(s => s.IsExpelled).ToList())
        {
            if (!AllowSameLastName && FindNamesake(s.LastName, s.Group, except: s) != null)
            {
                skipped.Add(s);
                continue;
            }
            s.Restore();
            restored.Add(s);
        }

        return new RestoreResult(restored, skipped);
    }

    /// <summary>Безвозвратно удаляет отчисленных. Обучающихся так удалить нельзя — они пропускаются.</summary>
    public IReadOnlyList<Student> DeleteExpelled(IEnumerable<Student> students)
    {
        var deleted = students.Where(s => s.IsExpelled).ToList();
        foreach (var s in deleted) Students.Remove(s);
        return deleted;
    }

    /// <summary>Заменяет весь список (загрузка из файлов).</summary>
    public void ReplaceAll(IEnumerable<Student> students)
    {
        var list = students.ToList();
        Students.Clear();
        foreach (var s in list) Students.Add(s);
    }

    /// <summary>Обучающийся студент той же группы с той же фамилией (без учёта регистра), кроме <paramref name="except"/>.</summary>
    public Student? FindNamesake(string lastName, string group, Student? except)
    {
        var normalizedLastName = TextNormalizer.Normalize(lastName);
        var normalizedGroup = TextNormalizer.Normalize(group);
        return Students.FirstOrDefault(s => !s.IsExpelled && s != except &&
                                            SameText(s.LastName, normalizedLastName) &&
                                            SameText(s.Group, normalizedGroup));
    }

    /// <summary>
    /// Правило однофамильцев. Проверяется только новый студент или смена фамилии/группы:
    /// однофамильцы, которые уже есть в списке, работе не мешают.
    /// </summary>
    private string? CheckNamesake(StudentDraft draft, Student? existing)
    {
        if (AllowSameLastName) return null;

        var lastName = TextNormalizer.Normalize(draft.LastName);
        var group = TextNormalizer.Normalize(draft.Group);
        if (existing != null && SameText(existing.LastName, lastName) && SameText(existing.Group, group))
            return null;

        var namesake = FindNamesake(lastName, group, except: existing);
        return namesake == null
            ? null
            : $"В группе «{group}» уже есть студент с фамилией «{namesake.LastName}» ({namesake.FullName}). " +
              "Чтобы добавить однофамильца, отметьте «Разрешить одинаковые фамилии в одной группе».";
    }

    private static bool SameText(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}
