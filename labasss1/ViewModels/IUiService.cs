namespace labasss1.ViewModels;

/// <summary>
/// Действия, которые требуют окон WPF (диалоги, другие окна). ViewModel вызывает их через этот
/// интерфейс, поэтому одна и та же команда работает и из кнопки, и из меню, и по горячей клавише.
/// </summary>
public interface IUiService
{
    /// <summary>
    /// Показывает диалог выбора файла и возвращает путь или null, если пользователь отказался.
    /// <paramref name="mustExist"/> = false позволяет указать ещё не созданный файл.
    /// </summary>
    string? PickFile(string title, string currentPath, bool mustExist);

    /// <summary>Открывает окно отчисленных студентов (или активирует уже открытое).</summary>
    void ShowExpelledWindow();

    /// <summary>Открывает окно справки (или активирует уже открытое).</summary>
    void ShowHelp();

    /// <summary>Показывает сведения о программе.</summary>
    void ShowAbout();

    /// <summary>Закрывает главное окно — завершает программу.</summary>
    void CloseMainWindow();
}
