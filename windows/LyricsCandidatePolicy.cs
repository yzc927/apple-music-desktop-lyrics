namespace AppleMusicDesktopLyrics;

/// <summary>Shared online, preload and offline selection rules.</summary>
internal static class LyricsCandidatePolicy
{
    internal static IReadOnlyList<LyricsCandidate> Visible(IEnumerable<LyricsCandidate> candidates,
        string song, LyricsPreferences preferences) => candidates
        .Where(item => !preferences.IsRejected(song, item.Key)).Take(8).ToArray();

    internal static LyricsCandidate? Automatic(IEnumerable<LyricsCandidate> candidates,
        string song, LyricsPreferences preferences, string? remembered)
    {
        var allowed = candidates.Where(item => !preferences.IsRejected(song, item.Key)).ToArray();
        return allowed.FirstOrDefault(item => item.Key == remembered) ??
            allowed.FirstOrDefault(item => item.Match.Confidence != LyricsMatchConfidence.Low);
    }

    internal static bool CanUseCache(StoredLyrics stored, string song, LyricsPreferences preferences)
    {
        if (string.IsNullOrWhiteSpace(stored.CandidateKey))
        {
            // Keep safe legacy offline caches, but never resurrect unknown rejected
            // versions or old unchecked preloads whose confidence is unavailable.
            return !(preferences.Rejected.TryGetValue(song, out var rejected) && rejected.Count > 0)
                && !stored.Label.StartsWith("预加载", StringComparison.Ordinal);
        }
        return !preferences.IsRejected(song, stored.CandidateKey) &&
            (stored.Confidence != LyricsMatchConfidence.Low || stored.UserSelected);
    }
}
