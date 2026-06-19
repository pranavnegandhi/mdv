namespace mdv.Services;

/// <summary>
/// Collapses the middle of a string with an ellipsis so it fits a target pixel width,
/// e.g. <c>C:\foo\bar\baz\temp\document.md</c> -&gt; <c>C:\foo\b...emp\document.md</c>.
/// The pixel measurement is supplied by the caller, so this stays free of any UI-toolkit
/// dependency and can be unit-tested with a simple fixed-width measurer.
/// </summary>
public static class MiddleEllipsis
{
    /// <summary>The marker inserted where the middle was removed.</summary>
    public const string Ellipsis = "...";

    /// <summary>
    /// Returns <paramref name="text"/> unchanged when it already fits
    /// <paramref name="maxWidth"/>; otherwise removes characters from the middle until
    /// the result fits, keeping as much of the string as possible.
    /// </summary>
    /// <param name="text">The full string (e.g. a file path).</param>
    /// <param name="maxWidth">Available width, in the same units <paramref name="measure"/> returns.</param>
    /// <param name="measure">Measures the rendered width of a candidate string.</param>
    /// <param name="frontFraction">
    /// Fraction of the kept characters taken from the start. Defaults to 0.4 so the tail
    /// — which for a path holds the file name — stays visible.
    /// </param>
    public static string Truncate(
        string text,
        double maxWidth,
        Func<string, double> measure,
        double frontFraction = 0.4)
    {
        if (string.IsNullOrEmpty(text) || measure(text) <= maxWidth)
            return text;

        // Keep k of the original characters split around the ellipsis. The candidate's
        // width grows monotonically with k, so binary-search the largest k that still
        // fits. k == 0 degrades to just the ellipsis, which is the floor result.
        var best = Ellipsis;
        int lo = 0, hi = text.Length - 1;
        while (lo <= hi)
        {
            var k = (lo + hi) / 2;
            var front = (int)(k * frontFraction);
            var back = k - front;

            var candidate = string.Concat(
                text.AsSpan(0, front),
                Ellipsis.AsSpan(),
                text.AsSpan(text.Length - back));

            if (measure(candidate) <= maxWidth)
            {
                best = candidate;
                lo = k + 1;
            }
            else
            {
                hi = k - 1;
            }
        }

        return best;
    }
}
