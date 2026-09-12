using System.Text.Json;
using System.IO;

namespace AppleMusicDesktopLyrics;

internal sealed record StoredLyrics(string Lrc, string Label, DateTimeOffset UpdatedAt,
    string? CandidateKey = null, LyricsMatchConfidence? Confidence = null, bool UserSelected = false);

internal sealed class LocalLyricsStore
{
    private readonly string _overridePath;
    private readonly string _cachePath;
    private Dictionary<string, StoredLyrics> _overrides;
    private Dictionary<string, StoredLyrics> _cache;

    public LocalLyricsStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppleMusicDesktopLyrics");
        _overridePath = Path.Combine(directory, "local-lyrics.json");
        _cachePath = Path.Combine(directory, "lyrics-cache.json");
        _overrides = Load(_overridePath);
        _cache = Load(_cachePath);
    }

    public StoredLyrics? GetOverride(string songKey) => Get(_overrides, songKey);
    public StoredLyrics? GetCache(string songKey) => Get(_cache, songKey);
    public bool HasOverride(string songKey) => !string.IsNullOrWhiteSpace(songKey) && _overrides.ContainsKey(songKey);
    public bool HasCache(string songKey) => !string.IsNullOrWhiteSpace(songKey) && _cache.ContainsKey(songKey);

    public void SetOverride(string songKey, string lrc, string label = "本地编辑")
    {
        if (string.IsNullOrWhiteSpace(songKey)) return;
        var updated = Load(_overridePath);
        updated[songKey] = new StoredLyrics(lrc, label, DateTimeOffset.UtcNow);
        Save(_overridePath, updated);
        _overrides = updated;
    }

    public void RemoveOverride(string songKey)
    {
        var updated = Load(_overridePath);
        if (updated.Remove(songKey)) Save(_overridePath, updated);
        _overrides = updated;
    }

    public void SetCache(string songKey, string lrc, string label,
        string? candidateKey = null, LyricsMatchConfidence? confidence = null, bool userSelected = false)
    {
        if (string.IsNullOrWhiteSpace(songKey) || string.IsNullOrWhiteSpace(lrc)) return;
        var updated = Load(_cachePath);
        updated[songKey] = new StoredLyrics(lrc, label, DateTimeOffset.UtcNow, candidateKey, confidence, userSelected);
        // Avoid unbounded growth while retaining the most recently used songs.
        if (updated.Count > 500)
            updated = updated.OrderByDescending(item => item.Value.UpdatedAt).Take(450)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        Save(_cachePath, updated);
        _cache = updated;
    }

    public void ClearCache()
    {
        var updated = new Dictionary<string, StoredLyrics>(StringComparer.Ordinal);
        Save(_cachePath, updated);
        _cache = updated;
    }

    public void RemoveCache(string songKey)
    {
        var updated = Load(_cachePath);
        if (updated.Remove(songKey)) Save(_cachePath, updated);
        _cache = updated;
    }

    private static StoredLyrics? Get(Dictionary<string, StoredLyrics> source, string key) =>
        !string.IsNullOrWhiteSpace(key) && source.TryGetValue(key, out var value) ? value : null;

    private static bool IsValid(Dictionary<string, StoredLyrics> values) =>
        values.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null &&
            !string.IsNullOrWhiteSpace(pair.Value.Lrc) && pair.Value.Label is not null &&
            (pair.Value.Confidence is null || Enum.IsDefined(pair.Value.Confidence.Value)));

    private static Dictionary<string, StoredLyrics> Load(string path) =>
        new(SettingsPersistence.Load(path, () => new Dictionary<string, StoredLyrics>(), IsValid), StringComparer.Ordinal);

    private static void Save(string path, Dictionary<string, StoredLyrics> values)
    {
        SettingsPersistence.Save(path, values, IsValid);
    }
}
