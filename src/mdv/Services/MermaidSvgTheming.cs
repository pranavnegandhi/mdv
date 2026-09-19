using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace mdv.Services;

/// <summary>
/// Preprocesses a Mermaider-rendered SVG so SharpVectors (used by <see cref="SvgRendering.Build"/>)
/// can actually parse it. Mermaider's entire theming model is CSS custom properties
/// (<c>--name: value;</c>) plus <c>var()</c> and <c>color-mix()</c> — used both inside its
/// <c>&lt;style&gt;</c> block and directly on presentation attributes (e.g.
/// <c>fill="var(--_node-fill)"</c>). SharpVectors' CSS tokenizer cannot parse the bare
/// <c>--name: value;</c> declaration syntax at all (confirmed against SharpVectors.Wpf 1.8.5: it
/// throws <c>DomException: Style declaration ending bracket missing</c> even on the simplest
/// single custom-property declaration, independent of <c>var()</c>/<c>color-mix()</c>). This
/// class resolves every custom property and <c>color-mix()</c> call down to literal values, then
/// deletes the now-unparseable declaration statements, leaving plain, literal-color SVG.
/// </summary>
internal static class MermaidSvgTheming
{
    private const int MaxPasses = 10;

    // A custom-property declaration: `--name: value;` (value is everything up to the next `;`,
    // but never a quote or angle bracket — those only appear once a declaration is unterminated,
    // e.g. the last property in an element's `style="...` attribute when it has no trailing `;`
    // before the closing quote; without this guard, `[^;]+` would run past the attribute, the
    // quote, and the tag itself looking for the next `;` anywhere later in the document,
    // corroding unrelated markup. Leaving that one declaration unstripped is harmless — it never
    // reaches SharpVectors' failing code path, which is specifically its <style> block parser).
    private static readonly Regex DeclarationPattern = new(
        @"--[\w-]+\s*:\s*[^;""'<>]+;",
        RegexOptions.Compiled);

    private static readonly Regex DeclarationCapturePattern = new(
        @"--([\w-]+)\s*:\s*([^;""'<>]+);",
        RegexOptions.Compiled);

    private static readonly Regex ColorMixPattern = new(
        @"color-mix\(\s*in\s+srgb\s*,\s*(#[0-9A-Fa-f]{6})\s+(\d+(?:\.\d+)?)%\s*,\s*(#[0-9A-Fa-f]{6})\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolves every <c>var()</c> reference and <c>color-mix()</c> call in <paramref name="svg"/>
    /// to a literal value, then strips the (now unparseable, and no longer needed) custom-property
    /// declarations. Never throws: anything that cannot be resolved within <see cref="MaxPasses"/>
    /// passes is left as-is — the caller's own fallback (a placeholder) handles any resulting
    /// SharpVectors parse failure.
    /// </summary>
    internal static string ResolveCssVariables(string svg)
    {
        var text = svg;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var variables = CollectVariables(text);
            var afterVar = SubstituteVarCalls(text, variables);
            var afterColorMix = ColorMixPattern.Replace(afterVar, ResolveColorMixMatch);

            if (afterColorMix == text)
                break;

            text = afterColorMix;
        }

        return DeclarationPattern.Replace(text, string.Empty);
    }

    /// <summary>
    /// Collects every <c>--name: value;</c> declaration in <paramref name="text"/> into a
    /// name→value lookup. If a name is declared more than once, the last one wins.
    /// </summary>
    private static Dictionary<string, string> CollectVariables(string text)
    {
        var variables = new Dictionary<string, string>();
        foreach (Match match in DeclarationCapturePattern.Matches(text))
            variables[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        return variables;
    }

    /// <summary>
    /// Replaces every <c>var(--name)</c> / <c>var(--name, fallback)</c> call anywhere in
    /// <paramref name="text"/>. A small paren-balance scan finds each call's true argument list
    /// (a fallback can itself contain nested parens, e.g. a nested <c>color-mix(...)</c>), which
    /// is then split into name and fallback at the first top-level comma.
    /// </summary>
    private static string SubstituteVarCalls(string text, Dictionary<string, string> variables)
    {
        var builder = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var idx = text.IndexOf("var(", i, StringComparison.Ordinal);
            if (idx < 0)
            {
                builder.Append(text, i, text.Length - i);
                break;
            }

            builder.Append(text, i, idx - i);

            var argsStart = idx + "var(".Length;
            var argsEnd = FindMatchingParen(text, argsStart);
            if (argsEnd < 0)
            {
                // Unbalanced parens — leave the rest verbatim rather than guess.
                builder.Append(text, idx, text.Length - idx);
                i = text.Length;
                break;
            }

            var args = text.Substring(argsStart, argsEnd - argsStart);
            builder.Append(ResolveVarArgs(args, variables));

            i = argsEnd + 1;
        }

        return builder.ToString();
    }

    private static string ResolveVarArgs(string args, Dictionary<string, string> variables)
    {
        var commaIndex = FindTopLevelComma(args);
        var rawName = commaIndex < 0 ? args : args[..commaIndex];
        var fallback = commaIndex < 0 ? null : args[(commaIndex + 1)..].Trim();
        var name = rawName.Trim().TrimStart('-');

        if (variables.TryGetValue(name, out var value))
            return value;

        if (fallback is not null)
            return fallback;

        // Nothing to resolve this pass — leave the call as-is for a later pass (or forever, if
        // it never resolves; the caller degrades to a placeholder rather than crashing).
        return $"var({args})";
    }

    /// <summary>
    /// Finds the index of the closing <c>)</c> matching the <c>(</c> implicitly opened just
    /// before <paramref name="contentStart"/>, accounting for nested parens. Returns -1 if
    /// unbalanced.
    /// </summary>
    private static int FindMatchingParen(string text, int contentStart)
    {
        var depth = 1;
        for (var j = contentStart; j < text.Length; j++)
        {
            switch (text[j])
            {
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth == 0) return j;
                    break;
            }
        }

        return -1;
    }

    private static int FindTopLevelComma(string args)
    {
        var depth = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0: return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Resolves <c>color-mix(in srgb, COLOR1 P%, COLOR2)</c> — both colors already literal
    /// 6-digit hex by the time this runs — to a single interpolated <c>#RRGGBB</c> value.
    /// </summary>
    private static string ResolveColorMixMatch(Match match)
    {
        var color1 = match.Groups[1].Value;
        var percent = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var color2 = match.Groups[3].Value;

        var (r1, g1, b1) = ParseHex(color1);
        var (r2, g2, b2) = ParseHex(color2);

        var r = MixChannel(r1, r2, percent);
        var g = MixChannel(g1, g2, percent);
        var b = MixChannel(b1, b2, percent);

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int MixChannel(int channel1, int channel2, double percent) =>
        (int)Math.Round(channel1 * (percent / 100.0) + channel2 * ((100.0 - percent) / 100.0),
            MidpointRounding.AwayFromZero);

    private static (int R, int G, int B) ParseHex(string hex)
    {
        var value = hex.TrimStart('#');
        var r = int.Parse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (r, g, b);
    }
}
