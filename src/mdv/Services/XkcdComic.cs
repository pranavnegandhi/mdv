namespace mdv.Services;

/// <summary>
/// Immutable data record for a single xkcd comic, used by both the live fetch path
/// and the embedded offline fallback.
/// </summary>
/// <param name="Num">The comic number (e.g. 1909).</param>
/// <param name="Title">The comic's title text.</param>
/// <param name="Alt">The comic's "title text" (alt text), shown as a tooltip.</param>
/// <param name="ImageUrl">URL or pack URI of the comic's image.</param>
/// <param name="PageUrl">Canonical xkcd page URL, e.g. <c>https://xkcd.com/1909/</c>.</param>
public record XkcdComic(int Num, string Title, string Alt, string ImageUrl, string PageUrl);
