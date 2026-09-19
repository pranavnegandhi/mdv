using Mermaider;
using Xunit;
using mdv.Services;

namespace mdv.Tests;

/// <summary>
/// End-to-end test for <see cref="MermaidSvgTheming"/> that also exercises
/// <see cref="SvgRendering.Build"/>, which requires WPF objects to be created on an STA thread
/// (#33). Kept separate from <see cref="MermaidSvgThemingTests"/> so the pure string/color-math
/// tests there don't need STA scheduling.
/// </summary>
[Collection("STA Tests")]
public sealed class MermaidSvgThemingIntegrationTests
{
    [Fact]
    public void ResolveCssVariables_RealMermaidOutput_ThenSvgRenderingBuild_DoesNotThrow()
    {
        STAHelper.RunOnSTA(() =>
        {
            var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B\n");

            var resolved = MermaidSvgTheming.ResolveCssVariables(svg);

            var block = SvgRendering.Build(resolved);

            Assert.NotNull(block);
        });
    }
}
