using System.Windows;
using System.Windows.Documents;
using Markdig.Wpf;

namespace mdv.Services;

/// <summary>
/// A single entry in the document outline: a heading's nesting level, its display
/// text, and a reference to the <see cref="Paragraph"/> it was rendered as so the UI
/// can scroll straight to it.
/// </summary>
public sealed record OutlineItem(int Level, string Text, Paragraph Target);

/// <summary>
/// Extracts a document outline (the heading hierarchy) from a rendered
/// <see cref="FlowDocument"/>. Markdig.Wpf renders each Markdown heading as a
/// <see cref="Paragraph"/> styled with one of <see cref="Styles"/>' HeadingN keys, so
/// a heading is identified by matching its <see cref="FrameworkContentElement.Style"/>
/// against those keyed resources.
/// </summary>
public static class OutlineBuilder
{
    // Resource keys for heading levels 1..6, in order. Resolved against the keyed
    // styles Markdig.Wpf applies (overridden in Themes/MarkdownStyles.xaml).
    private static readonly object[] HeadingStyleKeys =
    {
        Styles.Heading1StyleKey,
        Styles.Heading2StyleKey,
        Styles.Heading3StyleKey,
        Styles.Heading4StyleKey,
        Styles.Heading5StyleKey,
        Styles.Heading6StyleKey,
    };

    /// <summary>
    /// Walks <paramref name="document"/> in reading order and returns one
    /// <see cref="OutlineItem"/> per heading. Returns an empty list when the document
    /// is null or has no headings.
    /// </summary>
    public static IReadOnlyList<OutlineItem> Build(FlowDocument? document)
    {
        if (document is null)
            return Array.Empty<OutlineItem>();

        // Map each heading Style instance to its level (1-based). Resolved once here so
        // we compare object references rather than re-querying resources per paragraph.
        var levelByStyle = new Dictionary<Style, int>();
        for (var i = 0; i < HeadingStyleKeys.Length; i++)
        {
            if (Application.Current?.TryFindResource(HeadingStyleKeys[i]) is Style style)
                levelByStyle[style] = i + 1;
        }

        var items = new List<OutlineItem>();
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.Style is not { } style || !levelByStyle.TryGetValue(style, out var level))
                continue;

            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            if (text.Length == 0)
                continue;

            items.Add(new OutlineItem(level, text, paragraph));
        }

        return items;
    }
}
