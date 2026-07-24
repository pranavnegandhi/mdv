using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace mdv.Services;

/// <summary>
/// Recognizes an inline <c>&lt;svg&gt;…&lt;/svg&gt;</c> at the parse layer and captures it as a single
/// <see cref="SvgBlock"/>, from the opening <c>&lt;svg</c> through the matching <c>&lt;/svg&gt;</c>.
/// </summary>
/// <remarks>
/// This exists because CommonMark's raw-HTML block rules are the source of issue #26: a
/// single-line <c>&lt;svg …&gt;…&lt;/svg&gt;</c> never becomes an HTML block (it lands in a paragraph),
/// and a blank line inside an SVG truncates a type-7 HTML block. By owning recognition here,
/// both problems disappear: this parser ignores line layout and does not stop at blank lines.
/// It is inserted ahead of <see cref="HtmlBlockParser"/>, and any non-SVG <c>&lt;</c> line is
/// declined so normal HTML handling is unaffected.
/// </remarks>
internal sealed class SvgBlockParser : BlockParser
{
    public SvgBlockParser()
    {
        OpeningCharacters = new[] { '<' };
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        // An indented (code) line is never an SVG block start.
        if (processor.IsCodeIndent)
            return BlockState.None;

        // StringSlice is a struct, so this copy is a non-consuming scratch cursor over the line.
        var line = processor.Line;
        if (!StartsWithSvgTag(line))
            return BlockState.None;

        var block = new SvgBlock(this)
        {
            Column = processor.ColumnBeforeIndent,
            Line = processor.LineIndex,
            Span = new SourceSpan(processor.Start, processor.Line.End),
        };
        processor.NewBlocks.Push(block);

        // A whole <svg …>…</svg> on one physical line (what PlantUML emits) closes immediately.
        return ContainsClosingTag(line) ? BlockState.Break : BlockState.Continue;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        // Deliberately does NOT end on a blank line: an SVG may contain blank lines (pretty-printed
        // markup, or PlantUML embedded source). Keep consuming until the matching </svg>.
        block.Span.End = processor.Line.End;
        return ContainsClosingTag(processor.Line) ? BlockState.Break : BlockState.Continue;
    }

    /// <summary>
    /// True when the line begins with an <c>&lt;svg</c> tag — i.e. <c>&lt;svg</c> followed by a
    /// delimiter (whitespace, <c>&gt;</c>, <c>/</c>, or end of line), so <c>&lt;svgother&gt;</c> is rejected.
    /// </summary>
    private static bool StartsWithSvgTag(StringSlice line)
    {
        var text = line.ToString().AsSpan().TrimStart();
        if (text.Length < 4 || text[0] != '<'
            || (text[1] is not ('s' or 'S'))
            || (text[2] is not ('v' or 'V'))
            || (text[3] is not ('g' or 'G')))
        {
            return false;
        }

        if (text.Length == 4)
            return true;

        var next = text[4];
        return char.IsWhiteSpace(next) || next == '>' || next == '/';
    }

    private static bool ContainsClosingTag(StringSlice line) =>
        line.ToString().Contains("</svg", StringComparison.OrdinalIgnoreCase);
}
