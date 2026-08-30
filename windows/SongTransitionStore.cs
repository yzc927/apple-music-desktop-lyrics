using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

internal sealed record NextTrackMetadata(
    string Key, string Title, string Artist, string Album, double DurationSeconds);

internal sealed class SongTransitionStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "song-transitions.json");
    private Dictionary<string, NextTrackMetadata> _transitions = new(StringComparer.Ordinal);

    public SongTransitionStore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _transitions = JsonSerializer.Deserialize<Dictionary<string, NextTrackMetadata>>(
                File.ReadAllText(_path)) ?? new(StringComparer.Ordinal);
        }
        catch { }
    }

    public NextTrackMetadata? GetNext(string songKey) =>
        _transitions.TryGetValue(songKey, out var value) ? value : null;

    public void Learn(string songKey, NextTrackMetadata next)
    {
        if (string.IsNullOrWhiteSpace(songKey) || string.IsNullOrWhiteSpace(next.Key) ||
            string.Equals(songKey, next.Key, StringComparison.Ordinal)) return;
        _transitions[songKey] = next;
        if (_transitions.Count > 1000)
            _transitions = _transitions.TakeLast(900)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_transitions));
            File.Move(temporary, _path, true);
        }
        catch { }
    }
}
