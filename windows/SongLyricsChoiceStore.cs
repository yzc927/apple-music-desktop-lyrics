using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

internal sealed class SongLyricsChoiceStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "lyrics-choices.json");
    private Dictionary<string, string> _choices = new(StringComparer.Ordinal);

    public SongLyricsChoiceStore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _choices = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(_path)) ?? new(StringComparer.Ordinal);
        }
        catch { }
    }

    public string? Get(string songKey) =>
        _choices.TryGetValue(songKey, out var value) ? value : null;

    public void Set(string songKey, string candidateKey)
    {
        if (string.IsNullOrWhiteSpace(songKey) || string.IsNullOrWhiteSpace(candidateKey)) return;
        _choices[songKey] = candidateKey;
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_choices));
            File.Move(temporary, _path, true);
        }
        catch { }
    }
}
