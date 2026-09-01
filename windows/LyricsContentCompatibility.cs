namespace AppleMusicDesktopLyrics;

internal static class LyricsContentCompatibility
{
    public static bool IsClearlyIncompatible(
        IReadOnlyList<LyricLine> lines, string current, string next)
    {
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(current) ||
            string.IsNullOrWhiteSpace(next)) return false;

        var currentBest = lines.Max(line => LyricTiming.Similarity(line.Text, current));
        var nextBest = lines.Max(line => LyricTiming.Similarity(line.Text, next));
        return currentBest < 0.32 && nextBest < 0.32;
    }
}
