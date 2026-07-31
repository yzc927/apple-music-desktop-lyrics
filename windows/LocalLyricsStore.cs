using System.Text.Json;
using System.IO;

namespace AppleMusicDesktopLyrics;

internal sealed record StoredLyrics(string Lrc, string Label, DateTimeOffset UpdatedAt);

internal sealed class LocalLyricsStore
{
    private readonly string _overridePath;
    private readonly string _cachePath;
    private Dictionary<string, StoredLyrics> _overrides;
    private Dictionary<string, StoredLyrics> _cache;

    public LocalLyricsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppleMusicDesktopLyrics");
        Directory.CreateDirectory(directory);
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
        _overrides[songKey] = new StoredLyrics(lrc, label, DateTimeOffset.UtcNow);
        Save(_overridePath, _overrides);
    }

    public void RemoveOverride(string songKey)
    {
        if (_overrides.Remove(songKey)) Save(_overridePath, _overrides);
    }

    public void SetCache(string songKey, string lrc, string label)
    {
        if (string.IsNullOrWhiteSpace(songKey) || string.IsNullOrWhiteSpace(lrc)) return;
        _cache[songKey] = new StoredLyrics(lrc, label, DateTimeOffset.UtcNow);
        // Avoid unbounded growth while retaining the most recently used songs.
        if (_cache.Count > 500)
            _cache = _cache.OrderByDescending(item => item.Value.UpdatedAt).Take(450)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        Save(_cachePath, _cache);
    }

    public void ClearCache()
    {
        _cache.Clear();
        Save(_cachePath, _cache);
    }

    private static StoredLyrics? Get(Dictionary<string, StoredLyrics> source, string key) =>
        !string.IsNullOrWhiteSpace(key) && source.TryGetValue(key, out var value) ? value : null;

    private static Dictionary<string, StoredLyrics> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, StoredLyrics>>(File.ReadAllText(path))
                   ?? new(StringComparer.Ordinal);
        }
        catch { return new(StringComparer.Ordinal); }
    }

    private static void Save(string path, Dictionary<string, StoredLyrics> values)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(values));
        File.Move(temporary, path, true);
    }
}
