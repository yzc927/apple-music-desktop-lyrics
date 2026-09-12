using System.IO;
using System.Text.RegularExpressions;
namespace AppleMusicDesktopLyrics;

internal sealed record ConfirmedArtistAlias(string First, string Second);
internal sealed class ConfirmedArtistAliases
{
    public static ConfirmedArtistAliases Current { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "confirmed-artist-aliases.json"));
    private readonly string _path;
    private List<ConfirmedArtistAlias> _items;
    internal ConfirmedArtistAliases(string path)
    {
        _path = path;
        _items = SettingsPersistence.Load(path, () => new List<ConfirmedArtistAlias>(),
            values => values.All(item => item is not null && CanLearn(item.First, item.Second)));
    }
    internal IReadOnlyList<ConfirmedArtistAlias> Items => _items.ToArray();
    private static string Key(string value) => string.Concat(
        SongMetadataNormalizer.CanonicalArtistIdentity(value).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    internal static bool CanLearn(string first, string second) =>
        new[] { first, second }.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
            !Regex.IsMatch(value, @"[&＆×、,，/()（）]|\b(?:feat|featuring|with|CV)\b", RegexOptions.IgnoreCase)) &&
        Key(first) != Key(second);
    internal bool Matches(string first, string second) => _items.Any(item =>
        (Key(item.First) == Key(first) && Key(item.Second) == Key(second)) ||
        (Key(item.First) == Key(second) && Key(item.Second) == Key(first)));
    internal void Confirm(string first, string second)
    {
        if (!CanLearn(first, second)) throw new InvalidOperationException("这里只能确认两个单独歌手名称的别名关系。");
        if (Matches(first, second)) return;
        Commit(_items.Append(new ConfirmedArtistAlias(first.Trim(), second.Trim())).ToList());
    }
    internal void Remove(ConfirmedArtistAlias alias) => Commit(_items.Where(item => item != alias).ToList());
    private void Commit(List<ConfirmedArtistAlias> proposed)
    {
        SettingsPersistence.Save(_path, proposed);
        _items = proposed;
    }
}
