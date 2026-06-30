using System.IO;
using System.Text.Json;

namespace mdv.Services;

/// <summary>
/// Per-day disk cache for xkcd comics under <c>%LOCALAPPDATA%\mdv\xkcd\</c>.
/// All operations are best-effort: any exception causes the method to return
/// <see langword="null"/> so the caller falls back to the embedded offline comic.
/// </summary>
public static class XkcdCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "mdv",
        "xkcd");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Returns today's cached <see cref="XkcdComic"/>, or fetches, caches, and returns it on a
    /// cache miss. Returns <see langword="null"/> on any failure (offline, parse error, etc.).
    /// </summary>
    /// <param name="today">The date to resolve a comic for (typically <c>DateOnly.FromDateTime(DateTime.Today)</c>).</param>
    public static async Task<XkcdComic?> GetForTodayAsync(DateOnly today)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var jsonFile = Path.Combine(CacheDir, $"{today:yyyy-MM-dd}.json");

            // Cache hit: both the JSON and the image file must exist.
            if (File.Exists(jsonFile))
            {
                var cached = TryLoadCacheEntry(jsonFile);
                if (cached is not null)
                {
                    var imageFile = Path.Combine(CacheDir, cached.ImageFile);
                    if (File.Exists(imageFile))
                        return new XkcdComic(cached.Num, cached.Title, cached.Alt, imageFile, cached.PageUrl);
                }
            }

            // Cache miss: fetch from the network.
            var latest = await XkcdClient.GetLatestNumberAsync().ConfigureAwait(false);
            if (latest <= 0)
                return null;

            var id = XkcdComicSelector.Select(today, latest);
            var comic = await XkcdClient.GetComicAsync(id).ConfigureAwait(false);
            var imageBytes = await XkcdClient.DownloadImageAsync(comic.ImageUrl).ConfigureAwait(false);

            // Derive the local image filename from the remote URL's extension.
            var ext = Path.GetExtension(comic.ImageUrl);
            if (string.IsNullOrEmpty(ext))
                ext = ".png";
            var imageFileName = $"{today:yyyy-MM-dd}{ext}";
            var localImagePath = Path.Combine(CacheDir, imageFileName);

            await File.WriteAllBytesAsync(localImagePath, imageBytes).ConfigureAwait(false);

            var entry = new CacheEntry
            {
                Num = comic.Num,
                Title = comic.Title,
                Alt = comic.Alt,
                PageUrl = comic.PageUrl,
                ImageFile = imageFileName,
            };
            await File.WriteAllTextAsync(jsonFile, JsonSerializer.Serialize(entry, JsonOptions))
                      .ConfigureAwait(false);

            // Prune stale dated files, keeping only today's entry.
            PruneStaleEntries(today);

            // Return the comic with the local image path so the caller can build a BitmapImage.
            return new XkcdComic(comic.Num, comic.Title, comic.Alt, localImagePath, comic.PageUrl);
        }
        catch
        {
            // Best-effort: never throw into the UI. Caller shows the embedded fallback.
            return null;
        }
    }

    /// <summary>Returns the local image file path for today's cached comic, if present.</summary>
    public static string? GetCachedImagePath(DateOnly today)
    {
        try
        {
            var jsonFile = Path.Combine(CacheDir, $"{today:yyyy-MM-dd}.json");
            if (!File.Exists(jsonFile))
                return null;

            var entry = TryLoadCacheEntry(jsonFile);
            if (entry is null)
                return null;

            var imageFile = Path.Combine(CacheDir, entry.ImageFile);
            return File.Exists(imageFile) ? imageFile : null;
        }
        catch
        {
            return null;
        }
    }

    private static CacheEntry? TryLoadCacheEntry(string jsonFile)
    {
        try
        {
            var json = File.ReadAllText(jsonFile);
            return JsonSerializer.Deserialize<CacheEntry>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void PruneStaleEntries(DateOnly today)
    {
        try
        {
            var todayJson = $"{today:yyyy-MM-dd}.json";
            var todayImg = $"{today:yyyy-MM-dd}";

            foreach (var file in Directory.EnumerateFiles(CacheDir))
            {
                var name = Path.GetFileName(file);
                // Keep today's JSON and image; remove everything else that looks like a dated entry.
                if (name == todayJson)
                    continue;
                if (name.StartsWith(todayImg, StringComparison.Ordinal))
                    continue;

                // Only remove files that match the dated naming pattern (yyyy-MM-dd.*).
                if (name.Length >= 10 && name[4] == '-' && name[7] == '-' &&
                    int.TryParse(name[..4], out _))
                {
                    try { File.Delete(file); } catch { /* best-effort */ }
                }
            }
        }
        catch
        {
            // Pruning is non-essential; swallow all errors.
        }
    }

    /// <summary>JSON-serializable shape stored in the per-day cache file.</summary>
    private sealed class CacheEntry
    {
        public int Num { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Alt { get; set; } = string.Empty;
        public string PageUrl { get; set; } = string.Empty;
        /// <summary>Image filename within the cache folder (e.g. <c>2026-06-30.png</c>).</summary>
        public string ImageFile { get; set; } = string.Empty;
    }
}
