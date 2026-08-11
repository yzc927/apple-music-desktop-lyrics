using System.Globalization;
using System.Text.RegularExpressions;

namespace AppleMusicDesktopLyrics;

internal sealed record TimedLyricSegment(TimeSpan Time, string Text);

internal sealed record LyricLine(
    TimeSpan Time,
    string Text,
    IReadOnlyList<TimedLyricSegment>? Segments = null,
    TimeSpan? ExplicitEndTime = null)
{
    public bool HasWordTiming => Segments is { Count: > 0 };
}

internal static partial class LrcParser
{
    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>\d{2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"<(?<m>\d{1,3}):(?<s>\d{2}(?:\.\d{1,3})?)>")]
    private static partial Regex WordTimestampRegex();

    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        var parsed = new List<LyricLine>();
        foreach (var rawLine in lrc.Replace("\r", "").Split('\n'))
        {
            var lineMatches = TimestampRegex().Matches(rawLine);
            if (lineMatches.Count == 0) continue;
            var content = TimestampRegex().Replace(rawLine, "").TrimStart();

            foreach (Match lineMatch in lineMatches)
            {
                if (!TryParseTimestamp(lineMatch, out var lineTime)) continue;
                var (text, segments, explicitEnd) = ParseContent(content, lineTime);
                if (string.IsNullOrWhiteSpace(text)) text = "♪";
                parsed.Add(new LyricLine(lineTime, text, segments, explicitEnd));
            }
        }

        return parsed
            .OrderBy(line => line.Time)
            .GroupBy(line => line.Time)
            .Select(MergeSameTime)
            .ToArray();
    }

    public static string Serialize(IReadOnlyList<LyricLine> lines, bool includeWordTiming = true) =>
        string.Join(Environment.NewLine, lines.Select(line => SerializeLine(line, includeWordTiming)));

    private static string SerializeLine(LyricLine line, bool includeWordTiming)
    {
        var prefix = $"[{FormatTimestamp(line.Time)}]";
        if (!includeWordTiming || !line.HasWordTiming)
            return prefix + " " + (LyricTiming.IsInstrumental(line.Text) ? "" : line.Text);

        var timedSegments = line.Segments!;
        var segments = string.Concat(timedSegments.Select(segment =>
            $"<{FormatTimestamp(segment.Time)}>{segment.Text}"));
        if (line.ExplicitEndTime is { } end && end >= timedSegments[^1].Time)
            segments += $"<{FormatTimestamp(end)}>";
        return prefix + segments;
    }

    private static (string Text, IReadOnlyList<TimedLyricSegment>? Segments, TimeSpan? ExplicitEnd)
        ParseContent(string content, TimeSpan lineTime)
    {
        var matches = WordTimestampRegex().Matches(content);
        if (matches.Count == 0) return (content.TrimEnd(), null, null);

        var segments = new List<TimedLyricSegment>();
        TimeSpan? explicitEnd = null;
        var valid = true;
        var previous = lineTime;
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            if (!TryParseTimestamp(match, out var time) || time < previous)
            {
                valid = false;
                break;
            }
            previous = time;
            var textStart = match.Index + match.Length;
            var textEnd = index + 1 < matches.Count ? matches[index + 1].Index : content.Length;
            var segmentText = content[textStart..textEnd];
            if (index == matches.Count - 1) segmentText = segmentText.TrimEnd();
            if (segmentText.Length == 0)
            {
                if (index == matches.Count - 1) explicitEnd = time;
                continue;
            }
            segments.Add(new TimedLyricSegment(time, segmentText));
        }

        var plainText = WordTimestampRegex().Replace(content, "").Trim();
        if (!valid || segments.Count == 0) return (plainText, null, null);
        if (segments[0].Time < lineTime) return (plainText, null, null);
        return (string.Concat(segments.Select(segment => segment.Text)).TrimEnd(), segments, explicitEnd);
    }

    private static LyricLine MergeSameTime(IGrouping<TimeSpan, LyricLine> group)
    {
        var lines = group.ToArray();
        if (lines.Length == 1) return lines[0];
        return new LyricLine(group.Key,
            string.Join(" / ", lines.Select(line => line.Text).Distinct(StringComparer.Ordinal)));
    }

    private static bool TryParseTimestamp(Match match, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        if (!int.TryParse(match.Groups["m"].Value, out var minutes) ||
            !double.TryParse(match.Groups["s"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var seconds)) return false;
        timestamp = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string FormatTimestamp(TimeSpan value) =>
        $"{Math.Max(0, (int)value.TotalMinutes):00}:{Math.Max(0, value.Seconds):00}.{Math.Max(0, value.Milliseconds / 10):00}";
}
