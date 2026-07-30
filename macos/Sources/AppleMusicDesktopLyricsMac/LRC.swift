import Foundation

enum LRCParser {
    private static let timestamp = try! NSRegularExpression(pattern: #"\[(\d{1,3}):(\d{2})(?:[\.:](\d{1,3}))?\]"#)

    static func parse(_ text: String) -> [LyricLine] {
        var result: [LyricLine] = []
        for rawLine in text.components(separatedBy: .newlines) {
            let range = NSRange(rawLine.startIndex..., in: rawLine)
            let matches = timestamp.matches(in: rawLine, range: range)
            guard !matches.isEmpty else { continue }
            let rawLyric = timestamp.stringByReplacingMatches(in: rawLine, range: range, withTemplate: "")
                .trimmingCharacters(in: .whitespacesAndNewlines)
            let lyric = rawLyric.isEmpty ? "♪" : rawLyric
            for match in matches {
                func number(_ index: Int) -> Double {
                    guard match.range(at: index).location != NSNotFound,
                          let swiftRange = Range(match.range(at: index), in: rawLine) else { return 0 }
                    return Double(rawLine[swiftRange]) ?? 0
                }
                let fractionText = match.range(at: 3).location == NSNotFound ? "" : String(rawLine[Range(match.range(at: 3), in: rawLine)!])
                let fraction = fractionText.isEmpty ? 0 : (Double(fractionText) ?? 0) / pow(10, Double(fractionText.count))
                result.append(LyricLine(time: number(1) * 60 + number(2) + fraction, text: lyric))
            }
        }
        let grouped = Dictionary(grouping: result, by: \.time)
        return grouped.map { time, values in
            LyricLine(time: time, text: Array(Set(values.map(\.text))).sorted().joined(separator: " / "))
        }.sorted { $0.time < $1.time }
    }
}
