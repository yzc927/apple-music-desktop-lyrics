using System.Text.RegularExpressions;

namespace AppleMusicDesktopLyrics;

internal sealed record ArtistPalette(string Identity, IReadOnlyList<string> Colors);

/// <summary>
/// Platform-neutral artist identity palette. UI layers turn the hex colors into
/// native brushes (WPF today, AppKit/SwiftUI in a future macOS port).
/// </summary>
internal static partial class ArtistColorEngine
{
    private static readonly Dictionary<string, string[]> Curated = new(StringComparer.OrdinalIgnoreCase)
    {
        ["さくらみこ"] = ["#FFFF7EB6"],
        ["miComet"] = ["#FFFF7EB6", "#FF55B8FF"],
        ["Da-iCE"] = ["#FFFF6B5E"],
        ["大原櫻子"] = ["#FFFF7096"],
        ["星街すいせい"] = ["#FF55B8FF"],
        ["Kanaria"] = ["#FFE84A5F"],
        ["かいりきベア"] = ["#FFB26CFF"],
        ["しぐれうい"] = ["#FF6ED6C1"],
        ["シユイ"] = ["#FF9B7BFF"],
        ["Buzy"] = ["#FFFF8F70"],
        ["aiko"] = ["#FFFF5C8A"],
        ["EGOIST"] = ["#FF57C7FF", "#FFA56CFF"],
        ["スピッツ"] = ["#FF78C8FF"],
        ["DEEN"] = ["#FF4D9CFF"],
        ["Akie"] = ["#FFFFB85C"],
        ["DECO*27"] = ["#FFFF4F9A"],
        ["Official髭男dism"] = ["#FFFFB84D", "#FFFF7A45"],
        ["藤井 風"] = ["#FF61C995"],
        ["藤井風"] = ["#FF61C995"],
        ["Ado"] = ["#FF5965FF", "#FF9A5CFF"],
        ["宝鐘マリン"] = ["#FFFF4B5E"],
        ["椎名林檎"] = ["#FFD94A64"],
        ["れるりり"] = ["#FF50C9C3"],
        ["YOASOBI"] = ["#FF4D79FF", "#FFFF5B9D"],
        ["King Gnu"] = ["#FFD6A84B", "#FFA84B5B"],
        ["上坂すみれ"] = ["#FFA45CC7"],
        ["Eve"] = ["#FF4062BB", "#FF4CC9F0"],
        ["HoneyWorks"] = ["#FFFFB52E", "#FFFF6FAE"],
        ["L'Arc-en-Ciel"] = ["#FF55B8FF", "#FF9B72F2", "#FFFF6FAE"],
        ["浜崎あゆみ"] = ["#FFE3B35B"],
        ["Mitchie M"] = ["#FF35D0BA"],
        ["堀江由衣"] = ["#FFD78BD8"],
        ["伊東歌詞太郎"] = ["#FFE35D5B"],
        ["榊原ゆい"] = ["#FFF06292"],
        ["おねがいシラサギ(CV.種﨑敦美)"] = ["#FF8BC6EC"],
        ["春奈るな"] = ["#FF7A8CFF", "#FFAA72E8"],
        ["沢井 美空"] = ["#FF6CCDEB"],
        ["沢井美空"] = ["#FF6CCDEB"],
        ["fripSide"] = ["#FFFF9D3D", "#FFFFD166"],
        ["Sound Horizon"] = ["#FF5163A8", "#FFD9A441"],
        ["サカナクション"] = ["#FF32B8B0"],
        ["中島みゆき"] = ["#FFB54A63"],
        ["中島 美嘉"] = ["#FF7B6ED6"],
        ["中島美嘉"] = ["#FF7B6ED6"],
        ["Jay Chou"] = ["#FF5B7CFA", "#FFC18B5A"],
        ["周杰伦"] = ["#FF5B7CFA", "#FFC18B5A"],
        ["周杰倫"] = ["#FF5B7CFA", "#FFC18B5A"],
        ["G.E.M."] = ["#FF8B5CF6", "#FFF5B942"],
        ["邓紫棋"] = ["#FF8B5CF6", "#FFF5B942"],
        ["鄧紫棋"] = ["#FF8B5CF6", "#FFF5B942"],
        ["Jolin Tsai"] = ["#FFFF4FA3", "#FF8B5CF6"],
        ["蔡依林"] = ["#FFFF4FA3", "#FF8B5CF6"],
        ["许嵩"] = ["#FF4FB3A5"],
        ["許嵩"] = ["#FF4FB3A5"],
        ["孙燕姿"] = ["#FF57B8FF"],
        ["孫燕姿"] = ["#FF57B8FF"],
        ["范玮琪"] = ["#FFB58BD8"],
        ["范瑋琪"] = ["#FFB58BD8"],
        ["JUVENILE"] = ["#FF45CFF1"],
        ["Liyuu"] = ["#FFFF7FBF"],
        ["王力宏"] = ["#FF3C78D8", "#FFD4A64A"],
        ["Wang Leehom"] = ["#FF3C78D8", "#FFD4A64A"],
        ["ワン・リーホン"] = ["#FF3C78D8", "#FFD4A64A"],
        ["王心凌"] = ["#FFFF75B5", "#FF63C7FF"],
        ["Cyndi Wang"] = ["#FFFF75B5", "#FF63C7FF"],
        ["五月天"] = ["#FF3C78D8", "#FF35B7A7"],
        ["Mayday"] = ["#FF3C78D8", "#FF35B7A7"],
        ["F.I.R."] = ["#FF33B68A", "#FF8D6AD8"],
        ["F.I.R"] = ["#FF33B68A", "#FF8D6AD8"],
        ["By2"] = ["#FFFF7B8B", "#FF5CC8E8"],
        ["刘若英"] = ["#FFC98276"],
        ["劉若英"] = ["#FFC98276"],
        ["Rene Liu"] = ["#FFC98276"],
        ["林俊杰"] = ["#FF6757D9", "#FF4EA8DE"],
        ["林俊傑"] = ["#FF6757D9", "#FF4EA8DE"],
        ["JJ LIN"] = ["#FF6757D9", "#FF4EA8DE"]
    };

    private static readonly string[] Fallback =
    [
        "#FFFF6B6B", "#FFFF9F43", "#FFF6C445", "#FF55C98B",
        "#FF46C8C8", "#FF55B8FF", "#FF5F7CFF", "#FF9B72F2",
        "#FFC267E8", "#FFFF6FAE", "#FFDB6574", "#FF70B77E"
    ];

    public static ArtistPalette Resolve(string rawArtist)
    {
        var identity = StripAlbum(rawArtist).Trim();
        if (string.IsNullOrWhiteSpace(identity))
            return new ArtistPalette("Unknown", [Fallback[0]]);

        // Known groups are resolved before splitting. This lets a group own a
        // deliberate signature gradient without pretending its name lists members.
        if (Curated.TryGetValue(identity, out var exact))
            return new ArtistPalette(identity, exact);

        var artists = CollaborationSeparator().Split(identity)
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3).ToArray();
        if (artists.Length > 1)
        {
            var colors = artists.Select(ResolveSingle).ToArray();
            return new ArtistPalette(string.Join(" × ", artists), colors);
        }

        return new ArtistPalette(identity, [ResolveSingle(identity)]);
    }

    public static IReadOnlyList<ArtistPalette> GetCuratedPalettes() => Curated
        .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
        .Select(item => new ArtistPalette(item.Key, item.Value))
        .ToArray();

    private static string ResolveSingle(string artist)
    {
        if (Curated.TryGetValue(artist, out var curated)) return curated[0];
        uint hash = 2166136261;
        foreach (var character in artist.Trim().ToUpperInvariant())
            hash = (hash ^ character) * 16777619;
        return Fallback[hash % (uint)Fallback.Length];
    }

    private static string StripAlbum(string value)
    {
        foreach (var separator in new[] { " — ", " – " })
        {
            var index = value.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0) return value[..index];
        }
        return value;
    }

    [GeneratedRegex(@"\s+(?:&|＆|×|feat\.?|featuring|with|x)\s+|\s*、\s*|\s*,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CollaborationSeparator();
}
