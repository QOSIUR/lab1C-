using System.IO;

namespace labasss1.Repositories;

/// <summary>
/// Следит за файлами списка, пока программа работает: если файл удалили, переименовали
/// или изменили снаружи, через короткую паузу вызывает <c>onChanged</c> — чтобы набор
/// сразу же проверили и восстановили, не дожидаясь следующего сохранения или загрузки.
/// </summary>
public sealed class StudentSetWatcher : IDisposable
{
    /// <summary>Пауза, чтобы серия событий от одной записи файла превратилась в одну проверку.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _names;
    private readonly Action _onChanged;
    private readonly SynchronizationContext? _context;
    private readonly Timer _timer;

    /// <param name="filePaths">Пути всех файлов, за которыми следить (могут быть в разных папках).</param>
    /// <param name="onChanged">Вызывается в контексте синхронизации, в котором создан наблюдатель (UI-поток).</param>
    public StudentSetWatcher(IEnumerable<string> filePaths, Action onChanged)
    {
        _onChanged = onChanged;
        _context = SynchronizationContext.Current;
        _timer = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);

        var paths = filePaths.Select(Path.GetFullPath).ToList();
        _names = paths.Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;

            var watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnEvent;
            // Переполнение буфера событий — что-то могли пропустить, проверим на всякий случай.
            watcher.Error += (_, _) => Schedule();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        // Временные файлы (.tmp) и чужие файлы в той же папке не интересуют.
        if (_names.Contains(e.Name ?? string.Empty) ||
            (e is RenamedEventArgs r && _names.Contains(r.OldName ?? string.Empty)))
            Schedule();
    }

    private void Schedule() => _timer.Change(Debounce, Timeout.InfiniteTimeSpan);

    private void Raise()
    {
        if (_context != null) _context.Post(_ => _onChanged(), null);
        else _onChanged();
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _timer.Dispose();
    }
}
