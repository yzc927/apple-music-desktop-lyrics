using System.IO;
using System.Text.Json;

namespace AppleMusicDesktopLyrics;

/// <summary>Keeps lyric timing corrections local and separate for each recording.</summary>
internal sealed class SongOffsetStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "song-offsets.json");
    private Dictionary<string, double> _offsets = new(StringComparer.Ordinal);

    public SongOffsetStore()
    {
        try
        {
            _offsets = SettingsPersistence.Load(_path, () => new Dictionary<string, double>(StringComparer.Ordinal),
                values => values.Values.All(double.IsFinite));
        }
        catch { }
    }

    public TimeSpan Get(string songKey)
    {
        return _offsets.TryGetValue(songKey, out var seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, -30, 30))
            : TimeSpan.Zero;
    }

    public void Set(string songKey, TimeSpan offset)
    {
        if (string.IsNullOrWhiteSpace(songKey)) return;
        if (Math.Abs(offset.TotalMilliseconds) < 10)
            _offsets.Remove(songKey);
        else
            _offsets[songKey] = Math.Clamp(offset.TotalSeconds, -30, 30);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            SettingsPersistence.Save(_path, _offsets, values => values.Values.All(double.IsFinite));
        }
        catch { }
    }
}
