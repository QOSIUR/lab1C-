using labasss1.Models;

namespace labasss1.Repositories;

/// <summary>
/// Содержимое файла списка: студенты и номер сохранения (ревизия). При одном сохранении все три
/// файла получают одинаковую ревизию, поэтому после сбоя записи видно, какой файл новее.
/// Ревизия 0 — файл старой версии, где номера ещё не было.
/// </summary>
public record StudentFileContent(List<Student> Students, long Revision);

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
    StudentFileContent Read(string path);

    void Write(string path, StudentFileContent content);
}
