using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using Markdig;
using Markdig.Renderers;
using Markdig.Wpf;
using Xunit;
using mdv.Services;

namespace mdv.Tests;

/// <summary>
/// Renderer-dispatch tests for issue #33: a ```mermaid fence renders as a diagram; any other
/// fenced code block is unaffected by MermaidBlockRenderer's presence in the pipeline.
/// </summary>
[Collection("STA Tests")]
public sealed class MermaidBlockRendererTests
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Use(new MermaidExtension()).Build();

    private static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument();
        var renderer = new WpfRenderer(document);
        Pipeline.Setup(renderer);
        var parsed = Markdig.Markdown.Parse(markdown, Pipeline);
        renderer.Render(parsed);
        return document;
    }

    [Fact]
    public void MermaidFence_ValidDiagram_RendersImage()
    {
        STAHelper.RunOnSTA(() =>
        {
            const string md = "```mermaid\nflowchart TD\n  A --> B\n```";

            var document = Render(md);

            var container = Assert.IsType<BlockUIContainer>(document.Blocks.Single());
            Assert.IsType<Image>(container.Child);
        });
    }

    [Fact]
    public void MermaidFence_InvalidSyntax_RendersPlaceholder()
    {
        STAHelper.RunOnSTA(() =>
        {
            // Not a recognized Mermaid diagram type/grammar — Mermaider must throw on this.
            const string md = "```mermaid\nthis is not @@@ a valid --- diagram\n```";

            var document = Render(md);

            var container = Assert.IsType<BlockUIContainer>(document.Blocks.Single());
            var text = Assert.IsType<TextBlock>(container.Child);
            Assert.Equal("⚠ Could not render Mermaid diagram", text.Text);
        });
    }

    [Fact]
    public void NonMermaidFence_RendersAsDefaultCodeBlock_NotADiagram()
    {
        STAHelper.RunOnSTA(() =>
        {
            const string md = "```csharp\nvar x = 1;\n```";

            var document = Render(md);

            Assert.Empty(document.Blocks.OfType<BlockUIContainer>());
            var paragraph = Assert.IsType<Paragraph>(document.Blocks.Single());
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
            Assert.Contains("var x = 1;", text);
        });
    }
}
