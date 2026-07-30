using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace AppleMusicDesktopLyrics;

internal sealed class LyricsClient
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    static LyricsClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("AppleMusicDesktopLyrics/0.1 (Windows companion app)");
    }

    public async Task<IReadOnlyList<LyricLine>> GetAsync(
        string title, string artist, string album, TimeSpan duration, CancellationToken cancellationToken)
    {
        var cleanTitle = CleanTitle(title);
        var exactQuery = $"api/search?track_name={Uri.EscapeDataString(cleanTitle)}" +
                         $"&artist_name={Uri.EscapeDataString(CleanArtist(artist))}";
        var results = await Http.GetFromJsonAsync<LyricsResult[]>(exactQuery, cancellationToken) ?? [];

        // Apple Music for Windows sometimes exposes "artist — album" as the artist.
        // If that makes the strict lookup fail, search by title and rank candidates by
        // duration, artist and album instead. Duration is especially useful for covers.
        var usedTitleOnlySearch = !results.Any(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics));
        if (usedTitleOnlySearch)
        {
            var titleQuery = $"api/search?track_name={Uri.EscapeDataString(cleanTitle)}";
            results = await Http.GetFromJsonAsync<LyricsResult[]>(titleQuery, cancellationToken) ?? [];
        }

        var best = results
            .Where(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics))
            .OrderBy(x => Score(x, title, artist, album, duration))
            .FirstOrDefault();
        if (best is null) return [];

        // A title-only search can return a cover or an unrelated recording with the
        // same name. Prefer no LRCLIB result (and let Apple UI lyrics take over) over
        // a timeline that visibly stalls or drifts because it belongs to another cut.
        var durationDifference = Math.Abs(best.Duration - duration.TotalSeconds);
        var allowedDurationDifference = Math.Max(12, duration.TotalSeconds * 0.06);
        if (duration.TotalSeconds > 0 && durationDifference > allowedDurationDifference)
            return [];
        if (usedTitleOnlySearch && !ContainsEither(best.ArtistName, CleanArtist(artist)) &&
            durationDifference > 3)
            return [];
        return best.SyncedLyrics is { } lrc ? LrcParser.Parse(lrc) : [];
    }

    private static double Score(LyricsResult item, string title, string artist, string album, TimeSpan duration)
    {
        var score = Math.Abs(item.Duration - duration.TotalSeconds);
        if (!ContainsEither(item.TrackName, CleanTitle(title))) score += 120;
        if (!ContainsEither(item.ArtistName, artist)) score += 80;
        if (!string.IsNullOrWhiteSpace(album) && !ContainsEither(item.AlbumName, album)) score += 10;
        return score;
    }

    private static bool ContainsEither(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanTitle(string title) =>
        System.Text.RegularExpressions.Regex.Replace(title,
            @"\s*[\(\[].*?(remaster(?:ed)?|live|version|edit).*?[\)\]]", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    private static string CleanArtist(string artist)
    {
        var separators = new[] { " — ", " – ", " - " };
        foreach (var separator in separators)
        {
            var index = artist.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0) return artist[..index].Trim();
        }
        return artist.Trim();
    }

    private sealed record LyricsResult(
        [property: JsonPropertyName("trackName")] string TrackName,
        [property: JsonPropertyName("artistName")] string ArtistName,
        [property: JsonPropertyName("albumName")] string AlbumName,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);
}
