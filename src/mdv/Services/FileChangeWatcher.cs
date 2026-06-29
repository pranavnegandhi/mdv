using System.IO;
using System.Windows.Threading;

namespace mdv.Services;

/// <summary>
/// Watches a folder for changes to files matching a name filter and reports which path should
/// now be displayed. The rule for "which path" is supplied by the caller as a <em>picker</em>,
/// which is the only thing that differs between use cases:
/// <list type="bullet">
///   <item>Claude follow mode picks the newest <c>.md</c> file in a session folder, so the
///   shown file can switch as new sessions start.</item>
///   <item>Single-file auto-reload picks one fixed path if it still exists, else null.</item>
/// </list>
///
/// File-system notifications arrive on a thread-pool thread and can burst while a file is
/// written; they are marshalled onto the owning <see cref="Dispatcher"/> and debounced so the
/// UI reloads once per quiet interval. The picker is evaluated on the UI thread as each event
/// arrives, so the latest decision wins when the debounce finally fires.
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly string _directory;
    private readonly string _filter;
    private readonly Func<string?> _picker;
    private FileSystemWatcher? _watcher;
    private string? _pendingPath;

    /// <summary>Raised on the UI thread with the path the picker selected after a change.</summary>
    public event Action<string>? Changed;

    /// <param name="dispatcher">UI dispatcher events are marshalled onto.</param>
    /// <param name="directory">Folder to watch (not created here — the caller owns that).</param>
    /// <param name="filter">File-name filter, e.g. <c>*.md</c> or a single file's name.</param>
    /// <param name="picker">Returns the path to display now, or null when there is nothing to show.</param>
    public FileChangeWatcher(Dispatcher dispatcher, string directory, string filter, Func<string?> picker)
    {
        _dispatcher = dispatcher;
        _directory = directory;
        _filter = filter;
        _picker = picker;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _debounce.Tick += OnDebounceTick;
    }

    /// <summary>Begins watching and returns the picker's current choice, or null.</summary>
    public string? Start()
    {
        _watcher = new FileSystemWatcher(_directory, _filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                           | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;

        return _picker();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        // Coalesce the burst onto the UI thread; the picker's latest choice wins.
        _dispatcher.BeginInvoke(() =>
        {
            _pendingPath = _picker();
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        if (_pendingPath is { } path)
            Changed?.Invoke(path);
    }

    public void Dispose()
    {
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileSystemEvent;
            _watcher.Created -= OnFileSystemEvent;
            _watcher.Renamed -= OnFileSystemEvent;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
