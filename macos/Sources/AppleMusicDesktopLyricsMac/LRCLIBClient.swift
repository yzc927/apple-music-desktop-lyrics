import Foundation

struct LyricCandidate: Equatable {
    let key: String
    let label: String
    let lines: [LyricLine]
    let score: Double
}

private struct LRCLIBResponse: Decodable {
    let id: Int?
    let trackName: String
    let artistName: String
    let albumName: String
    let duration: Double?
    let syncedLyrics: String?
}

final class LRCLIBClient {
    func lyrics(for track: TrackInfo) async -> [LyricLine] {
        (await candidates(for: track)).first?.lines ?? []
    }

    func candidates(for track: TrackInfo) async -> [LyricCandidate] {
        let title = cleanTitle(track.title), artist = cleanArtist(track.artist)
        async let exactRequest = search(title: title, artist: artist)
        async let broadRequest = search(title: title, artist: nil)
        let (exact, broad) = await (exactRequest, broadRequest)
        let exactKeys = Set(exact.map(candidateKey))
        let combined = exact + broad.filter { !exactKeys.contains(candidateKey($0)) }
        var timelines = Set<String>()
        return combined.compactMap { item -> LyricCandidate? in
            guard let lyrics = item.syncedLyrics, !lyrics.isEmpty else { return nil }
            let lines = LRCParser.parse(lyrics)
            guard !lines.isEmpty else { return nil }
            let durationDifference = item.duration.map { abs($0 - track.duration) } ?? 300
            let allowedDurationDifference = max(10, track.duration * 0.045)
            guard track.duration <= 0 || durationDifference <= allowedDurationDifference else { return nil }
            let titleMatch = textMatch(item.trackName, cleanTitle(track.title))
            let artistMatch = artistMatch(item.artistName, cleanArtist(track.artist))
            guard titleMatch >= 0.72 else { return nil }
            guard exactKeys.contains(candidateKey(item)) || artistMatch >= 0.45 || durationDifference <= 2.5 else { return nil }
            let fingerprint = timelineFingerprint(lines)
            guard timelines.insert(fingerprint).inserted else { return nil }
            let score = durationDifference * 3 + (1 - titleMatch) * 180 + (1 - artistMatch) * 110 +
                (track.album.isEmpty ? 0 : (1 - textMatch(item.albumName, track.album)) * 16) +
                (item.duration == nil ? 80 : 0)
            let durationLabel = item.duration.map { String(format: "%d:%02d", Int($0) / 60, Int($0) % 60) } ?? "时长未知"
            let albumLabel = item.albumName.isEmpty ? "" : " · \(item.albumName)"
            return LyricCandidate(
                key: candidateKey(item), label: "\(item.artistName)\(albumLabel) · \(durationLabel)",
                lines: lines, score: score
            )
        }.sorted { $0.score < $1.score }.prefix(8).map { $0 }
    }

    private func search(title: String, artist: String?) async -> [LRCLIBResponse] {
        var components = URLComponents(string: "https://lrclib.net/api/search")!
        components.queryItems = [URLQueryItem(name: "track_name", value: title)]
        if let artist, !artist.isEmpty { components.queryItems?.append(URLQueryItem(name: "artist_name", value: artist)) }
        guard let url = components.url else { return [] }
        var request = URLRequest(url: url)
        request.timeoutInterval = 12
        request.setValue("AppleMusicDesktopLyricsMac/0.2", forHTTPHeaderField: "User-Agent")
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { return [] }
            return try JSONDecoder().decode([LRCLIBResponse].self, from: data)
        } catch { return [] }
    }

    private func candidateKey(_ item: LRCLIBResponse) -> String {
        if let id = item.id { return String(id) }
        return "\(normalized(item.trackName))|\(normalized(item.artistName))|\(normalized(item.albumName))|\(item.duration ?? -1)"
    }

    private func timelineFingerprint(_ lines: [LyricLine]) -> String {
        lines.prefix(24).map { "\(Int(($0.time * 1000).rounded())):\(normalized($0.text))" }.joined(separator: "|")
    }

    private func textMatch(_ lhs: String, _ rhs: String) -> Double {
        let first = normalized(lhs), second = normalized(rhs)
        guard !first.isEmpty, !second.isEmpty else { return 0 }
        if first == second { return 1 }
        if first.contains(second) || second.contains(first) {
            return Double(min(first.count, second.count)) / Double(max(first.count, second.count))
        }
        let left = bigrams(first), right = bigrams(second)
        guard !left.isEmpty, !right.isEmpty else { return 0 }
        return 2 * Double(left.intersection(right).count) / Double(left.count + right.count)
    }

    private func artistMatch(_ lhs: String, _ rhs: String) -> Double {
        let separators = CharacterSet(charactersIn: "&＆×、,，/")
        let first = Set(lhs.components(separatedBy: separators).map(normalized).filter { !$0.isEmpty })
        let second = Set(rhs.components(separatedBy: separators).map(normalized).filter { !$0.isEmpty })
        let overlap = first.intersection(second).count
        return overlap > 0 ? Double(overlap) / Double(min(first.count, second.count)) : textMatch(lhs, rhs)
    }

    private func normalized(_ value: String) -> String {
        value.folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) }.map { String($0) }.joined()
    }

    private func bigrams(_ value: String) -> Set<String> {
        let characters = Array(value)
        if characters.count == 1 { return [String(characters[0])] }
        guard characters.count > 1 else { return [] }
        return Set((0..<(characters.count - 1)).map { String(characters[$0...($0 + 1)]) })
    }

    private func cleanTitle(_ title: String) -> String {
        title.replacingOccurrences(
            of: #"\s*[\(\[].*?(remaster(?:ed)?|live|version|edit).*?[\)\]]"#,
            with: "", options: [.regularExpression, .caseInsensitive]
        ).trimmingCharacters(in: .whitespaces)
    }

    private func cleanArtist(_ artist: String) -> String {
        for separator in [" — ", " – ", " - "] {
            if let range = artist.range(of: separator), range.lowerBound > artist.startIndex {
                return String(artist[..<range.lowerBound]).trimmingCharacters(in: .whitespaces)
            }
        }
        return artist.trimmingCharacters(in: .whitespaces)
    }
}
