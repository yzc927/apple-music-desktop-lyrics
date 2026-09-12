using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Reflection;

namespace AppleMusicDesktopLyrics;

internal sealed record LyricsCandidate(
    string Key, string Label, IReadOnlyList<LyricLine> Lines, double Score,
    LyricsMatchAssessment Match, double? DurationDifferenceSeconds = null, string? RecordingIdentity = null)
{
    public string Preview => $"{Label}\n时长差：{(DurationDifferenceSeconds is { } difference ? $"{difference:+0.0;-0.0;0.0} 秒" : "未知")} · " +
        (Lines.Any(line => line.HasWordTiming) ? "含逐词时间轴" : "整句时间轴") + "\n" +
        string.Join("\n", Lines.Where(line => !string.IsNullOrWhiteSpace(line.Text)).Take(2).Select(line => line.Text));
}

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
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"AppleMusicDesktopLyrics/{version} (Windows companion app)");
    }

    public async Task<IReadOnlyList<LyricLine>> GetAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken cancellationToken) =>
        (await SearchAsync(title, artist, album, duration, cancellationToken))
        .Candidates.FirstOrDefault()?.Lines ?? [];

    public async Task<LyricsSearchResult> SearchAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken cancellationToken)
    {
        var cleanTitle = SongMetadataNormalizer.CleanTitle(title);
        var cleanArtist = SongMetadataNormalizer.CleanArtist(artist);
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

        var candidates = RankCandidates(combined
            .Where(item => !string.IsNullOrWhiteSpace(item.SyncedLyrics))
            .Select(item => CreateCandidate(item, title, artist, album, duration,
                exactKeys.Contains(CandidateKey(item))))
            .Where(item => item is not null)
            .Cast<LyricsCandidate>());
        return candidates.Count == 0 ? LyricsSearchResult.Empty : new(candidates);
    }

    internal static IReadOnlyList<LyricsCandidate> RankCandidates(IEnumerable<LyricsCandidate> candidates) =>
        candidates
            .GroupBy(item => (item.RecordingIdentity ?? item.Label, item.DurationDifferenceSeconds, TimelineFingerprint(item.Lines)))
            .Select(group => group.OrderBy(item => item.Score).First())
            .OrderBy(item => item.Score)
            .ToArray();

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

        var titleMatch = TextMatch(item.TrackName, SongMetadataNormalizer.CleanTitle(title));
        var artistMatch = ArtistMatch(item.ArtistName, SongMetadataNormalizer.CleanArtist(artist));
        if (titleMatch < 0.72) return null;
        if (!exactArtistQuery && artistMatch < 0.45 && durationDifference > 2.5) return null;

        var variantPenalty = SongMetadataNormalizer.VariantMismatchPenalty(title, item.TrackName);
        var score = Score(item, title, artist, album, duration, titleMatch, artistMatch,
            variantPenalty);
        var assessment = LyricsMatchConfidenceEvaluator.Evaluate(titleMatch, artistMatch,
            durationDifference, variantPenalty, item.Duration is not null && duration.TotalSeconds > 0);
        var durationLabel = item.Duration is { } seconds
            ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss")
            : "时长未知";
        var albumLabel = string.IsNullOrWhiteSpace(item.AlbumName) ? "" : $" · {item.AlbumName}";
        return new LyricsCandidate(
            CandidateKey(item), $"{item.ArtistName}{albumLabel} · {durationLabel}", lines, score,
            assessment, item.Duration is { } candidateSeconds && duration.TotalSeconds > 0 ? candidateSeconds - duration.TotalSeconds : null,
            System.Text.Json.JsonSerializer.Serialize(new { item.TrackName, item.ArtistName, item.AlbumName, item.Duration }));
    }

    private static double Score(
        LyricsResult item, string title, string artist, string album, TimeSpan duration,
        double? knownTitleMatch = null, double? knownArtistMatch = null,
        double? knownVariantPenalty = null)
    {
        var durationDifference = DurationDifference(item.Duration, duration.TotalSeconds);
        var score = durationDifference * 3;
        score += (1 - (knownTitleMatch ?? TextMatch(item.TrackName,
            SongMetadataNormalizer.CleanTitle(title)))) * 180;
        score += (1 - (knownArtistMatch ?? ArtistMatch(item.ArtistName,
            SongMetadataNormalizer.CleanArtist(artist)))) * 110;
        if (!string.IsNullOrWhiteSpace(album))
            score += (1 - TextMatch(item.AlbumName, album)) * 16;
        score += knownVariantPenalty ??
            SongMetadataNormalizer.VariantMismatchPenalty(title, item.TrackName);
        if (item.Duration is null) score += 80;
        return score;
    }

    private static string CandidateKey(LyricsResult item) => item.Id is { } id
        ? id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{Normalize(item.TrackName)}|{Normalize(item.ArtistName)}|{Normalize(item.AlbumName)}|{item.Duration:0.0}|{TimelineFingerprint(LrcParser.Parse(item.SyncedLyrics ?? ""))}";

    internal static string TimelineFingerprint(IReadOnlyList<LyricLine> lines) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(lines.Select(line => new
            {
                Time = line.Time.Ticks, line.Text, End = line.ExplicitEndTime?.Ticks,
                Segments = line.Segments?.Select(segment => new { Time = segment.Time.Ticks, segment.Text }).ToArray()
            }))));

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

    private sealed record LyricsResult(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("trackName")] string TrackName,
        [property: JsonPropertyName("artistName")] string ArtistName,
        [property: JsonPropertyName("albumName")] string AlbumName,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);
}
