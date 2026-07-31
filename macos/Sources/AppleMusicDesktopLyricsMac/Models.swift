import Combine
import Foundation

struct TrackInfo: Equatable {
    let title: String
    let artist: String
    let album: String
    let duration: TimeInterval
    let position: TimeInterval
    let isPlaying: Bool

    var key: String { "\(title)\n\(artist)\n\(Int(duration.rounded()))" }
}

struct LyricLine: Equatable {
    let time: TimeInterval
    let text: String
}

struct AppleLyricSnapshot: Equatable {
    let current: String
    let next: String
    let isInstrumental: Bool
}

enum LyricsSource: String {
    case waiting = "等待 Apple Music"
    case appleMusic = "Apple Music 官方歌词"
    case lrclib = "LRCLIB 同步歌词"
    case cache = "LRCLIB 本地缓存"
    case local = "本地 LRC（永久覆盖）"
    case unavailable = "未找到同步歌词"
}

@MainActor
final class LyricsDisplayState: ObservableObject {
    @Published var title = "正在等待 Apple Music…"
    @Published var artist = ""
    @Published var current = "请先在 Music 中播放一首歌曲"
    @Published var next = ""
    @Published var progress = 0.0
    @Published var source: LyricsSource = .waiting
    @Published var isPlaying = false
    @Published var lyricVersion = "自动匹配"
    @Published var lyricVersionPosition = ""
    @Published var toast: String?
}
