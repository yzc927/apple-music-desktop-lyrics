import Foundation

struct StoredLyrics: Codable {
    let lrc: String
    let label: String
    let updatedAt: Date
}

final class LocalLyricsStore {
    static let shared = LocalLyricsStore()
    private let defaults = UserDefaults.standard
    private let overridesKey = "AppleMusicDesktopLyricsMac.localLyrics.v1"
    private let cacheKey = "AppleMusicDesktopLyricsMac.lyricsCache.v1"
    private var overrides: [String: StoredLyrics] = [:]
    private var cache: [String: StoredLyrics] = [:]

    private init() {
        overrides = decode(overridesKey)
        cache = decode(cacheKey)
    }

    func override(for key: String) -> StoredLyrics? { overrides[key] }
    func cached(for key: String) -> StoredLyrics? { cache[key] }
    func hasOverride(_ key: String) -> Bool { overrides[key] != nil }
    func hasCache(_ key: String) -> Bool { cache[key] != nil }

    func setOverride(_ lrc: String, label: String, for key: String) {
        overrides[key] = StoredLyrics(lrc: lrc, label: label, updatedAt: Date())
        encode(overrides, key: overridesKey)
    }

    func removeOverride(for key: String) {
        overrides.removeValue(forKey: key)
        encode(overrides, key: overridesKey)
    }

    func setCache(_ lrc: String, label: String, for key: String) {
        cache[key] = StoredLyrics(lrc: lrc, label: label, updatedAt: Date())
        if cache.count > 500 {
            cache = Dictionary(uniqueKeysWithValues: cache.sorted { $0.value.updatedAt > $1.value.updatedAt }
                .prefix(450).map { ($0.key, $0.value) })
        }
        encode(cache, key: cacheKey)
    }

    func clearCache() { cache = [:]; encode(cache, key: cacheKey) }

    private func decode(_ key: String) -> [String: StoredLyrics] {
        guard let data = defaults.data(forKey: key) else { return [:] }
        return (try? JSONDecoder().decode([String: StoredLyrics].self, from: data)) ?? [:]
    }

    private func encode(_ value: [String: StoredLyrics], key: String) {
        if let data = try? JSONEncoder().encode(value) { defaults.set(data, forKey: key) }
    }
}
