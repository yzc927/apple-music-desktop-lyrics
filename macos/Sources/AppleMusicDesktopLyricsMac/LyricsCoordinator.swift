import Combine
import Foundation

@MainActor
final class LyricsCoordinator: ObservableObject {
    let display = LyricsDisplayState()
    private let player = AppleMusicPlayer()
    private let appleLyrics = AppleMusicAccessibilityLyricsProvider()
    private let lrclib = LRCLIBClient()
    private let settings: AppSettings
    private var pollTimer: Timer?
    private var renderTimer: Timer?
    private var polling = false
    private var currentTrack: TrackInfo?
    private var sampledAt = ProcessInfo.processInfo.systemUptime
    private var lrcLines: [LyricLine] = []
    private var candidates: [LyricCandidate] = []
    private var candidateIndex = -1
    private var loadingKey = ""
    private var appleCurrent = ""
    private var appleStartedAt = 0.0
    private var automaticOffset = 0.0
    private var calibrationSamples: [Double] = []
    private var calibrationAppleCurrent = ""
    private var pendingCalibrationCurrent = ""
    private var pendingCalibrationNext = ""
    private var pendingCalibrationCount = 0
    private var lastCalibrationLineIndex = -1
    private var lastCalibrationReadAt = 0.0
    private var secondsPerVocalUnit = 0.28
    private var lastRenderedIndex = -1
    private var lastProgressIndex = -1
    private var lastProgress = 0.0

    init(settings: AppSettings) { self.settings = settings }

    var songKey: String { currentTrack?.key ?? "" }
    var offset: Double { settings.offset(for: songKey) }
    var lyricCandidateCount: Int { candidates.count }

    func start() {
        requestPermissions()
        pollTimer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: true) { [coordinator = self] _ in
            Task { @MainActor in await coordinator.poll() }
        }
        renderTimer = Timer.scheduledTimer(withTimeInterval: 0.08, repeats: true) { [coordinator = self] _ in
            Task { @MainActor in coordinator.renderFrame() }
        }
        Task { await poll() }
    }

    func stop() {
        pollTimer?.invalidate(); pollTimer = nil
        renderTimer?.invalidate(); renderTimer = nil
    }

    func requestPermissions() {
        player.requestAutomationPermission()
        _ = appleLyrics.requestPermission(prompt: true)
    }

    func adjustOffset(_ delta: Double) {
        settings.adjustOffset(for: songKey, by: delta)
        lastRenderedIndex = -1
        lastProgressIndex = -1
        lastProgress = 0
        showToast(String(format: "%+.1f s", offset))
    }

    func resetOffset() {
        settings.resetOffset(for: songKey)
        lastRenderedIndex = -1
        lastProgressIndex = -1
        lastProgress = 0
        showToast("0.0 s")
    }

    func changeLyricsCandidate(_ delta: Int) {
        guard candidates.count > 1 else { showToast("当前只有一个匹配版本"); return }
        var next = (candidateIndex + delta) % candidates.count
        if next < 0 { next += candidates.count }
        applyCandidate(next, remember: true)
        showToast("已切换歌词版本 \(next + 1)/\(candidates.count)")
    }

    func refresh() {
        loadingKey = ""
        lrcLines = []
        candidates = []
        candidateIndex = -1
        resetTimingState()
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

        let now = ProcessInfo.processInfo.systemUptime
        let changed = currentTrack?.key != track.key
        if !changed, let previous = currentTrack {
            let predicted = playbackPosition(for: previous, at: now)
            if abs(track.position - predicted) >= 1.5 {
                lastRenderedIndex = -1
                lastProgressIndex = -1
                lastProgress = 0
                automaticOffset = 0
                calibrationSamples = []
            }
        }
        currentTrack = track
        sampledAt = now
        display.title = track.title
        display.artist = track.artist
        display.isPlaying = track.isPlaying
        if changed || forceReload {
            lrcLines = []
            candidates = []
            candidateIndex = -1
            appleCurrent = ""
            loadingKey = ""
            display.current = track.title
            display.next = "正在读取歌词…"
            display.progress = 0
            display.lyricVersion = "自动匹配"
            display.lyricVersionPosition = ""
            resetTimingState()
        }

        if loadingKey != track.key {
            loadingKey = track.key
            let loaded = await lrclib.candidates(for: track)
            guard currentTrack?.key == track.key else { return }
            candidates = loaded
            if !loaded.isEmpty {
                let remembered = settings.lyricChoice(for: track.key)
                let selected = remembered.flatMap { key in loaded.firstIndex { $0.key == key } } ?? 0
                applyCandidate(selected, remember: false)
            }
        }
        if !lrcLines.isEmpty {
            renderLRC(position: playbackPosition(for: track))
            if settings.automaticLyricsCalibration && now - lastCalibrationReadAt >= 0.7 {
                lastCalibrationReadAt = now
                if let snapshot = await Task.detached(priority: .utility, operation: { [appleLyrics] in
                    appleLyrics.read(track: track)
                }).value, currentTrack?.key == track.key {
                    applyCalibration(snapshot)
                }
            }
            return
        }

        if let snapshot = await Task.detached(priority: .utility, operation: { [appleLyrics] in
            appleLyrics.read(track: track)
        }).value {
            applyApple(snapshot, position: playbackPosition(for: track))
            return
        }
        renderUnavailable(track)
    }

    private func renderFrame() {
        guard let track = currentTrack, !lrcLines.isEmpty else { return }
        renderLRC(position: playbackPosition(for: track))
    }

    private func playbackPosition(for track: TrackInfo, at now: Double = ProcessInfo.processInfo.systemUptime) -> Double {
        track.position + (track.isPlaying ? max(0, min(2, now - sampledAt)) : 0)
    }

    private func applyCandidate(_ index: Int, remember: Bool) {
        guard candidates.indices.contains(index) else { return }
        candidateIndex = index
        lrcLines = candidates[index].lines
        secondsPerVocalUnit = LyricTiming.secondsPerUnit(lrcLines)
        resetTimingState(keepTempo: true)
        display.lyricVersion = candidates[index].label
        display.lyricVersionPosition = "\(index + 1)/\(candidates.count)"
        if remember { settings.setLyricChoice(candidates[index].key, for: songKey) }
    }

    private func applyCalibration(_ snapshot: AppleLyricSnapshot) {
        guard settings.automaticLyricsCalibration, !snapshot.isInstrumental,
              !snapshot.current.isEmpty, let track = currentTrack else { return }
        if pendingCalibrationCurrent != snapshot.current {
            pendingCalibrationCurrent = snapshot.current
            pendingCalibrationNext = snapshot.next
            pendingCalibrationCount = 1
            return
        }
        pendingCalibrationNext = snapshot.next
        pendingCalibrationCount += 1
        guard pendingCalibrationCount >= 2, snapshot.current != calibrationAppleCurrent else { return }
        let rawPosition = playbackPosition(for: track)
        let expected = rawPosition + (settings.automaticLyricsCalibration ? automaticOffset : 0) + offset
        guard let index = LyricTiming.calibrationLine(
            lines: lrcLines, current: snapshot.current, next: pendingCalibrationNext,
            expectedPosition: expected
        ), index > lastCalibrationLineIndex else { return }
        let correction = lrcLines[index].time - rawPosition
        guard abs(correction) <= 8 else { return }
        if !calibrationSamples.isEmpty, abs(correction - automaticOffset) > 1.25 { return }
        calibrationSamples.append(correction)
        if calibrationSamples.count > 5 { calibrationSamples.removeFirst() }
        let ordered = calibrationSamples.sorted()
        let median = ordered[ordered.count / 2]
        automaticOffset = min(6, max(-6, calibrationSamples.count == 1
            ? median : automaticOffset + min(0.2, max(-0.2, median - automaticOffset))))
        calibrationAppleCurrent = snapshot.current
        lastCalibrationLineIndex = index
    }

    private func applyApple(_ snapshot: AppleLyricSnapshot, position: Double) {
        if appleCurrent != snapshot.current {
            appleCurrent = snapshot.current
            appleStartedAt = position
        }
        display.current = snapshot.current
        display.next = snapshot.next
        display.source = .appleMusic
        display.lyricVersion = "Apple Music 官方歌词"
        display.lyricVersionPosition = ""
        if snapshot.isInstrumental {
            display.progress = 0
        } else {
            let elapsed = max(0, position + offset - appleStartedAt)
            let estimate = min(6, max(1.4, Double(snapshot.current.count) * 0.28))
            display.progress = min(1, elapsed / estimate)
        }
    }

    private func renderLRC(position rawPosition: Double) {
        let position = rawPosition + (settings.automaticLyricsCalibration ? automaticOffset : 0) + offset
        var index = lrcLines.lastIndex { $0.time <= position } ?? -1
        if lastRenderedIndex >= 0, index < lastRenderedIndex { index = lastRenderedIndex }
        lastRenderedIndex = index
        if index < 0 {
            display.current = "•••"
            display.next = lrcLines.first.map { LyricTiming.displayText($0.text) } ?? ""
            display.progress = 0
        } else {
            display.current = LyricTiming.displayText(lrcLines[index].text)
            display.next = index + 1 < lrcLines.count ? LyricTiming.displayText(lrcLines[index + 1].text) : ""
            var progress = LyricTiming.progress(
                lines: lrcLines, index: index, position: position, secondsPerUnit: secondsPerVocalUnit
            )
            if index == lastProgressIndex { progress = max(progress, lastProgress) }
            else { lastProgressIndex = index; lastProgress = 0 }
            lastProgress = progress
            display.progress = progress
        }
        display.source = .lrclib
    }

    private func renderUnavailable(_ track: TrackInfo) {
        display.current = track.title
        display.next = "未找到同步歌词"
        display.progress = 0
        display.source = .unavailable
        display.lyricVersion = "没有可用版本"
        display.lyricVersionPosition = ""
    }

    private func resetTimingState(keepTempo: Bool = false) {
        automaticOffset = 0
        calibrationSamples = []
        calibrationAppleCurrent = ""
        pendingCalibrationCurrent = ""
        pendingCalibrationNext = ""
        pendingCalibrationCount = 0
        lastCalibrationLineIndex = -1
        lastRenderedIndex = -1
        lastProgressIndex = -1
        lastProgress = 0
        if !keepTempo { secondsPerVocalUnit = 0.28 }
    }
}
