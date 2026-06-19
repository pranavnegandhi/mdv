using System.Text;
using System.Windows.Documents;

namespace mdv.Services;

/// <summary>
/// Plain-text search over a rendered <see cref="FlowDocument"/>. A FlowDocument has no
/// built-in find, so this builds a flat string of the document's text once, paired with
/// a map back to <see cref="TextPointer"/>s, then locates substring matches and returns
/// each as a <see cref="TextRange"/> the UI can select and scroll to.
/// </summary>
public sealed class DocumentSearch
{
    // One contiguous text run: its offset within the flattened string, its length, and
    // the pointer at its start. A match offset is mapped back to a pointer by finding the
    // run that contains it and stepping that many text symbols from the run's start.
    private readonly record struct Run(int StringStart, int Length, TextPointer Start);

    private readonly List<Run> _runs = new();
    private readonly string _text;

    public DocumentSearch(FlowDocument document)
    {
        var builder = new StringBuilder();

        var pointer = document.ContentStart;
        while (pointer is not null)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = pointer.GetTextInRun(LogicalDirection.Forward);
                if (runText.Length > 0)
                {
                    _runs.Add(new Run(builder.Length, runText.Length, pointer));
                    builder.Append(runText);
                }
            }

            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }

        _text = builder.ToString();
    }

    /// <summary>
    /// Returns every (case-insensitive) occurrence of <paramref name="query"/> as a
    /// <see cref="TextRange"/>, in document order. Empty when the query is blank or absent.
    /// </summary>
    public IReadOnlyList<TextRange> FindAll(string query)
    {
        var results = new List<TextRange>();
        if (string.IsNullOrEmpty(query) || _text.Length == 0)
            return results;

        var from = 0;
        int index;
        while ((index = _text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = MapOffset(index);
            var end = MapOffset(index + query.Length);
            if (start is not null && end is not null)
                results.Add(new TextRange(start, end));

            from = index + query.Length;
        }

        return results;
    }

    /// <summary>Maps a character offset in the flattened text back to a <see cref="TextPointer"/>.</summary>
    private TextPointer? MapOffset(int offset)
    {
        // A match may span runs; the start and end offsets are mapped independently, so a
        // pointer just needs the run whose span contains the offset (inclusive of its end).
        foreach (var run in _runs)
        {
            if (offset >= run.StringStart && offset <= run.StringStart + run.Length)
                return run.Start.GetPositionAtOffset(offset - run.StringStart, LogicalDirection.Forward);
        }

        return null;
    }
}
