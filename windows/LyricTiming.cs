using System.Globalization;
using System.Text;

namespace AppleMusicDesktopLyrics;

internal static class LyricTiming
{
    public static double EstimateSecondsPerUnit(IReadOnlyList<LyricLine> lines)
    {
        var samples = new List<double>();
        for (var index = 0; index + 1 < lines.Count; index++)
        {
            if (IsInstrumental(lines[index].Text)) continue;
            var seconds = (lines[index + 1].Time - lines[index].Time).TotalSeconds;
            var units = VocalUnits(lines[index].Text);
            if (seconds is < 0.55 or > 8 || units < 1) continue;
            samples.Add(Math.Clamp(seconds / units, 0.07, 0.75));
        }

        if (samples.Count == 0) return 0.28;
        samples.Sort();
        var middle = samples.Count / 2;
        return samples.Count % 2 == 0
            ? (samples[middle - 1] + samples[middle]) / 2
            : samples[middle];
    }

    public static double Progress(
        IReadOnlyList<LyricLine> lines, int index, TimeSpan position, double secondsPerUnit)
    {
        if (index < 0 || index >= lines.Count || IsInstrumental(lines[index].Text)) return 0;
        if (index + 1 >= lines.Count) return 1;

        var natural = (lines[index + 1].Time - lines[index].Time).TotalSeconds;
        if (natural <= 0) return 0;
        var elapsed = (position - lines[index].Time).TotalSeconds;
        // Line-synchronised LRC does not tell us when individual words are sung.
        // The only deterministic interval is this row's timestamp to the next one;
        // never guess a shorter duration from character count.
        return Math.Clamp(elapsed / Math.Max(0.1, natural), 0, 1);
    }

    public static bool IsInstrumental(string? text) =>
        string.IsNullOrWhiteSpace(text) || text is "♪" or "•••" ||
        text.All(character => character is '•' or '.' or '·' || char.IsWhiteSpace(character));

    public static string DisplayText(string? text) => IsInstrumental(text) ? "•••" : text?.Trim() ?? "";

    public static double Similarity(string? left, string? right)
    {
        var first = NormalizeText(left);
        var second = NormalizeText(right);
        if (first.Length == 0 || second.Length == 0) return 0;
        if (first == second) return 1;
        if ((first.Contains(second, StringComparison.Ordinal) ||
             second.Contains(first, StringComparison.Ordinal)) &&
            Math.Min(first.Length, second.Length) >= Math.Max(first.Length, second.Length) * 0.7)
            return 0.9;

        var firstPairs = Bigrams(first);
        var secondPairs = Bigrams(second);
        if (firstPairs.Count == 0 || secondPairs.Count == 0) return 0;
        var overlap = firstPairs.Intersect(secondPairs, StringComparer.Ordinal).Count();
        return 2d * overlap / (firstPairs.Count + secondPairs.Count);
    }

    public static int FindCalibrationLine(
        IReadOnlyList<LyricLine> lines, string current, string next, TimeSpan expectedPosition)
    {
        var bestIndex = -1;
        var bestScore = double.NegativeInfinity;
        for (var index = 0; index < lines.Count; index++)
        {
            if (IsInstrumental(lines[index].Text)) continue;
            var currentSimilarity = Similarity(lines[index].Text, current);
            if (currentSimilarity < 0.72) continue;
            var nextSimilarity = index + 1 < lines.Count && !IsInstrumental(next)
                ? Similarity(lines[index + 1].Text, next)
                : 0;
            var distance = Math.Abs((lines[index].Time - expectedPosition).TotalSeconds);
            if (distance > 15) continue;
            var score = currentSimilarity * 6 + nextSimilarity * 3 - distance * 0.12;
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = index;
        }
        return bestIndex;
    }

    private static double VocalUnits(string text)
    {
        var units = 0d;
        var inLatinWord = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or
                UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or
                UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or
                UnicodeCategory.OtherSymbol)
            {
                inLatinWord = false;
                continue;
            }

            if (rune.Value <= 0x024f && Rune.IsLetterOrDigit(rune))
            {
                if (!inLatinWord) units += 1.6;
                inLatinWord = true;
            }
            else
            {
                units += 1;
                inLatinWord = false;
            }
        }
        return units;
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder();
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune))
                builder.Append(rune.ToString().ToLowerInvariant());
            else if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                builder.Append(rune);
        }
        return builder.ToString();
    }

    private static HashSet<string> Bigrams(string value)
    {
        var runes = value.EnumerateRunes().Select(item => item.ToString()).ToArray();
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (runes.Length == 1) result.Add(runes[0]);
        for (var index = 0; index + 1 < runes.Length; index++)
            result.Add(runes[index] + runes[index + 1]);
        return result;
    }
}
