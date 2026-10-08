using System.Text;

namespace labasss1.Models;

/// <summary>
/// Приводит текстовые поля студента к виду, который одинаково хранится во всех форматах файлов.
/// Без этого, например, перевод строки (вставленный из буфера обмена) в текстовом файле
/// превращался бы в пробел, а в бинарном сохранялся — и файлы расходились бы по содержимому.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Управляющие символы (переводы строк, табуляция и т. п.) заменяет пробелами, одиночные
    /// половинки суррогатных пар — символом «�» (UTF-8 их всё равно не хранит), обрезает пробелы по краям.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        StringBuilder? sb = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            char? replacement = null;
            if (char.IsControl(c))
            {
                replacement = ' ';
            }
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }
            else if (char.IsSurrogate(c))
            {
                replacement = '�';
            }

            if (replacement != null && sb == null)
                sb = new StringBuilder(value, 0, i, value.Length);
            sb?.Append(replacement ?? c);
        }

        return (sb?.ToString() ?? value).Trim();
    }

    /// <summary>Пусто ли поле после нормализации (например, состоит только из пробелов и переводов строк).</summary>
    public static bool IsBlank(string? value) => Normalize(value).Length == 0;
}
