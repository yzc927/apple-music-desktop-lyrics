import Foundation

private struct LRCLIBResponse: Decodable {
    let trackName: String
    let artistName: String
    let albumName: String
    let duration: Double
    let syncedLyrics: String?
}

final class LRCLIBClient {
    func lyrics(for track: TrackInfo) async -> [LyricLine] {
        let title = cleanTitle(track.title)
        var results = await search(title: title, artist: cleanArtist(track.artist))
        let usedTitleOnlySearch = !results.contains(where: { !($0.syncedLyrics ?? "").isEmpty })
        if usedTitleOnlySearch {
            results = await search(title: title, artist: nil)
        }
        let best = results.filter { !($0.syncedLyrics ?? "").isEmpty }.min {
            score($0, track: track) < score($1, track: track)
        }
        guard let best else { return [] }
        let durationDifference = abs(best.duration - track.duration)
        let allowedDurationDifference = max(12, track.duration * 0.06)
        guard track.duration <= 0 || durationDifference <= allowedDurationDifference else { return [] }
        if usedTitleOnlySearch && !containsEither(best.artistName, cleanArtist(track.artist)) && durationDifference > 3 {
            return []
        }
        return LRCParser.parse(best.syncedLyrics ?? "")
    }

    private func search(title: String, artist: String?) async -> [LRCLIBResponse] {
        var components = URLComponents(string: "https://lrclib.net/api/search")!
        components.queryItems = [URLQueryItem(name: "track_name", value: title)]
        if let artist, !artist.isEmpty { components.queryItems?.append(URLQueryItem(name: "artist_name", value: artist)) }
        guard let url = components.url else { return [] }
        var request = URLRequest(url: url)
        request.timeoutInterval = 12
        request.setValue("AppleMusicDesktopLyricsMac/0.1", forHTTPHeaderField: "User-Agent")
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { return [] }
            return try JSONDecoder().decode([LRCLIBResponse].self, from: data)
        } catch { return [] }
    }

    private func score(_ item: LRCLIBResponse, track: TrackInfo) -> Double {
        var value = abs(item.duration - track.duration)
        if !containsEither(item.trackName, cleanTitle(track.title)) { value += 120 }
        if !containsEither(item.artistName, track.artist) { value += 80 }
        if !track.album.isEmpty && !containsEither(item.albumName, track.album) { value += 10 }
        return value
    }

    private func containsEither(_ lhs: String, _ rhs: String) -> Bool {
        lhs.localizedCaseInsensitiveContains(rhs) || rhs.localizedCaseInsensitiveContains(lhs)
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
