import AppKit
import SwiftUI

struct OverlayView: View {
    @ObservedObject var display: LyricsDisplayState
    @ObservedObject var settings: AppSettings
    let coordinator: LyricsCoordinator
    let close: () -> Void

    @State private var toolbarVisible = false

    private var colors: [Color] {
        let values = settings.autoColor ? ArtistColorEngine.colors(for: display.artist) : [settings.manualColor]
        return values.compactMap { value in NSColor(argbHex: value).map { Color(nsColor: $0) } }
    }

    var body: some View {
        GeometryReader { geometry in
            let scale = min(2.2, max(0.65, min(geometry.size.width / 760, geometry.size.height / 170)))
            ZStack(alignment: .top) {
                VStack(spacing: 7 * scale) {
                    ProgressLyricText(
                        text: display.current,
                        progress: display.progress,
                        colors: colors.isEmpty ? [.cyan] : colors,
                        font: lyricFont(size: 38 * scale)
                    )
                    Text(display.next)
                        .font(lyricFont(size: 24 * scale))
                        .foregroundStyle(.white.opacity(0.92))
                        .multilineTextAlignment(.center)
                        .lineLimit(2)
                        .shadow(color: .black.opacity(0.9), radius: 3, x: 0, y: 2)
                }
                .padding(.horizontal, 22 * scale)
                .padding(.top, settings.locked ? 14 * scale : 38 * scale)
                .padding(.bottom, 10 * scale)
                .frame(maxWidth: .infinity, maxHeight: .infinity)

                if !settings.locked && toolbarVisible {
                    toolbar(scale: scale)
                        .transition(.opacity.combined(with: .move(edge: .top)))
                }

                if let toast = display.toast {
                    Text(toast)
                        .font(.system(size: 13, weight: .semibold))
                        .padding(.horizontal, 12).padding(.vertical, 7)
                        .background(.black.opacity(0.78), in: Capsule())
                        .foregroundStyle(.white)
                        .offset(y: 4)
                        .transition(.opacity.combined(with: .move(edge: .top)))
                }
            }
            .contentShape(Rectangle())
            .onHover { inside in
                guard !settings.locked else { return }
                withAnimation(.easeInOut(duration: 0.2)) { toolbarVisible = inside }
            }
        }
        .background(Color.clear)
        .animation(.easeInOut(duration: 0.25), value: display.toast)
    }

    private func lyricFont(size: CGFloat) -> Font {
        .custom(settings.fontFamily, fixedSize: size).weight(.bold)
    }

    @ViewBuilder
    private func toolbar(scale: CGFloat) -> some View {
        HStack(spacing: 3) {
            toolButton("backward.end.alt", help: "歌词慢 0.5 秒") { coordinator.adjustOffset(-0.5) }
                .overlay(alignment: .bottomTrailing) { badge("0.5") }
            toolButton("forward.end.alt", help: "歌词快 0.5 秒") { coordinator.adjustOffset(0.5) }
                .overlay(alignment: .bottomTrailing) { badge("0.5") }
            toolButton("lock.open.fill", help: "锁定并启用鼠标穿透") { settings.locked = true }
            toolButton(settings.alwaysOnTop ? "pin.fill" : "pin", help: "切换置顶") {
                settings.alwaysOnTop.toggle()
            }
            toolButton("paintpalette.fill", help: "切换手动颜色") {
                settings.autoColor = false
                let choices = ["#FFFF6FAE", "#FF55B8FF", "#FF46C8C8", "#FF9B72F2", "#FFFF9F43"]
                let index = (choices.firstIndex(of: settings.manualColor) ?? -1) + 1
                settings.manualColor = choices[index % choices.count]
                coordinator.showToast("手动配色")
            }
            toolButton("sparkles", help: "按歌手自动配色") {
                settings.autoColor = true
                coordinator.showToast("歌手自动配色")
            }
            toolButton("xmark", help: "隐藏到菜单栏") { close() }
        }
        .padding(4)
        .background(.black.opacity(0.78), in: RoundedRectangle(cornerRadius: 6))
        .scaleEffect(max(0.85, min(1.25, scale)))
    }

    private func toolButton(_ systemName: String, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: systemName).frame(width: 27, height: 25).foregroundStyle(.white)
        }
        .buttonStyle(.plain)
        .help(help)
    }

    private func badge(_ text: String) -> some View {
        Text(text).font(.system(size: 7, weight: .bold)).foregroundStyle(.white).offset(x: -1, y: -1)
    }
}

private struct ProgressLyricText: View {
    let text: String
    let progress: Double
    let colors: [Color]
    let font: Font

    var body: some View {
        GeometryReader { geometry in
            ZStack {
                lyric.foregroundStyle(.white)
                lyric
                    .foregroundStyle(LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
                    .mask(alignment: .leading) {
                        Rectangle().frame(width: geometry.size.width * min(1, max(0, progress)))
                    }
            }
        }
        .frame(minHeight: 52)
    }

    private var lyric: some View {
        Text(text)
            .font(font)
            .multilineTextAlignment(.center)
            .lineLimit(2)
            .minimumScaleFactor(0.55)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .shadow(color: .black.opacity(0.95), radius: 4, x: 0, y: 2)
    }
}
