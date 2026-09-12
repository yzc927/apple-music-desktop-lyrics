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
            _choices = SettingsPersistence.Load(_path, () => new Dictionary<string, string>(StringComparer.Ordinal),
                values => values.Values.All(value => !string.IsNullOrWhiteSpace(value)));
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
            SettingsPersistence.Save(_path, _choices, values => values.Values.All(value => !string.IsNullOrWhiteSpace(value)));
        }
        catch { }
    }
}
