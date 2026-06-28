using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;
using Markdig.Syntax;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace mdv.Services;

/// <summary>
/// Renders a block-level inline <c>&lt;svg&gt;…&lt;/svg&gt;</c> (which Markdig parses into an
/// <see cref="HtmlBlock"/>) into the <see cref="FlowDocument"/>. Markdig.Wpf's renderer
/// registers no <see cref="HtmlBlock"/> handler at all, so without this every SVG block is
/// silently dropped; registering this renderer is the documented interception point.
/// </summary>
/// <remarks>
/// The SVG is rasterised to WPF visuals with SharpVectors (in-memory, from the raw block
/// text) and hosted in a <see cref="BlockUIContainer"/>. Only <see cref="HtmlBlock"/>s that
/// actually look like SVG are handled; every other raw-HTML block is left untouched, so
/// non-SVG HTML stays dropped exactly as it is today. A block that fails to render degrades
/// to a quiet inline placeholder rather than aborting the whole document render.
/// </remarks>
internal sealed class SvgHtmlBlockRenderer : WpfObjectRenderer<HtmlBlock>
{
    private static readonly Regex ViewBoxPattern = new(
        @"viewBox\s*=\s*[""']\s*(?<v>[^""']+?)\s*[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WidthPattern = new(
        @"\bwidth\s*=\s*[""']\s*(?<n>[\d.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeightPattern = new(
        @"\bheight\s*=\s*[""']\s*(?<n>[\d.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected override void Write(WpfRenderer renderer, HtmlBlock obj)
    {
        if (renderer is null) throw new ArgumentNullException(nameof(renderer));
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        var markup = GetRawText(obj);

        // Only handle blocks that are actually SVG; leave all other raw HTML alone (it stays
        // dropped, as before). This keeps the change narrowly scoped to the feature.
        if (!IsSvg(markup))
            return;

        try
        {
            renderer.WriteBlock(BuildSvgBlock(markup));
        }
        catch
        {
            // One malformed diagram must never blank the rest of the document.
            renderer.WriteBlock(BuildPlaceholder());
        }
    }

    /// <summary>
    /// Reassembles the block's verbatim source. Markdig keeps the raw lines on
    /// <see cref="LeafBlock.Lines"/> because HTML parsing is enabled (the pipeline never
    /// calls <c>DisableHtml()</c>).
    /// </summary>
    private static string GetRawText(HtmlBlock obj)
    {
        var lines = obj.Lines.Lines;
        if (lines is null)
            return string.Empty;

        var builder = new System.Text.StringBuilder();
        // StringLineGroup over-allocates its backing array; Count is the live line count.
        for (var i = 0; i < obj.Lines.Count; i++)
            builder.AppendLine(lines[i].Slice.ToString());

        return builder.ToString();
    }

    private static bool IsSvg(string markup)
    {
        var trimmed = markup.AsSpan().TrimStart();
        return trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            && markup.Contains("</svg", StringComparison.OrdinalIgnoreCase);
    }

    private static BlockUIContainer BuildSvgBlock(string markup)
    {
        var settings = new WpfDrawingSettings();
        var reader = new FileSvgReader(settings);

        DrawingGroup drawing;
        using (var sr = new StringReader(markup))
            drawing = reader.Read(sr);

        if (drawing is null)
            throw new InvalidOperationException("SharpVectors returned no drawing.");

        var (width, height) = ResolveSize(markup, drawing);

        // SharpVectors' drawing bounds track the ink, not the SVG viewport, so text near a
        // viewBox edge can sit slightly outside the bounds and get clipped. Anchor the drawing
        // onto a transparent rectangle the size of the viewport so the rendered surface is the
        // full declared canvas and nothing is clipped.
        var canvas = new DrawingGroup();
        canvas.Children.Add(new GeometryDrawing(
            Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, width, height))));
        canvas.Children.Add(drawing);
        canvas.Freeze();

        var image = new Image
        {
            Source = new DrawingImage(canvas),
            Stretch = Stretch.Uniform,
            // Cap at the natural size so a diagram is never upscaled (mirroring image handling);
            // Stretch.Uniform still scales it down to fit a narrower reading column.
            MaxWidth = width,
            MaxHeight = height,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        return new BlockUIContainer(image) { Margin = new Thickness(0, 0, 0, 10) };
    }

    /// <summary>
    /// Picks the rendered canvas size: the <c>viewBox</c> dimensions first, then explicit
    /// <c>width</c>/<c>height</c> attributes, then SharpVectors' computed bounds as a last resort.
    /// </summary>
    private static (double Width, double Height) ResolveSize(string markup, DrawingGroup drawing)
    {
        var viewBox = ViewBoxPattern.Match(markup);
        if (viewBox.Success)
        {
            var parts = viewBox.Groups["v"].Value.Split(
                new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbW)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbH)
                && vbW > 0 && vbH > 0)
            {
                return (vbW, vbH);
            }
        }

        var width = ParseLength(WidthPattern, markup);
        var height = ParseLength(HeightPattern, markup);
        if (width > 0 && height > 0)
            return (width, height);

        var bounds = drawing.Bounds;
        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            return (bounds.Width, bounds.Height);

        return (300, 150); // SVG's default intrinsic size when nothing else is known.
    }

    private static double ParseLength(Regex pattern, string markup)
    {
        var match = pattern.Match(markup);
        return match.Success
            && double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>
    /// A quiet, theme-aware stand-in shown when an SVG block cannot be rendered, so a single
    /// bad diagram is visible-but-harmless instead of silently missing or fatal.
    /// </summary>
    private static BlockUIContainer BuildPlaceholder()
    {
        var label = new TextBlock
        {
            Text = "⚠ Could not render SVG diagram",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = SystemColors.GrayTextBrush,
        };
        return new BlockUIContainer(label) { Margin = new Thickness(0, 0, 0, 10) };
    }
}
