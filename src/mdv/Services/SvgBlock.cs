using System.Text;
using Markdig.Parsers;
using Markdig.Syntax;

namespace mdv.Services;

/// <summary>
/// A Markdown block holding a complete inline <c>&lt;svg&gt;…&lt;/svg&gt;</c>. Produced by
/// <see cref="SvgBlockParser"/> so that SVG recognition happens at parse time and does not
/// depend on CommonMark's raw-HTML line-shape rules (which drop single-line SVGs and truncate
/// SVGs containing blank lines — see issue #26). Rendered by <see cref="SvgBlockRenderer"/>.
/// </summary>
internal sealed class SvgBlock : LeafBlock
{
    public SvgBlock(BlockParser parser) : base(parser)
    {
    }

    /// <summary>
    /// Reassembles the block's verbatim source markup from the captured lines. The parser keeps
    /// every line — blank lines included — so the returned markup is the whole SVG untouched.
    /// </summary>
    public string GetMarkup()
    {
        var lines = Lines.Lines;
        if (lines is null)
            return string.Empty;

        var builder = new StringBuilder();
        // StringLineGroup over-allocates its backing array; Count is the live line count.
        for (var i = 0; i < Lines.Count; i++)
            builder.AppendLine(lines[i].Slice.ToString());

        return builder.ToString();
    }
}
