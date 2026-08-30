using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

internal sealed record PracticeMarker(double TimeSeconds, string Text, DateTimeOffset UpdatedAt);

internal sealed class PracticeMarkerStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "practice-markers.json");
    private Dictionary<string, List<PracticeMarker>> _markers = new(StringComparer.Ordinal);

    public PracticeMarkerStore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            _markers = JsonSerializer.Deserialize<Dictionary<string, List<PracticeMarker>>>(
                File.ReadAllText(_path)) ?? new(StringComparer.Ordinal);
        }
        catch { }
    }

    public int Count(string songKey) =>
        _markers.TryGetValue(songKey, out var values) ? values.Count : 0;

    public bool IsMarked(string songKey, TimeSpan time) =>
        _markers.TryGetValue(songKey, out var values) &&
        values.Any(item => Math.Abs(item.TimeSeconds - time.TotalSeconds) < 0.25);

    public bool Toggle(string songKey, TimeSpan time, string text)
    {
        if (string.IsNullOrWhiteSpace(songKey) || string.IsNullOrWhiteSpace(text)) return false;
        if (!_markers.TryGetValue(songKey, out var values))
            _markers[songKey] = values = [];
        var existing = values.FindIndex(item =>
            Math.Abs(item.TimeSeconds - time.TotalSeconds) < 0.25);
        var marked = existing < 0;
        if (marked) values.Add(new(time.TotalSeconds, text.Trim(), DateTimeOffset.UtcNow));
        else values.RemoveAt(existing);
        if (values.Count == 0) _markers.Remove(songKey);
        Save();
        return marked;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_markers));
            File.Move(temporary, _path, true);
        }
        catch { }
    }
}
