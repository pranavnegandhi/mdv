using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace mdv.Services;

/// <summary>
/// An opt-in <see cref="FlowDocument"/> transform for the live Claude-session view.
/// It restyles the standalone italic time lines the session hook appends ("_11:23 AM_")
/// into quiet, right-aligned chat-style captions.
///
/// This is deliberately separate from <see cref="MarkdownDocumentLoader"/>: the loader
/// renders any Markdown verbatim, and only the follow-session path opts into this
/// transform — opening an ordinary Markdown file is never affected.
/// </summary>
public static class ChatTimestampStyler
{
    // A lone-italic paragraph that looks like a clock time. Permissive across locales:
    // H:MM (or H.MM), optional seconds, optional AM/PM-style designator.
    private static readonly Regex TimestampPattern = new(
        @"^\d{1,2}[:.]\d{2}([:.]\d{2})?(\s?\p{L}{1,5})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Same sans-serif family as the body (no new typeface introduced).
    private static readonly FontFamily TimestampFont = new("Segoe UI");

    /// <summary>
    /// Restyles each top-level paragraph that consists solely of an italic clock time so
    /// the appended chat timestamps read as small, light, right-aligned captions rather
    /// than ordinary emphasised body text. The colour comes from the system theme
    /// (<see cref="SystemColors.GrayTextBrush"/>) so it tracks the user's Windows palette.
    /// </summary>
    public static void Apply(FlowDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;

            // The hook writes the timestamp as its own paragraph holding a single
            // emphasis run (Markdig.Wpf renders "_x_" as one Italic inline).
            if (paragraph.Inlines.Count != 1 || paragraph.Inlines.FirstInline is not Italic)
                continue;

            var content = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            if (!TimestampPattern.IsMatch(content))
                continue;

            paragraph.TextAlignment = TextAlignment.Right;
            paragraph.FontFamily = TimestampFont;
            paragraph.FontSize = 12;
            paragraph.Foreground = SystemColors.GrayTextBrush;
            // Tuck the caption up under the response it belongs to.
            paragraph.Margin = new Thickness(0, 0, 0, 6);
        }
    }
}
