using System.Text.RegularExpressions;

namespace AppleMusicDesktopLyrics;

internal static partial class SongMetadataNormalizer
{
    private static readonly string[] VariantKeywords =
    [
        "live", "remaster", "acoustic", "instrumental", "karaoke", "demo",
        "radio edit", "extended", "sped up", "slowed", "nightcore", "mono",
        "tv size", "anime", "动画", "动漫", "剧场版", "opening", "ending"
    ];

    public static string CleanTitle(string value)
    {
        var cleaned = FeatureSuffixRegex().Replace(value ?? "", "");
        cleaned = DecoratedVersionRegex().Replace(cleaned, "");
        cleaned = DashVersionSuffixRegex().Replace(cleaned, "");
        return WhitespaceRegex().Replace(cleaned, " ").Trim(' ', '-', '–', '—', '·');
    }

    public static string CleanArtist(string value)
    {
        var cleaned = FeatureSuffixRegex().Replace(value ?? "", "");
        foreach (var separator in new[] { " — ", " – ", " - " })
        {
            var index = cleaned.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0) cleaned = cleaned[..index];
        }
        return WhitespaceRegex().Replace(cleaned, " ").Trim();
    }

    // Matching aliases are explicit identities, never inferred from palette colors
    // or loose transliteration (which can accidentally accept another performer).
    internal static string CanonicalArtistIdentity(string value)
    {
        var cleaned = CleanArtist(value);
        var key = string.Concat(cleaned.Where(character => !char.IsWhiteSpace(character)))
            .Normalize().ToUpperInvariant();
        return key switch
        {
            "HOSHIMACHISUISEI" or "SUISEIHOSHIMACHI" or "星街すいせい" => "星街すいせい",
            _ => cleaned
        };
    }

    public static IReadOnlySet<string> VariantTags(string? value)
    {
        var normalized = (value ?? "").Normalize().ToLowerInvariant()
            .Replace('-', ' ').Replace('_', ' ');
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in VariantKeywords)
            if (normalized.Contains(keyword, StringComparison.Ordinal)) tags.Add(keyword);
        if (Regex.IsMatch(normalized, @"(?:^|\W)op(?:\W|$)")) tags.Add("opening");
        if (Regex.IsMatch(normalized, @"(?:^|\W)ed(?:\W|$)")) tags.Add("ending");
        return tags;
    }

    public static double VariantMismatchPenalty(string expected, string candidate)
    {
        var left = VariantTags(expected);
        var right = VariantTags(candidate);
        if (left.Count == right.Count && left.All(right.Contains)) return 0;
        // A wrong live/instrumental/TV-size recording is much more noticeable
        // than a small metadata mismatch, so keep this above the album weight.
        return left.SymmetricExceptCount(right) * 34;
    }

    private static int SymmetricExceptCount(this IReadOnlySet<string> left,
        IReadOnlySet<string> right) =>
        left.Count(item => !right.Contains(item)) + right.Count(item => !left.Contains(item));

    [GeneratedRegex(@"\s*(?:\(|\[|【|（)[^\)\]】）]*(?:live|remaster(?:ed)?|version|edit|acoustic|instrumental|karaoke|demo|mono|stereo|tv\s*size|anime|动画|动漫|剧场版|opening|ending|\bop\b|\bed\b)[^\)\]】）]*(?:\)|\]|】|）)", RegexOptions.IgnoreCase)]
    private static partial Regex DecoratedVersionRegex();

    [GeneratedRegex(@"\s+(?:feat(?:uring)?\.?|ft\.?)\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureSuffixRegex();

    [GeneratedRegex(@"\s*[-–—]\s*(?:live|remaster(?:ed)?|acoustic|instrumental|karaoke|demo|radio\s+edit|extended|sped\s+up|slowed|nightcore|tv\s*size|anime|动画版|动漫版|剧场版).*$", RegexOptions.IgnoreCase)]
    private static partial Regex DashVersionSuffixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
