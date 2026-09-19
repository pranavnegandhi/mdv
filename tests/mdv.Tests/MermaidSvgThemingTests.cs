using Mermaider;
using Xunit;
using mdv.Services;

namespace mdv.Tests;

/// <summary>
/// Pure string/color-math tests for <see cref="MermaidSvgTheming"/> — the preprocessing step
/// that resolves Mermaider's CSS custom properties (<c>var()</c>, <c>color-mix()</c>) into
/// literal values SharpVectors' CSS parser can actually consume (#33).
/// </summary>
public sealed class MermaidSvgThemingTests
{
    [Fact]
    public void ResolveCssVariables_SimpleVarReference_ResolvesToDeclaredValue()
    {
        const string svg = "<svg><style>--fg: #123456;</style><rect fill=\"var(--fg)\" /></svg>";

        var result = MermaidSvgTheming.ResolveCssVariables(svg);

        Assert.Contains("fill=\"#123456\"", result);
    }

    [Fact]
    public void ResolveCssVariables_ColorMixWorkedExample_ResolvesToExpectedHex()
    {
        const string svg = "<rect fill=\"color-mix(in srgb, #27272A 55%, #FFFFFF)\" />";

        var result = MermaidSvgTheming.ResolveCssVariables(svg);

        Assert.Contains("fill=\"#88888A\"", result);
    }

    [Fact]
    public void ResolveCssVariables_Declaration_IsStrippedFromOutput()
    {
        const string svg = "<svg><style>--_text: #000000;</style></svg>";

        var result = MermaidSvgTheming.ResolveCssVariables(svg);

        Assert.DoesNotContain("--_text", result);
    }

    [Fact]
    public void ResolveCssVariables_RemFontSize_ConvertsToPixelNumber()
    {
        const string svg = "<svg><style>--fs-m: 1rem;</style><text font-size=\"var(--fs-m)\">A</text></svg>";

        var result = MermaidSvgTheming.ResolveCssVariables(svg);

        Assert.Contains("font-size=\"16\"", result);
    }

    [Fact]
    public void ResolveCssVariables_AccentWithNoTrailingSemicolon_IsResolved()
    {
        const string svg = "<svg style=\"--bg:#FFFFFF;--fg:#27272A;--accent:#3b82f6\">" +
                            "<path fill=\"var(--accent)\" /></svg>";

        var result = MermaidSvgTheming.ResolveCssVariables(svg);

        Assert.Contains("fill=\"#3b82f6\"", result);
    }

    [Fact]
    public void ResolveCssVariables_RealMermaidOutput_ResolvesFontSizeAndAccentColor()
    {
        var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B\n");

        var resolved = MermaidSvgTheming.ResolveCssVariables(svg);

        Assert.Contains("font-size=\"16\"", resolved);
        Assert.Contains("#3b82f6", resolved);
    }
}
