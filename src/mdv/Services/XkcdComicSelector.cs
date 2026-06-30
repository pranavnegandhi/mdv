namespace mdv.Services;

/// <summary>
/// Pure, stateless algorithm that maps a date and an archive size to a deterministic comic id.
/// A C# port of the <c>xkcd-prng.py</c> reference script, seeded from Randall Munroe's birthday.
/// </summary>
public static class XkcdComicSelector
{
    /// <summary>
    /// Returns a deterministic comic id in the range <c>[1, latestComicNumber)</c> for the given
    /// <paramref name="date"/>.
    /// </summary>
    /// <param name="date">The target date (typically today).</param>
    /// <param name="latestComicNumber">
    /// The current highest comic number from the live feed. Must be &gt; 0; the caller should
    /// treat a zero-or-negative value as a fetch failure and fall back to the embedded comic.
    /// </param>
    /// <returns>A comic id in the range 1 .. latestComicNumber - 1 (never 0).</returns>
    public static int Select(DateOnly date, int latestComicNumber)
    {
        var birth = new DateOnly(1984, 10, 17);
        int deltaDays = date.DayNumber - birth.DayNumber;
        int approx = (int)(deltaDays * (3.0 / 7.0));          // truncates toward zero, matches Python int()

        // Must use long: 17101984 * approx overflows int for present-day dates (≈ 7.7×10¹⁰).
        long scrambled = (17101984L * approx + 1017L) % latestComicNumber;
        return (int)Math.Max(1L, scrambled);
    }
}
