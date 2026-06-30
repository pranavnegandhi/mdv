using System.Net.Http;
using System.Text.Json;

namespace mdv.Services;

/// <summary>
/// Minimal HTTP client for the xkcd JSON API and image downloads.
/// Uses a single shared <see cref="HttpClient"/> with a ~10 s timeout.
/// All methods throw on failure; callers are expected to catch and handle errors.
/// </summary>
public static class XkcdClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <summary>Fetches the latest comic number from <c>https://xkcd.com/info.0.json</c>.</summary>
    /// <returns>The latest comic's <c>num</c> field.</returns>
    /// <exception cref="Exception">Thrown when the request fails or the response cannot be parsed.</exception>
    public static async Task<int> GetLatestNumberAsync()
    {
        var json = await Http.GetStringAsync("https://xkcd.com/info.0.json").ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("num").GetInt32();
    }

    /// <summary>
    /// Fetches metadata for comic <paramref name="num"/> from
    /// <c>https://xkcd.com/{num}/info.0.json</c>.
    /// </summary>
    /// <returns>An <see cref="XkcdComic"/> populated from the API response.</returns>
    /// <exception cref="Exception">Thrown when the request fails or the response cannot be parsed.</exception>
    public static async Task<XkcdComic> GetComicAsync(int num)
    {
        var url = $"https://xkcd.com/{num}/info.0.json";
        var json = await Http.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var title = root.GetProperty("title").GetString() ?? string.Empty;
        var alt = root.GetProperty("alt").GetString() ?? string.Empty;
        var imageUrl = root.GetProperty("img").GetString() ?? string.Empty;
        var pageUrl = $"https://xkcd.com/{num}/";

        return new XkcdComic(num, title, alt, imageUrl, pageUrl);
    }

    /// <summary>Downloads the raw bytes for an image at <paramref name="imageUrl"/>.</summary>
    /// <exception cref="Exception">Thrown when the download fails.</exception>
    public static async Task<byte[]> DownloadImageAsync(string imageUrl)
    {
        return await Http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
    }
}
