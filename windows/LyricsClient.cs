using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AppleMusicDesktopLyrics;

internal sealed record LyricsCandidate(
    string Key, string Label, IReadOnlyList<LyricLine> Lines, double Score);

internal sealed record LyricsSearchResult(IReadOnlyList<LyricsCandidate> Candidates)
{
    public static readonly LyricsSearchResult Empty = new([]);
}

internal sealed partial class LyricsClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    static LyricsClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("AppleMusicDesktopLyrics/0.2 (Windows companion app)");
    }

    public async Task<IReadOnlyList<LyricLine>> GetAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken cancellationToken) =>
        (await SearchAsync(title, artist, album, duration, cancellationToken))
        .Candidates.FirstOrDefault()?.Lines ?? [];

    public async Task<LyricsSearchResult> SearchAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken cancellationToken)
    {
        var cleanTitle = CleanTitle(title);
        var cleanArtist = CleanArtist(artist);
        var exactQuery = $"api/search?track_name={Uri.EscapeDataString(cleanTitle)}" +
                         $"&artist_name={Uri.EscapeDataString(cleanArtist)}";
        // Always collect title-only alternatives. They are useful when Apple Music
        // exposes a collaboration or album suffix differently, and they allow the
        // user to switch recordings without weakening the automatic first choice.
        var titleQuery = $"api/search?track_name={Uri.EscapeDataString(cleanTitle)}";
        var exactTask = SearchEndpointAsync(exactQuery, cancellationToken);
        var broadTask = SearchEndpointAsync(titleQuery, cancellationToken);
        await Task.WhenAll(exactTask, broadTask);
        var exact = await exactTask;
        var broad = await broadTask;
        var exactKeys = exact.Select(CandidateKey).ToHashSet(StringComparer.Ordinal);
        var combined = exact.Concat(broad.Where(item => !exactKeys.Contains(CandidateKey(item))));

        var candidates = combined
            .Where(item => !string.IsNullOrWhiteSpace(item.SyncedLyrics))
            .Select(item => CreateCandidate(item, title, artist, album, duration,
                exactKeys.Contains(CandidateKey(item))))
            .Where(item => item is not null)
            .Cast<LyricsCandidate>()
            .GroupBy(item => TimelineFingerprint(item.Lines), StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.Score).First())
            .OrderBy(item => item.Score)
            .Take(8)
            .ToArray();
        return candidates.Length == 0 ? LyricsSearchResult.Empty : new(candidates);
    }

    private static async Task<LyricsResult[]> SearchEndpointAsync(
        string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            return await Http.GetFromJsonAsync<LyricsResult[]>(endpoint, cancellationToken) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return []; }
        catch (System.Text.Json.JsonException) { return []; }
        catch (TaskCanceledException) { return []; }
    }

    private static LyricsCandidate? CreateCandidate(
        LyricsResult item, string title, string artist, string album, TimeSpan duration, bool exactArtistQuery)
    {
        var lines = LrcParser.Parse(item.SyncedLyrics ?? "");
        if (lines.Count == 0) return null;
        var durationDifference = DurationDifference(item.Duration, duration.TotalSeconds);
        var allowedDurationDifference = Math.Max(10, duration.TotalSeconds * 0.045);
        if (duration.TotalSeconds > 0 && durationDifference > allowedDurationDifference) return null;

        var titleMatch = TextMatch(item.TrackName, CleanTitle(title));
        var artistMatch = ArtistMatch(item.ArtistName, CleanArtist(artist));
        if (titleMatch < 0.72) return null;
        if (!exactArtistQuery && artistMatch < 0.45 && durationDifference > 2.5) return null;

        var score = Score(item, title, artist, album, duration, titleMatch, artistMatch);
        var durationLabel = item.Duration is { } seconds
            ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss")
            : "时长未知";
        var albumLabel = string.IsNullOrWhiteSpace(item.AlbumName) ? "" : $" · {item.AlbumName}";
        return new LyricsCandidate(
            CandidateKey(item), $"{item.ArtistName}{albumLabel} · {durationLabel}", lines, score);
    }

    private static double Score(
        LyricsResult item, string title, string artist, string album, TimeSpan duration,
        double? knownTitleMatch = null, double? knownArtistMatch = null)
    {
        var durationDifference = DurationDifference(item.Duration, duration.TotalSeconds);
        var score = durationDifference * 3;
        score += (1 - (knownTitleMatch ?? TextMatch(item.TrackName, CleanTitle(title)))) * 180;
        score += (1 - (knownArtistMatch ?? ArtistMatch(item.ArtistName, CleanArtist(artist)))) * 110;
        if (!string.IsNullOrWhiteSpace(album))
            score += (1 - TextMatch(item.AlbumName, album)) * 16;
        if (item.Duration is null) score += 80;
        return score;
    }

    private static string CandidateKey(LyricsResult item) => item.Id is { } id
        ? id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{Normalize(item.TrackName)}|{Normalize(item.ArtistName)}|{Normalize(item.AlbumName)}|{item.Duration:0.0}";

    private static string TimelineFingerprint(IReadOnlyList<LyricLine> lines) =>
        string.Join('|', lines.Take(24).Select(line => $"{line.Time.TotalMilliseconds:0}:{Normalize(line.Text)}"));

    private static double DurationDifference(double? candidateDuration, double expectedDuration) =>
        candidateDuration is { } value && expectedDuration > 0 ? Math.Abs(value - expectedDuration) : 300;

    private static double TextMatch(string? left, string? right)
    {
        var first = Normalize(left);
        var second = Normalize(right);
        if (first.Length == 0 || second.Length == 0) return 0;
        if (first == second) return 1;
        if (first.Contains(second, StringComparison.Ordinal) || second.Contains(first, StringComparison.Ordinal))
            return Math.Min(first.Length, second.Length) / (double)Math.Max(first.Length, second.Length);
        return TokenOverlap(first, second);
    }

    private static double ArtistMatch(string? left, string? right)
    {
        var first = ArtistTokens(left);
        var second = ArtistTokens(right);
        if (first.Count == 0 || second.Count == 0) return 0;
        var overlap = first.Intersect(second, StringComparer.Ordinal).Count();
        if (overlap > 0) return overlap / (double)Math.Min(first.Count, second.Count);
        return TextMatch(left, right);
    }

    private static HashSet<string> ArtistTokens(string? value) =>
        Regex.Split(value ?? "", @"\s*(?:&|＆|×|、|,|，|/| feat\.? | featuring | with | x )\s*",
                RegexOptions.IgnoreCase)
            .Select(Normalize)
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static double TokenOverlap(string first, string second)
    {
        var left = Bigrams(first);
        var right = Bigrams(second);
        if (left.Count == 0 || right.Count == 0) return 0;
        return 2d * left.Intersect(right, StringComparer.Ordinal).Count() / (left.Count + right.Count);
    }

    private static HashSet<string> Bigrams(string value)
    {
        var runes = value.EnumerateRunes().Select(item => item.ToString()).ToArray();
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (runes.Length == 1) result.Add(runes[0]);
        for (var index = 0; index + 1 < runes.Length; index++) result.Add(runes[index] + runes[index + 1]);
        return result;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new StringBuilder();
        foreach (var rune in value.Normalize(NormalizationForm.FormKC).EnumerateRunes())
            if (Rune.IsLetterOrDigit(rune)) builder.Append(rune.ToString().ToLowerInvariant());
        return builder.ToString();
    }

    private static string CleanTitle(string title) =>
        TitleDecorationRegex().Replace(title, "").Trim();

    private static string CleanArtist(string artist)
    {
        foreach (var separator in new[] { " — ", " – ", " - " })
        {
            var index = artist.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0) return artist[..index].Trim();
        }
        return artist.Trim();
    }

    [GeneratedRegex(@"\s*[\(\[].*?(remaster(?:ed)?|live|version|edit).*?[\)\]]",
        RegexOptions.IgnoreCase)]
    private static partial Regex TitleDecorationRegex();

    private sealed record LyricsResult(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("trackName")] string TrackName,
        [property: JsonPropertyName("artistName")] string ArtistName,
        [property: JsonPropertyName("albumName")] string AlbumName,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);
}
