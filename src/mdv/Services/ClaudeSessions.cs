using System.IO;

namespace mdv.Services;

/// <summary>
/// Claude-specific session-folder rules, kept out of the general <see cref="FileChangeWatcher"/>
/// so the watcher stays use-case agnostic. The <c>Stop</c> hook (stop.ps1) writes one Markdown
/// file per response into a per-project folder under <c>%LOCALAPPDATA%\mdv\sessions\&lt;slug&gt;</c>;
/// follow mode mirrors the newest such file.
/// </summary>
public static class ClaudeSessions
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

    /// <summary>Returns the most-recently-written <c>.md</c> file in <paramref name="directory"/>, or null.</summary>
    public static string? Newest(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        return new DirectoryInfo(directory)
            .GetFiles("*.md")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }
}
