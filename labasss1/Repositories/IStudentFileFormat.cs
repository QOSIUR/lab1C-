using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>
/// Один из форматов файла со списком студентов. Все форматы хранят одно и то же содержимое
/// и взаимозаменяемы: прочитанное из любого из них можно записать в любой другой.
/// </summary>
public interface IStudentFileFormat
{
    /// <summary>Расширение файла с точкой, например «.txt».</summary>
    string Extension { get; }

    /// <summary>Название формата для сообщений пользователю.</summary>
    string Description { get; }

    /// <summary>
    /// Читает список студентов. Повреждённый файл — <see cref="FormatException"/>,
    /// отсутствующий — <see cref="FileNotFoundException"/>.
    /// </summary>
    List<Student> Read(string path);

    void Write(string path, IReadOnlyCollection<Student> students);
}
