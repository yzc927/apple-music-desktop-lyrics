import Foundation

enum ArtistColorEngine {
    static let curated: [String: [String]] = [
        "さくらみこ": ["#FFFF7EB6"], "miComet": ["#FFFF7EB6", "#FF55B8FF"],
        "星街すいせい": ["#FF55B8FF"], "YOASOBI": ["#FF4D79FF", "#FFFF5B9D"],
        "Ado": ["#FF5965FF", "#FF9A5CFF"], "EGOIST": ["#FF57C7FF", "#FFA56CFF"],
        "Eve": ["#FF4062BB", "#FF4CC9F0"], "HoneyWorks": ["#FFFFB52E", "#FFFF6FAE"],
        "King Gnu": ["#FFD6A84B", "#FFA84B5B"], "aiko": ["#FFFF5C8A"],
        "Liyuu": ["#FF4D9CFF", "#FF63C7FF"], "初音ミク": ["#FF39C5BB"],
        "Hatsune Miku": ["#FF39C5BB"], "水樹奈々": ["#FF4169E1", "#FFC54BCE"],
        "Jay Chou": ["#FF5B7CFA", "#FFC18B5A"], "周杰伦": ["#FF5B7CFA", "#FFC18B5A"],
        "周杰倫": ["#FF5B7CFA", "#FFC18B5A"], "G.E.M.": ["#FF8B5CF6", "#FFF5B942"],
        "邓紫棋": ["#FF8B5CF6", "#FFF5B942"], "鄧紫棋": ["#FF8B5CF6", "#FFF5B942"],
        "Jolin Tsai": ["#FFFF4FA3", "#FF8B5CF6"], "蔡依林": ["#FFFF4FA3", "#FF8B5CF6"],
        "五月天": ["#FF3C78D8", "#FF35B7A7"], "Mayday": ["#FF3C78D8", "#FF35B7A7"],
        "王心凌": ["#FFFF75B5", "#FF63C7FF"], "Cyndi Wang": ["#FFFF75B5", "#FF63C7FF"],
        "F.I.R.": ["#FF33B68A", "#FF8D6AD8"], "林俊杰": ["#FF6757D9", "#FF4EA8DE"],
        "林俊傑": ["#FF6757D9", "#FF4EA8DE"], "JJ LIN": ["#FF6757D9", "#FF4EA8DE"],
        "Official髭男dism": ["#FFFFB84D", "#FFFF7A45"], "藤井風": ["#FF61C995"],
        "藤井 風": ["#FF61C995"], "米津玄師": ["#FF6477B9"],
        "fripSide": ["#FFFF9D3D", "#FFFFD166"], "Da-iCE": ["#FFFF6B5E"],
        "大原櫻子": ["#FFFF7096"], "Kanaria": ["#FFE84A5F"],
        "かいりきベア": ["#FFB26CFF"], "しぐれうい": ["#FF6ED6C1"],
        "シユイ": ["#FF9B7BFF"], "Buzy": ["#FFFF8F70"], "スピッツ": ["#FF78C8FF"],
        "DEEN": ["#FF4D9CFF"], "Akie": ["#FFFFB85C"], "DECO*27": ["#FFFF4F9A"],
        "宝鐘マリン": ["#FFFF4B5E"], "椎名林檎": ["#FFD94A64"], "れるりり": ["#FF50C9C3"],
        "L'Arc-en-Ciel": ["#FF55B8FF", "#FF9B72F2", "#FFFF6FAE"],
        "浜崎あゆみ": ["#FFE3B35B"], "Mitchie M": ["#FF35D0BA"], "堀江由衣": ["#FFD78BD8"],
        "伊東歌詞太郎": ["#FFE35D5B"], "榊原ゆい": ["#FFF06292"],
        "おねがいシラサギ(CV.種﨑敦美)": ["#FF8BC6EC"],
        "春奈るな": ["#FF7A8CFF", "#FFAA72E8"], "沢井 美空": ["#FF6CCDEB"],
        "沢井美空": ["#FF6CCDEB"], "Sound Horizon": ["#FF5163A8", "#FFD9A441"],
        "サカナクション": ["#FF32B8B0"], "中島みゆき": ["#FFB54A63"],
        "中島 美嘉": ["#FF7B6ED6"], "中島美嘉": ["#FF7B6ED6"],
        "許嵩": ["#FF4FB3A5"], "许嵩": ["#FF4FB3A5"], "孙燕姿": ["#FF57B8FF"],
        "孫燕姿": ["#FF57B8FF"], "范玮琪": ["#FFB58BD8"], "范瑋琪": ["#FFB58BD8"],
        "JUVENILE": ["#FF45CFF1"], "王力宏": ["#FF3C78D8", "#FFD4A64A"],
        "Wang Leehom": ["#FF3C78D8", "#FFD4A64A"], "ワン・リーホン": ["#FF3C78D8", "#FFD4A64A"],
        "By2": ["#FFFF7B8B", "#FF5CC8E8"], "刘若英": ["#FFC98276"],
        "劉若英": ["#FFC98276"], "Rene Liu": ["#FFC98276"],
        "doriko": ["#FFD966A6"], "Omoinotake": ["#FFE45756", "#FFF2A65A"],
        "竹達彩奈": ["#FFFF8FA3"], "今井麻美": ["#FF5967C9"],
        "松本梨香": ["#FFE6503C"], "山崎まさよし": ["#FFC68A4A"]
    ]

    private static let fallback = [
        "#FFFF6B6B", "#FFFF9F43", "#FFF6C445", "#FF55C98B", "#FF46C8C8",
        "#FF55B8FF", "#FF5F7CFF", "#FF9B72F2", "#FFC267E8", "#FFFF6FAE"
    ]

    static func colors(for rawArtist: String) -> [String] {
        let identity = rawArtist.components(separatedBy: " — ").first?.trimmingCharacters(in: .whitespaces) ?? rawArtist
        if let exact = curated[identity] { return exact }
        let separators = try? NSRegularExpression(pattern: #"\s+(?:&|＆|×|feat\.?|featuring|with|x)\s+|\s*、\s*|\s*,\s*"#, options: .caseInsensitive)
        let range = NSRange(identity.startIndex..., in: identity)
        let artists = separators?.stringByReplacingMatches(in: identity, range: range, withTemplate: "\u{1F}")
            .split(separator: "\u{1F}").map { String($0).trimmingCharacters(in: .whitespaces) } ?? [identity]
        if artists.count > 1 { return artists.prefix(3).map(singleColor) }
        return [singleColor(identity)]
    }

    private static func singleColor(_ artist: String) -> String {
        if let color = curated[artist]?.first { return color }
        var hash: UInt32 = 2_166_136_261
        for scalar in artist.uppercased().unicodeScalars { hash = (hash ^ scalar.value) &* 16_777_619 }
        return fallback[Int(hash % UInt32(fallback.count))]
    }
}
