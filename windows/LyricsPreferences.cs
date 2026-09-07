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
    private static LyricsPreferences Load()
    {
        try { return JsonSerializer.Deserialize<LyricsPreferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
    public bool IsRejected(string song, string version) =>
        Rejected.TryGetValue(song, out var versions) && versions.Contains(version);
    public ReadingPreference? GetReading(string song, string word) =>
        Readings.TryGetValue(song, out var words) && words.TryGetValue(word, out var value) ? value : null;
}
