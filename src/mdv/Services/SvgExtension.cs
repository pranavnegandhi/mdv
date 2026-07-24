using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;

namespace mdv.Services;

/// <summary>
/// Self-contained Markdig extension for inline SVG support. Registers the parse-layer
/// <see cref="SvgBlockParser"/> and the WPF <see cref="SvgBlockRenderer"/> together so SVG
/// handling is one unit instead of a renderer bolted on at call time.
/// </summary>
internal sealed class SvgExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (pipeline.BlockParsers.Contains<SvgBlockParser>())
            return;

        // Must run before HtmlBlockParser so an <svg line is claimed here, not by HTML detection.
        if (!pipeline.BlockParsers.InsertBefore<HtmlBlockParser>(new SvgBlockParser()))
            pipeline.BlockParsers.Insert(0, new SvgBlockParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // Only the WPF renderer draws SVGs; other renderers (e.g. HTML) leave the block alone.
        if (renderer is WpfRenderer wpf && !wpf.ObjectRenderers.Contains<SvgBlockRenderer>())
            wpf.ObjectRenderers.Add(new SvgBlockRenderer());
    }
}
