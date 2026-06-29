namespace mdv.Services;

/// <summary>
/// The user's persisted runtime preferences, loaded once at startup and saved whenever a
/// setting changes (see <see cref="PreferencesService"/>). This is the single, extensible
/// home for every cross-session setting: new options are added as plain properties with a
/// sensible default, and the JSON store tolerates missing or unknown members so older and
/// newer files stay mutually compatible.
/// </summary>
public sealed class Preferences
{
    /// <summary>
    /// Whether the viewer reloads the open document automatically when its file changes on
    /// disk. On by default so mdv behaves like a live preview out of the box; the user can
    /// turn it off from the View menu.
    /// </summary>
    public bool AutoReload { get; set; } = true;
}
