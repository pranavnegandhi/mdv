using System.IO;
using System.Text.Json;

namespace mdv.Services;

/// <summary>
/// Persists the user's <see cref="Preferences"/> to a small JSON file under
/// <c>%APPDATA%\mdv\preferences.json</c> — the same directory the recent-files store uses.
/// All operations are best-effort: a missing, corrupt, or unwritable store never throws into
/// the UI; on any load failure the defaults from a fresh <see cref="Preferences"/> are returned.
/// </summary>
public static class PreferencesService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mdv",
        "preferences.json");

    // camelCase on the wire so the file reads naturally (e.g. {"autoReload": true}) and stays
    // consistent as #12/#13 add their own settings.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Loads saved preferences, or returns defaults when none exist or the store is unreadable.</summary>
    public static Preferences Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var prefs = JsonSerializer.Deserialize<Preferences>(json, Options);
                if (prefs is not null)
                    return prefs;
            }
        }
        catch
        {
            // Ignore a missing/corrupt/unreadable store and start from defaults.
        }

        return new Preferences();
    }

    /// <summary>Writes the given preferences to disk. Failures are swallowed; persistence is non-essential.</summary>
    public static void Save(Preferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var json = JsonSerializer.Serialize(preferences, Options);
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Persisting preferences is non-essential; never fail the app over it.
        }
    }
}
