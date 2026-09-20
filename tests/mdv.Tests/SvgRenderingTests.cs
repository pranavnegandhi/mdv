using System.Windows.Controls;
using Xunit;
using mdv.Services;

namespace mdv.Tests;

/// <summary>
/// Unit tests for the SharpVectors-based SVG build/placeholder path shared by
/// <see cref="SvgBlockRenderer"/> (#26) and <see cref="MermaidBlockRenderer"/> (#33).
/// </summary>
[Collection("STA Tests")]
public sealed class SvgRenderingTests
{
    private const string ValidSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\">" +
        "<rect width=\"10\" height=\"10\"/></svg>";

    [Fact]
    public void Build_ValidSvgMarkup_ReturnsBlockUIContainerWithImage()
    {
        STAHelper.RunOnSTA(() =>
        {
            var block = SvgRendering.Build(ValidSvg);
            Assert.IsType<Image>(block.Child);
        });
    }

    [Fact]
    public void Build_MalformedMarkup_Throws()
    {
        // Mismatched tags are not well-formed XML, so SharpVectors' reader must fail on this.
        STAHelper.RunOnSTA(() =>
        {
            Assert.ThrowsAny<Exception>(() => SvgRendering.Build("<svg><rect></svg>"));
        });
    }

    [Fact]
    public void BuildPlaceholder_ReturnsBlockUIContainerWithGivenLabel()
    {
        STAHelper.RunOnSTA(() =>
        {
            var block = SvgRendering.BuildPlaceholder("⚠ Could not render SVG diagram");
            var text = Assert.IsType<TextBlock>(block.Child);
            Assert.Equal("⚠ Could not render SVG diagram", text.Text);
        });
    }
}
