using Markdig;
using Markdig.Syntax;
using Markdig.Wpf;
using mdv.Services;

namespace mdv.Tests;

/// <summary>
/// Parse-layer tests for issue #26: inline SVG must be recognized as a single block
/// regardless of whether it is on one physical line or contains blank lines. These assert
/// the parse tree only — no WPF rendering — so they run without an STA thread.
/// </summary>
public sealed class SvgBlockParserTests
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Use(new SvgExtension()).Build();

    private static List<SvgBlock> ParseSvgBlocks(string markdown) =>
        Markdig.Markdown.Parse(markdown, Pipeline).Descendants<SvgBlock>().ToList();

    [Fact]
    public void SingleLineSvg_SurroundedByBlankLines_IsOneSvgBlock()
    {
        // The core #26 case: a whole <svg>…</svg> on one physical line — what PlantUML emits.
        const string md = "a\n\n<svg width='10' height='10'><rect/></svg>\n\nb";

        var blocks = ParseSvgBlocks(md);

        var svg = Assert.Single(blocks);
        Assert.Contains("<rect/>", svg.GetMarkup());
        Assert.Contains("</svg>", svg.GetMarkup());
    }

    [Fact]
    public void SvgWithInternalBlankLines_IsOneSvgBlock_WithFullMarkup()
    {
        // Second facet of #26: a blank line inside the SVG must NOT truncate the block.
        const string md =
            "a\n\n<svg width='10' height='10'>\n<rect/>\n\n<metadata>keep me</metadata>\n</svg>\n\nb";

        var blocks = ParseSvgBlocks(md);

        var svg = Assert.Single(blocks);
        var markup = svg.GetMarkup();
        Assert.Contains("<rect/>", markup);
        // The content AFTER the internal blank line must survive in the same block.
        Assert.Contains("<metadata>keep me</metadata>", markup);
        Assert.Contains("</svg>", markup);
    }

    [Fact]
    public void PlantUmlStyleSvg_WithProcessingInstructionAndDefs_IsOneSvgBlock()
    {
        // A realistic PlantUML-server payload: single line, leading <?plantuml?> PI, empty <defs/>.
        // The markup must be captured verbatim — nothing stripped (see issue #28 constraint).
        const string md =
            "before\n\n<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" +
            "<?plantuml 1.2026.7beta8?><defs/><g><rect/></g></svg>\n\nafter";

        var blocks = ParseSvgBlocks(md);

        var svg = Assert.Single(blocks);
        var markup = svg.GetMarkup();
        Assert.Contains("<?plantuml 1.2026.7beta8?>", markup);
        Assert.Contains("<defs/>", markup);
        Assert.Contains("</svg>", markup);
    }

    [Fact]
    public void SvgWithOpeningTagOnOwnLine_StillIsOneSvgBlock()
    {
        // Regression guard: the layout that ALREADY worked before the fix must keep working.
        const string md = "a\n\n<svg width='10' height='10'>\n<rect/>\n</svg>\n\nb";

        var blocks = ParseSvgBlocks(md);

        var svg = Assert.Single(blocks);
        Assert.Contains("<rect/>", svg.GetMarkup());
    }

    [Fact]
    public void NonSvgHtmlBlock_IsNotCapturedAsSvgBlock()
    {
        // A plain HTML block must fall through to Markdig's HtmlBlockParser untouched.
        const string md = "a\n\n<div>\nhello\n</div>\n\nb";

        var document = Markdig.Markdown.Parse(md, Pipeline);

        Assert.Empty(document.Descendants<SvgBlock>());
        Assert.NotEmpty(document.Descendants<HtmlBlock>());
    }

    [Fact]
    public void InlineSvgInsideAParagraph_IsNotCapturedAsBlock()
    {
        // A <svg> in the middle of a sentence is not a block start; it must not become an SvgBlock.
        const string md = "text before <svg><rect/></svg> text after";

        Assert.Empty(ParseSvgBlocks(md));
    }
}
