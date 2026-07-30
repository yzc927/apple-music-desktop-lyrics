import AppKit
import Foundation
import SwiftUI

struct ManagementView: View {
    @ObservedObject var settings: AppSettings
    @ObservedObject var display: LyricsDisplayState
    let coordinator: LyricsCoordinator
    let showOverlay: () -> Void
    let requestAccessibility: () -> Void

    private let manualColors = ["#FFFF6FAE", "#FF55B8FF", "#FF46C8C8", "#FF5F7CFF", "#FF9B72F2", "#FFFF9F43"]

    var body: some View {
        Form {
            Section("当前歌曲") {
                LabeledContent("歌曲", value: display.title)
                LabeledContent("歌手", value: display.artist.isEmpty ? "—" : display.artist)
                LabeledContent("歌词来源", value: display.source.rawValue)
                LabeledContent("歌词版本", value: display.lyricVersionPosition.isEmpty
                    ? display.lyricVersion : "\(display.lyricVersionPosition) · \(display.lyricVersion)")
                LabeledContent("当前偏移", value: String(format: "%+.1f 秒", coordinator.offset))
                HStack {
                    Button("歌词慢 0.5 秒") { coordinator.adjustOffset(-0.5) }
                    Button("歌词快 0.5 秒") { coordinator.adjustOffset(0.5) }
                    Button("归零") { coordinator.resetOffset() }
                    Button("重新读取") { coordinator.refresh() }
                }
                HStack {
                    Button("上一个歌词版本") { coordinator.changeLyricsCandidate(-1) }
                    Button("下一个歌词版本") { coordinator.changeLyricsCandidate(1) }
                }
                Toggle("Apple 实验自动对时", isOn: $settings.automaticLyricsCalibration)
                Text("默认关闭；LRCLIB 时间明显不准时再尝试开启。")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("窗口") {
                Toggle("锁定位置并启用鼠标穿透", isOn: $settings.locked)
                Toggle("固定到最前端", isOn: $settings.alwaysOnTop)
                Button("显示歌词窗口", action: showOverlay)
            }

            Section("字体") {
                Picker("歌词字体", selection: $settings.fontFamily) {
                    ForEach(settings.curatedFonts, id: \.self) { Text($0).font(.custom($0, size: 13)).tag($0) }
                }
            }

            Section("歌词显示") {
                Picker("显示模式", selection: $settings.karaokeMode) {
                    Text("普通").tag(false)
                    Text("卡拉 OK").tag(true)
                }
                .pickerStyle(.segmented)
                Text(settings.karaokeMode
                    ? "按播放进度从左向右扫色。"
                    : "当前句从一开始就完整显示歌手颜色。")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("颜色") {
                Toggle("按歌手自动配色", isOn: $settings.autoColor)
                HStack {
                    Text("当前歌手配色")
                    Spacer()
                    ForEach(Array(ArtistColorEngine.colors(
                        for: display.artist, unknownArtistColor: settings.manualColor
                    ).enumerated()), id: \.offset) { item in
                        Circle().fill(Color(nsColor: NSColor(argbHex: item.element) ?? .cyan)).frame(width: 22, height: 22)
                    }
                }
                HStack {
                    ForEach(manualColors, id: \.self) { value in
                        Button {
                            settings.manualColor = value
                        } label: {
                            Circle().fill(Color(nsColor: NSColor(argbHex: value) ?? .cyan))
                                .frame(width: 26, height: 26)
                                .overlay(Circle().stroke(settings.manualColor == value ? Color.primary : Color.clear, lineWidth: 2))
                        }.buttonStyle(.plain)
                    }
                }
                DisclosureGroup("查看已收录歌手（\(ArtistColorEngine.curatedPalettes.count)）") {
                    Text(ArtistColorEngine.curatedPalettes.map(\.identity).joined(separator: "、"))
                        .font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
                }
            }

            Section("权限") {
                Text("官方歌词后备读取需要“辅助功能”权限；曲目信息需要允许控制 Music。")
                    .font(.caption).foregroundStyle(.secondary)
                Button("请求辅助功能权限", action: requestAccessibility)
            }
        }
        .formStyle(.grouped)
        .padding(8)
        .frame(width: 560, height: 530)
    }
}

@MainActor
final class ManagementWindowController {
    private let window: NSWindow

    init(settings: AppSettings, coordinator: LyricsCoordinator, overlay: OverlayWindowController,
         requestAccessibility: @escaping () -> Void) {
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 560, height: 530),
            styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false
        )
        window.title = "Apple Music 桌面歌词管理"
        window.center()
        window.isReleasedWhenClosed = false
        window.contentView = NSHostingView(rootView: ManagementView(
            settings: settings, display: coordinator.display, coordinator: coordinator,
            showOverlay: { overlay.show() }, requestAccessibility: requestAccessibility
        ))
    }

    func show() {
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }
}
