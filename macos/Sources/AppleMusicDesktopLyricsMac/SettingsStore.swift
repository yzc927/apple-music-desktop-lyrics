import AppKit
import Combine
import Foundation

struct SettingsSnapshot: Codable {
    var locked = false
    var alwaysOnTop = true
    var autoColor = true
    var manualColor = "#FF55B8FF"
    var fontFamily = "PingFang SC"
    var songOffsets: [String: Double] = [:]
}

@MainActor
final class AppSettings: ObservableObject {
    static let shared = AppSettings()
    private let defaults = UserDefaults.standard
    private let key = "AppleMusicDesktopLyricsMac.settings.v1"

    @Published var locked = false { didSet { save() } }
    @Published var alwaysOnTop = true { didSet { save() } }
    @Published var autoColor = true { didSet { save() } }
    @Published var manualColor = "#FF55B8FF" { didSet { save() } }
    @Published var fontFamily = "PingFang SC" { didSet { save() } }
    @Published private(set) var songOffsets: [String: Double] = [:]

    private init() {
        guard let data = defaults.data(forKey: key),
              let snapshot = try? JSONDecoder().decode(SettingsSnapshot.self, from: data) else { return }
        locked = snapshot.locked
        alwaysOnTop = snapshot.alwaysOnTop
        autoColor = snapshot.autoColor
        manualColor = snapshot.manualColor
        fontFamily = snapshot.fontFamily
        songOffsets = snapshot.songOffsets
    }

    func offset(for songKey: String) -> Double { songOffsets[songKey] ?? 0 }

    func adjustOffset(for songKey: String, by delta: Double) {
        guard !songKey.isEmpty else { return }
        songOffsets[songKey] = min(30, max(-30, (songOffsets[songKey] ?? 0) + delta))
        objectWillChange.send()
        save()
    }

    func resetOffset(for songKey: String) {
        songOffsets[songKey] = 0
        objectWillChange.send()
        save()
    }

    var curatedFonts: [String] {
        let preferred = [
            "PingFang SC", "Hiragino Sans GB", "Hiragino Kaku Gothic ProN",
            "SF Pro Display", "Helvetica Neue", "Arial Unicode MS",
            "Source Han Sans SC", "Noto Sans CJK SC", "LXGW WenKai"
        ]
        let installed = Set(NSFontManager.shared.availableFontFamilies)
        return preferred.filter(installed.contains)
    }

    private func save() {
        let snapshot = SettingsSnapshot(
            locked: locked, alwaysOnTop: alwaysOnTop, autoColor: autoColor,
            manualColor: manualColor, fontFamily: fontFamily, songOffsets: songOffsets
        )
        if let data = try? JSONEncoder().encode(snapshot) { defaults.set(data, forKey: key) }
    }
}

extension NSColor {
    convenience init?(argbHex: String) {
        let value = argbHex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)
        guard let number = UInt64(value, radix: 16) else { return nil }
        let a, r, g, b: CGFloat
        if value.count == 8 {
            a = CGFloat((number >> 24) & 0xff) / 255
            r = CGFloat((number >> 16) & 0xff) / 255
            g = CGFloat((number >> 8) & 0xff) / 255
            b = CGFloat(number & 0xff) / 255
        } else if value.count == 6 {
            a = 1; r = CGFloat((number >> 16) & 0xff) / 255
            g = CGFloat((number >> 8) & 0xff) / 255; b = CGFloat(number & 0xff) / 255
        } else { return nil }
        self.init(srgbRed: r, green: g, blue: b, alpha: a)
    }
}
