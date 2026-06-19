using System.IO;
using System.Windows.Threading;

namespace mdv.Services;

/// <summary>
/// Watches one project's Claude session folder under <c>%LOCALAPPDATA%\mdv\sessions\&lt;slug&gt;</c>
/// and reports when the newest <c>.md</c> file changes — either because the active session had a
/// new response appended, or because a newer session started a newer file. The path that should
/// currently be displayed is delivered through <see cref="Changed"/>.
///
/// File-system notifications arrive on a thread-pool thread and can burst while a file is written;
/// they are marshalled onto the owning <see cref="Dispatcher"/> and debounced so the UI reloads
/// once per quiet interval.
/// </summary>
public sealed class ClaudeSessionWatcher : IDisposable
{
    /// <summary>Root the Claude <c>Stop</c> hook writes all per-project session folders under.</summary>
    public static string SessionsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "mdv", "sessions");

    /// <summary>
    /// Maps a project directory to the sessions subfolder the <c>Stop</c> hook writes into.
    /// This slug rule MUST stay in lockstep with the hook (stop.ps1): full path, trailing
    /// separators trimmed, lower-cased, with ':' '\' '/' all replaced by '-'
    /// (e.g. <c>D:\projects\mdv</c> -&gt; <c>d--projects-mdv</c>).
    /// </summary>
    public static string ProjectDirectory(string projectPath)
    {
        var full = Path.GetFullPath(projectPath).TrimEnd('\\', '/').ToLowerInvariant();
        var slug = full.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        return Path.Combine(SessionsRoot, slug);
    }

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly string _directory;
    private FileSystemWatcher? _watcher;
    private string? _pendingPath;

    /// <summary>Raised on the UI thread with the path of the session file to display.</summary>
    public event Action<string>? Changed;

    public ClaudeSessionWatcher(Dispatcher dispatcher, string directory)
    {
        _dispatcher = dispatcher;
        _directory = directory;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _debounce.Tick += OnDebounceTick;
    }

    /// <summary>Begins watching and returns the newest existing session file, or null if none.</summary>
    public string? Start()
    {
        Directory.CreateDirectory(_directory);

        _watcher = new FileSystemWatcher(_directory, "*.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                           | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;

        return Newest();
    }

    /// <summary>Returns the most-recently-written <c>.md</c> file in the watched folder, or null.</summary>
    public string? Newest()
    {
        if (!Directory.Exists(_directory))
            return null;

        return new DirectoryInfo(_directory)
            .GetFiles("*.md")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        // Coalesce the burst onto the UI thread; the latest newest-file wins.
        _dispatcher.BeginInvoke(() =>
        {
            _pendingPath = Newest();
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
