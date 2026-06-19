using System.IO;
using System.Windows;

namespace mdv;

/// <summary>
/// Interaction logic for App.xaml.
/// Handles startup manually (instead of StartupUri) so a file path passed on the
/// command line — e.g. <c>mdv.exe sample.md</c> — is opened on launch.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();

        // "mdv --follow [project-path]" launches straight into live Claude-session mirroring,
        // scoped to the given project (or the current working directory when omitted).
        var followIndex = Array.FindIndex(
            e.Args, a => string.Equals(a, "--follow", StringComparison.OrdinalIgnoreCase));
        var follow = followIndex >= 0;

        string? followPath = null;
        if (follow
            && followIndex + 1 < e.Args.Length
            && !e.Args[followIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            followPath = e.Args[followIndex + 1];
        }

        if (follow)
        {
            window.Show();
            window.EnableFollow(followPath);
        }
        else
        {
            var fileArg = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            if (fileArg is not null)
            {
                // Resolve relative paths (e.g. from the Run dialog) against the working directory.
                window.OpenFile(Path.GetFullPath(fileArg));
            }

            window.Show();
        }
    }
}
