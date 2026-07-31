using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace AppleMusicDesktopLyrics;

internal sealed record CustomArtistPalette(string Identity, string[] Colors);

internal sealed partial class CustomArtistPaletteStore
{
    public static CustomArtistPaletteStore Current { get; } = new();

    private readonly string _path;
    private Dictionary<string, CustomArtistPalette> _items = new(StringComparer.Ordinal);

    private CustomArtistPaletteStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppleMusicDesktopLyrics");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "custom-artist-colors.json");
        Load();
    }

    public IReadOnlyList<CustomArtistPalette> GetAll() => _items.Values
        .OrderBy(item => item.Identity, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public bool TryGet(string artist, out string[] colors)
    {
        if (_items.TryGetValue(ArtistColorEngine.NormalizeArtistKey(artist), out var item))
        {
            colors = item.Colors;
            return true;
        }
        colors = [];
        return false;
    }

    public bool IsCustom(string artist) => _items.ContainsKey(ArtistColorEngine.NormalizeArtistKey(artist));

    public void Set(string identity, IEnumerable<string> colors)
    {
        identity = identity.Trim();
        var validated = colors.Select(NormalizeColor).Where(value => value is not null)
            .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
        if (identity.Length == 0 || validated.Length == 0)
            throw new ArgumentException("歌手名称和至少一个有效颜色不能为空");
        _items[ArtistColorEngine.NormalizeArtistKey(identity)] = new CustomArtistPalette(identity, validated);
        Save();
    }

    public bool Remove(string identity)
    {
        var removed = _items.Remove(ArtistColorEngine.NormalizeArtistKey(identity));
        if (removed) Save();
        return removed;
    }

    public void Export(string path) => File.WriteAllText(path,
        JsonSerializer.Serialize(GetAll(), new JsonSerializerOptions { WriteIndented = true }));

    public int Import(string path)
    {
        var values = JsonSerializer.Deserialize<CustomArtistPalette[]>(File.ReadAllText(path)) ?? [];
        var count = 0;
        foreach (var item in values)
        {
            try { Set(item.Identity, item.Colors); count++; }
            catch (ArgumentException) { }
        }
        Save();
        return count;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var values = JsonSerializer.Deserialize<CustomArtistPalette[]>(File.ReadAllText(_path)) ?? [];
            _items = values.Where(item => !string.IsNullOrWhiteSpace(item.Identity) && item.Colors.Length > 0)
                .ToDictionary(item => ArtistColorEngine.NormalizeArtistKey(item.Identity),
                    item => item, StringComparer.Ordinal);
        }
        catch { _items = new(StringComparer.Ordinal); }
    }

    private void Save()
    {
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary,
            JsonSerializer.Serialize(GetAll(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }

    private static string? NormalizeColor(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        if (Hex6().IsMatch(text)) return "#FF" + text[1..];
        return Hex8().IsMatch(text) ? text : null;
    }

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex Hex6();
    [GeneratedRegex("^#[0-9A-F]{8}$")]
    private static partial Regex Hex8();
}
