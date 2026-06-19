using System.IO;
using System.Text.Json;

namespace mdv.Services;

/// <summary>
/// Persists the "most recently used" file list to a small JSON file under
/// <c>%APPDATA%\mdv\recent.json</c>. All operations are best-effort: a corrupt or
/// unwritable store never throws into the UI.
/// </summary>
public static class RecentFilesService
{
    /// <summary>Maximum number of entries kept in the MRU list.</summary>
    public const int MaxItems = 5;

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mdv",
        "recent.json");

    public static List<string> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list is not null)
                    return list.Take(MaxItems).ToList();
            }
        }
        catch
        {
            // Ignore a missing/corrupt/unreadable store and start fresh.
        }

        return new List<string>();
    }

    public static void Save(IEnumerable<string> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var json = JsonSerializer.Serialize(items.Take(MaxItems).ToList());
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Persisting the MRU is non-essential; never fail the app over it.
        }
    }
}
