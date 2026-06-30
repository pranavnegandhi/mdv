using System.Windows.Media.Imaging;

namespace mdv.Services;

/// <summary>
/// Embedded offline fallback comic: xkcd #1909 "Digital Resource Lifespan".
/// The PNG is bundled inside the assembly as a WPF <c>Resource</c> so it is
/// always available with no network or disk access required.
/// </summary>
public static class XkcdFallback
{
    private const string PackUri = "pack://application:,,,/Resources/digital_resource_lifespan.png";

    /// <summary>The hardcoded metadata for xkcd #1909.</summary>
    public static readonly XkcdComic Comic = new(
        Num: 1909,
        Title: "Digital Resource Lifespan",
        Alt: "I spent a long time thinking about how to design a system for long-term organization and storage of subject-specific informational resources without needing ongoing work from the experts who created them, only to realized I'd just reinvented libraries.",
        ImageUrl: PackUri,
        PageUrl: "https://xkcd.com/1909/");

    /// <summary>
    /// Loads the embedded fallback image as a frozen <see cref="BitmapImage"/>.
    /// </summary>
    public static BitmapImage LoadImage()
    {
        var bitmap = new BitmapImage(new Uri(PackUri, UriKind.Absolute));
        bitmap.Freeze();
        return bitmap;
    }
}
