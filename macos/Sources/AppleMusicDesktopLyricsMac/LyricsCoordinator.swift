import Combine
import Foundation

@MainActor
final class LyricsCoordinator: ObservableObject {
    let display = LyricsDisplayState()
    private let player = AppleMusicPlayer()
    private let appleLyrics = AppleMusicAccessibilityLyricsProvider()
    private let lrclib = LRCLIBClient()
    private let settings: AppSettings
    private var timer: Timer?
    private var polling = false
    private var currentTrack: TrackInfo?
    private var lrcLines: [LyricLine] = []
    private var loadingKey = ""
    private var appleCurrent = ""
    private var appleStartedAt = 0.0

    init(settings: AppSettings) { self.settings = settings }

    var songKey: String { currentTrack?.key ?? "" }
    var offset: Double { settings.offset(for: songKey) }

    func start() {
        requestPermissions()
        timer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.poll() }
        }
        Task { await poll() }
    }

    func stop() { timer?.invalidate(); timer = nil }

    func requestPermissions() {
        player.requestAutomationPermission()
        _ = appleLyrics.requestPermission(prompt: true)
    }

    func adjustOffset(_ delta: Double) {
        settings.adjustOffset(for: songKey, by: delta)
        showToast(String(format: "%+.1f s", offset))
    }

    func resetOffset() { settings.resetOffset(for: songKey); showToast("0.0 s") }

    func refresh() {
        loadingKey = ""
        lrcLines = []
        Task { await poll(forceReload: true) }
    }

    func showToast(_ message: String) {
        display.toast = message
        Task {
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            if display.toast == message { display.toast = nil }
        }
    }

    private func poll(forceReload: Bool = false) async {
        guard !polling else { return }
        polling = true
        defer { polling = false }
        guard let track = await player.readTrack() else {
            currentTrack = nil
            display.title = "正在等待 Apple Music…"
            display.current = "请先在 Music 中播放一首歌曲"
            display.next = ""
            display.progress = 0
            display.source = .waiting
            return
        }

        let changed = currentTrack?.key != track.key
        currentTrack = track
        display.title = track.title
        display.artist = track.artist
        display.isPlaying = track.isPlaying
        if changed || forceReload {
            lrcLines = []
            appleCurrent = ""
            loadingKey = ""
            display.current = track.title
            display.next = "正在读取歌词…"
            display.progress = 0
        }

        if loadingKey != track.key {
            loadingKey = track.key
            let loaded = await lrclib.lyrics(for: track)
            guard currentTrack?.key == track.key else { return }
            lrcLines = loaded
        }
        if !lrcLines.isEmpty {
            renderLRC(track)
            return
        }

        if let snapshot = await Task.detached(priority: .utility, operation: { [appleLyrics] in
            appleLyrics.read(track: track)
        }).value {
            applyApple(snapshot, track: track)
            return
        }
        renderLRC(track)
    }

    private func applyApple(_ snapshot: AppleLyricSnapshot, track: TrackInfo) {
        if appleCurrent != snapshot.current {
            appleCurrent = snapshot.current
            appleStartedAt = track.position
        }
        display.current = snapshot.current
        display.next = snapshot.next
        display.source = .appleMusic
        if snapshot.isInstrumental {
            display.progress = 0
        } else {
            let elapsed = max(0, track.position + offset - appleStartedAt)
            let estimate = min(6, max(1.4, Double(snapshot.current.count) * 0.28))
            display.progress = min(1, elapsed / estimate)
        }
    }

    private func renderLRC(_ track: TrackInfo) {
        guard !lrcLines.isEmpty else {
            display.current = track.title
            display.next = "未找到同步歌词"
            display.progress = 0
            display.source = .unavailable
            return
        }
        let position = track.position + offset
        let index = lrcLines.lastIndex { $0.time <= position } ?? -1
        if index < 0 {
            display.current = "•••"
            display.next = lrcLines.first?.text ?? ""
            display.progress = 0
        } else {
            display.current = lrcLines[index].text
            display.next = index + 1 < lrcLines.count ? lrcLines[index + 1].text : ""
            if index + 1 < lrcLines.count {
                let natural = lrcLines[index + 1].time - lrcLines[index].time
                let estimate = min(6, max(1.4, Double(lrcLines[index].text.count) * 0.28))
                let active = natural > estimate * 1.35 ? estimate : natural
                display.progress = min(1, max(0, (position - lrcLines[index].time) / max(0.1, active)))
            } else { display.progress = 1 }
        }
        display.source = .lrclib
    }
}
