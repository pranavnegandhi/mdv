using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace mdv.Services;

/// <summary>
/// Shared SharpVectors-based SVG-to-<see cref="BlockUIContainer"/> rendering path, used by both
/// <see cref="SvgBlockRenderer"/> (inline &lt;svg&gt; blocks, #26) and
/// <see cref="MermaidBlockRenderer"/> (Mermaid diagrams rendered to SVG by Mermaider, #33), so
/// both share one size-resolution strategy and one placeholder look.
/// </summary>
internal static class SvgRendering
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

    /// <summary>
    /// Parses <paramref name="markup"/> as SVG and returns a sized, non-upscaling image wrapped
    /// in a <see cref="BlockUIContainer"/>. Throws if SharpVectors cannot parse the markup —
    /// callers catch this and fall back to <see cref="BuildPlaceholder"/>.
    /// </summary>
    internal static BlockUIContainer Build(string markup)
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
    /// A quiet, theme-aware stand-in shown when a diagram cannot be rendered, so one bad diagram
    /// is visible-but-harmless instead of silently missing or fatal. Callers supply their own
    /// wording so the placeholder names the feature that failed (SVG vs. Mermaid).
    /// </summary>
    internal static BlockUIContainer BuildPlaceholder(string label)
    {
        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = SystemColors.GrayTextBrush,
        };
        return new BlockUIContainer(text) { Margin = new Thickness(0, 0, 0, 10) };
    }
}
