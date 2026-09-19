using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;

namespace mdv.Services;

/// <summary>
/// Self-contained Markdig extension for Mermaid diagram support. Unlike <see cref="SvgExtension"/>,
/// no block parser is registered: Markdig's own fenced-code-block parsing already produces the
/// <c>FencedCodeBlock</c> this feature needs (<c>Info == "mermaid"</c>), so this extension only
/// wires up the WPF renderer.
/// </summary>
internal sealed class MermaidExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // No block parser to register — nothing to do here.
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Only the WPF renderer draws diagrams; other renderers (e.g. HTML) leave the block alone.
        if (renderer is not WpfRenderer wpf || wpf.ObjectRenderers.Contains<MermaidBlockRenderer>())
            return;

        // Must run before CodeBlockRenderer: Markdig's renderer dispatch picks the FIRST
        // registered renderer whose type matches an object, so MermaidBlockRenderer must be
        // tried first and itself decide (by Info) whether to render a diagram or fall through.
        if (!wpf.ObjectRenderers.InsertBefore<CodeBlockRenderer>(new MermaidBlockRenderer()))
            wpf.ObjectRenderers.Insert(0, new MermaidBlockRenderer());
    }
}
