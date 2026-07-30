using System.Globalization;
using System.Text.RegularExpressions;

namespace AppleMusicDesktopLyrics;

internal sealed record LyricLine(TimeSpan Time, string Text);

internal static partial class LrcParser
{
    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>\d{2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimestampRegex();

    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        var parsed = new List<LyricLine>();
        foreach (var rawLine in lrc.Replace("\r", "").Split('\n'))
        {
            var matches = TimestampRegex().Matches(rawLine);
            if (matches.Count == 0) continue;
            var text = TimestampRegex().Replace(rawLine, "").Trim();
            if (string.IsNullOrWhiteSpace(text)) text = "♪";

            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups["m"].Value, out var minutes) ||
                    !double.TryParse(match.Groups["s"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var seconds)) continue;
                parsed.Add(new LyricLine(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), text));
            }
        }

        return parsed
            .OrderBy(line => line.Time)
            .GroupBy(line => line.Time)
            .Select(group => new LyricLine(group.Key,
                string.Join(" / ", group.Select(x => x.Text).Distinct())))
            .ToArray();
    }
}
