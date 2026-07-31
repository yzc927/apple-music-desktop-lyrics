import Combine
import Foundation

struct CustomArtistPalette: Codable, Identifiable, Equatable {
    var identity: String
    var colors: [String]
    var id: String { Self.key(identity) }

    static func key(_ value: String) -> String {
        value.unicodeScalars
            .filter { !CharacterSet.whitespacesAndNewlines.contains($0) }
            .map(String.init).joined().uppercased()
    }
}

final class CustomArtistPaletteStore: ObservableObject {
    static let shared = CustomArtistPaletteStore()

    @Published private(set) var palettes: [CustomArtistPalette] = []
    private let defaults = UserDefaults.standard
    private let storageKey = "AppleMusicDesktopLyricsMac.customArtistPalettes.v1"

    private init() {
        guard let data = defaults.data(forKey: storageKey),
              let decoded = try? JSONDecoder().decode([CustomArtistPalette].self, from: data) else { return }
        palettes = Self.deduplicated(decoded)
    }

    func colors(for artist: String) -> [String]? {
        palettes.first { $0.id == CustomArtistPalette.key(artist) }?.colors
    }

    func save(identity: String, colors rawColors: [String]) throws {
        let name = identity.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { throw PaletteError.invalidName }
        let colors = try rawColors.prefix(5).map(Self.normalizeColor)
        guard !colors.isEmpty else { throw PaletteError.invalidColor }
        let palette = CustomArtistPalette(identity: name, colors: colors)
        palettes.removeAll { $0.id == palette.id }
        palettes.append(palette)
        palettes.sort { $0.identity.localizedCaseInsensitiveCompare($1.identity) == .orderedAscending }
        persist()
    }

    func remove(identity: String) {
        palettes.removeAll { $0.id == CustomArtistPalette.key(identity) }
        persist()
    }

    func exportData() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(palettes)
    }

    @discardableResult
    func importData(_ data: Data) throws -> Int {
        let decoded = try JSONDecoder().decode([CustomArtistPalette].self, from: data)
        var merged = palettes
        for item in decoded {
            let colors = try item.colors.prefix(5).map(Self.normalizeColor)
            let name = item.identity.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !name.isEmpty, !colors.isEmpty else { continue }
            let palette = CustomArtistPalette(identity: name, colors: colors)
            merged.removeAll { $0.id == palette.id }
            merged.append(palette)
        }
        palettes = Self.deduplicated(merged)
        persist()
        return decoded.count
    }

    private func persist() {
        if let data = try? JSONEncoder().encode(palettes) {
            defaults.set(data, forKey: storageKey)
        }
    }

    private static func deduplicated(_ values: [CustomArtistPalette]) -> [CustomArtistPalette] {
        var byKey: [String: CustomArtistPalette] = [:]
        for value in values {
            guard !value.identity.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  let colors = try? value.colors.prefix(5).map(normalizeColor),
                  !colors.isEmpty else { continue }
            byKey[value.id] = CustomArtistPalette(identity: value.identity, colors: colors)
        }
        return byKey.values.sorted {
            $0.identity.localizedCaseInsensitiveCompare($1.identity) == .orderedAscending
        }
    }

    private static func normalizeColor(_ raw: String) throws -> String {
        var value = raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if !value.hasPrefix("#") { value = "#" + value }
        if value.count == 7 { value = "#FF" + String(value.dropFirst()) }
        guard value.count == 9,
              value.dropFirst().allSatisfy({ $0.isHexDigit }) else {
            throw PaletteError.invalidColor
        }
        return value
    }

    enum PaletteError: LocalizedError {
        case invalidName
        case invalidColor

        var errorDescription: String? {
            switch self {
            case .invalidName: "请输入歌手或组合名称"
            case .invalidColor: "颜色应为 #RRGGBB，多色用逗号分隔"
            }
        }
    }
}
