using System.Text;
using System.Windows;
using System.Windows.Documents;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;
using Markdig.Syntax;
using Markdig.Wpf;
using Mermaider;

namespace mdv.Services;

/// <summary>
/// Renders a fenced code block tagged <c>mermaid</c> as a diagram (via Mermaider, then the
/// shared <see cref="SvgRendering"/> path); any other fenced code block falls
/// through to the same appearance Markdig.Wpf's own <c>CodeBlockRenderer</c> produces.
/// </summary>
/// <remarks>
/// Markdig's renderer dispatch (<c>RendererBase.Write</c>) picks the first registered renderer
/// whose type matches, and matching is type-only — a renderer cannot decline an instance by
/// content. This renderer must therefore be registered before <c>CodeBlockRenderer</c> (see
/// <see cref="MermaidExtension"/>) and itself branch on <see cref="FencedCodeBlock.Info"/>.
/// </remarks>
internal sealed class MermaidBlockRenderer : WpfObjectRenderer<FencedCodeBlock>
{
    protected override void Write(WpfRenderer renderer, FencedCodeBlock obj)
    {
        if (renderer is null) throw new ArgumentNullException(nameof(renderer));
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        if (!string.Equals(obj.Info?.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase))
        {
            WriteDefaultCodeBlock(renderer, obj);
            return;
        }

        try
        {
            var svg = MermaidRenderer.RenderSvg(GetSource(obj));
            var resolved = MermaidSvgTheming.ResolveCssVariables(svg);
            renderer.WriteBlock(SvgRendering.Build(resolved));
        }
        catch
        {
            // One malformed diagram must never blank the rest of the document.
            renderer.WriteBlock(SvgRendering.BuildPlaceholder("⚠ Could not render Mermaid diagram"));
        }
    }

    /// <summary>
    /// Reproduces Markdig.Wpf's own <c>CodeBlockRenderer.Write</c> exactly, so any fenced code
    /// block that isn't tagged <c>mermaid</c> is visually unaffected by this renderer's presence
    /// in the pipeline.
    /// </summary>
    private static void WriteDefaultCodeBlock(WpfRenderer renderer, FencedCodeBlock obj)
    {
        var paragraph = new Paragraph();
        paragraph.SetResourceReference(FrameworkContentElement.StyleProperty, Styles.CodeBlockStyleKey);
        renderer.Push(paragraph);
        renderer.WriteLeafRawLines(obj);
        renderer.Pop();
    }

    /// <summary>
    /// Reassembles the block's verbatim source text (the raw Mermaid diagram definition, without
    /// the surrounding ``` fence) from its captured lines, mirroring <see cref="SvgBlock.GetMarkup"/>.
    /// </summary>
    private static string GetSource(FencedCodeBlock obj)
    {
        var lines = obj.Lines.Lines;
        if (lines is null)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < obj.Lines.Count; i++)
            builder.AppendLine(lines[i].Slice.ToString());

        return builder.ToString();
    }
}
