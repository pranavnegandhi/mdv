using System.Windows.Documents;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;

namespace mdv.Services;

/// <summary>
/// Renders an <see cref="SvgBlock"/> (a complete inline <c>&lt;svg&gt;…&lt;/svg&gt;</c> captured by
/// <see cref="SvgBlockParser"/>) into the <see cref="FlowDocument"/>. Markdig.Wpf ships no
/// renderer for SVG at all, so without this the block is silently dropped.
/// </summary>
/// <remarks>
/// The parser guarantees the block is a whole SVG, so no content sniffing is needed here. Actual
/// parsing/sizing/placeholder logic lives in <see cref="SvgRendering"/>, shared with
/// <see cref="MermaidBlockRenderer"/>.
/// </remarks>
internal sealed class SvgBlockRenderer : WpfObjectRenderer<SvgBlock>
{
    protected override void Write(WpfRenderer renderer, SvgBlock obj)
    {
        if (renderer is null) throw new ArgumentNullException(nameof(renderer));
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        var markup = obj.GetMarkup();

        try
        {
            renderer.WriteBlock(SvgRendering.Build(markup));
        }
        catch
        {
            // One malformed diagram must never blank the rest of the document.
            renderer.WriteBlock(SvgRendering.BuildPlaceholder("⚠ Could not render SVG diagram"));
        }
    }
}
