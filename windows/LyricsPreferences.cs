using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

internal sealed record ReadingPreference(string Reading, bool Familiar);

internal sealed class LyricsPreferences
{
    public Dictionary<string, HashSet<string>> Rejected { get; set; } = new();
    public Dictionary<string, Dictionary<string, ReadingPreference>> Readings { get; set; } = new();
    public bool OnlyUnfamiliar { get; set; }
    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        ["显示 / 隐藏"] = "Ctrl+Alt+H", ["锁定 / 解锁"] = "Ctrl+Alt+L",
        ["歌词慢 0.5 秒"] = "Ctrl+Alt+Left", ["歌词快 0.5 秒"] = "Ctrl+Alt+Right",
        ["循环当前句"] = "Ctrl+Alt+R"
    };
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "interaction-preferences.json");
    public static LyricsPreferences Current { get; } = Load();
    private string? _committed;
    internal static bool IsValid(LyricsPreferences value) =>
        value.Hotkeys is not null && value.Hotkeys.All(pair => pair.Value is not null) &&
        value.Rejected is not null && value.Rejected.All(pair => pair.Value is not null && pair.Value.All(version => version is not null)) &&
        value.Readings is not null && value.Readings.All(pair => pair.Value is not null &&
            pair.Value.All(word => word.Value is not null && word.Value.Reading is not null));

    private static LyricsPreferences Load()
    {
        var value = SettingsPersistence.Load(FilePath, () => new LyricsPreferences(), IsValid);
        value._committed = JsonSerializer.Serialize(value);
        return value;
    }
    public void Save() => SaveTo(FilePath);
    internal void SaveTo(string path)
    {
        try
        {
            var snapshot = JsonSerializer.Serialize(this);
            SettingsPersistence.Save(path, this, IsValid);
            _committed = snapshot;
        }
        catch
        {
            if (_committed is { } previous)
            {
                var restored = JsonSerializer.Deserialize<LyricsPreferences>(previous)!;
                Hotkeys = restored.Hotkeys; Rejected = restored.Rejected;
                Readings = restored.Readings; OnlyUnfamiliar = restored.OnlyUnfamiliar;
            }
            throw;
        }
    }
    internal void SaveHotkeys(Dictionary<string, string> proposed)
    {
        var previous = Hotkeys;
        Hotkeys = new(proposed);
        try { Save(); }
        catch { Hotkeys = previous; throw; }
    }
    public bool IsRejected(string song, string version) =>
        Rejected.TryGetValue(song, out var versions) && versions.Contains(version);
    public ReadingPreference? GetReading(string song, string word) =>
        Readings.TryGetValue(song, out var words) && words.TryGetValue(word, out var value) ? value : null;
}
