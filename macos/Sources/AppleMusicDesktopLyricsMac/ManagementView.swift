import AppKit
import Foundation
import SwiftUI

struct ManagementView: View {
    @ObservedObject var settings: AppSettings
    @ObservedObject var display: LyricsDisplayState
    @ObservedObject private var customPalettes = CustomArtistPaletteStore.shared
    let coordinator: LyricsCoordinator
    let showOverlay: () -> Void
    let requestAccessibility: () -> Void

    private let manualColors = ["#FFFF6FAE", "#FF55B8FF", "#FF46C8C8", "#FF5F7CFF", "#FF9B72F2", "#FFFF9F43"]
    @State private var lrcDraft = ""
    @State private var showingLyricsEditor = false
    @State private var artistName = ""
    @State private var artistColors = ""
    @State private var operationMessage = ""

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

            Section("本地歌词与缓存") {
                HStack {
                    Button("导入 .lrc", action: importLRC)
                    Button("编辑当前歌词") {
                        lrcDraft = coordinator.currentLRCText
                        showingLyricsEditor = true
                    }
                    Button("删除本地覆盖") { coordinator.removeLocalLyricsOverride() }
                        .disabled(!coordinator.hasLocalLyricsOverride)
                    Button("清空缓存") { coordinator.clearLyricsCache() }
                }
                Text(coordinator.hasLocalLyricsOverride
                    ? "当前歌曲使用永久本地覆盖。"
                    : coordinator.hasCachedLyrics ? "当前歌曲已有离线缓存。" : "当前歌曲没有本地歌词。")
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

            Section("歌手配色管理器") {
                TextField("歌手或组合名称", text: $artistName)
                TextField("颜色顺序，例如 #F25F7C, #55B8FF", text: $artistColors)
                Text("多种颜色按填写顺序生成渐变，最多 5 色；自定义配色优先于内置配色。")
                    .font(.caption).foregroundStyle(.secondary)
                HStack {
                    Button("使用当前歌手") {
                        artistName = display.artist
                        artistColors = ArtistColorEngine.colors(
                            for: display.artist, unknownArtistColor: settings.manualColor
                        ).map { rgbHex($0) }.joined(separator: ", ")
                    }
                    Button("新增／保存", action: savePalette)
                    Button("删除自定义") {
                        customPalettes.remove(identity: artistName)
                        operationMessage = "已删除自定义配色；内置歌手会恢复原配色。"
                    }
                    Button("导入", action: importPalettes)
                    Button("导出", action: exportPalettes)
                }
                if !customPalettes.palettes.isEmpty {
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 4) {
                            ForEach(customPalettes.palettes) { palette in
                                Button {
                                    artistName = palette.identity
                                    artistColors = palette.colors.map { rgbHex($0) }.joined(separator: ", ")
                                } label: {
                                    HStack {
                                        Text(palette.identity)
                                        Spacer()
                                        ForEach(Array(palette.colors.enumerated()), id: \.offset) { item in
                                            Circle()
                                                .fill(Color(nsColor: NSColor(argbHex: item.element) ?? .cyan))
                                                .frame(width: 14, height: 14)
                                        }
                                    }
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    .frame(maxHeight: 110)
                }
                if !operationMessage.isEmpty {
                    Text(operationMessage).font(.caption).foregroundStyle(.secondary)
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
        .frame(width: 700, height: 760)
        .sheet(isPresented: $showingLyricsEditor) {
            LyricsEditorSheet(text: $lrcDraft, isPresented: $showingLyricsEditor) {
                if let error = coordinator.setLocalLyrics(lrcDraft) {
                    operationMessage = error
                    return false
                }
                operationMessage = "已保存当前歌曲的本地歌词。"
                return true
            }
        }
    }

    private func importLRC() {
        let panel = NSOpenPanel()
        panel.allowedFileTypes = ["lrc"]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            lrcDraft = try String(contentsOf: url, encoding: .utf8)
            showingLyricsEditor = true
        } catch {
            operationMessage = "读取失败：\(error.localizedDescription)"
        }
    }

    private func savePalette() {
        do {
            try customPalettes.save(
                identity: artistName,
                colors: artistColors.split(separator: ",").map(String.init)
            )
            operationMessage = "已保存歌手配色。"
        } catch {
            operationMessage = error.localizedDescription
        }
    }

    private func importPalettes() {
        let panel = NSOpenPanel()
        panel.allowedFileTypes = ["json"]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let count = try customPalettes.importData(Data(contentsOf: url))
            operationMessage = "已导入 \(count) 条配色。"
        } catch {
            operationMessage = "导入失败：\(error.localizedDescription)"
        }
    }

    private func exportPalettes() {
        let panel = NSSavePanel()
        panel.allowedFileTypes = ["json"]
        panel.nameFieldStringValue = "artist-palettes.json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try customPalettes.exportData().write(to: url, options: .atomic)
            operationMessage = "配色库已导出。"
        } catch {
            operationMessage = "导出失败：\(error.localizedDescription)"
        }
    }

    private func rgbHex(_ argb: String) -> String {
        argb.count == 9 ? "#" + String(argb.suffix(6)) : argb
    }
}

private struct LyricsEditorSheet: View {
    @Binding var text: String
    @Binding var isPresented: Bool
    let save: () -> Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("编辑本地同步歌词").font(.headline)
            Text("每行格式：[mm:ss.xx] 歌词。保存后永久优先于在线歌词。")
                .font(.caption).foregroundStyle(.secondary)
            TextEditor(text: $text)
                .font(.system(.body, design: .monospaced))
                .border(Color.secondary.opacity(0.35))
            HStack {
                Spacer()
                Button("取消") { isPresented = false }
                Button("保存") {
                    if save() { isPresented = false }
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 640, height: 500)
    }
}

@MainActor
final class ManagementWindowController {
    private let window: NSWindow

    init(settings: AppSettings, coordinator: LyricsCoordinator, overlay: OverlayWindowController,
         requestAccessibility: @escaping () -> Void) {
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 700, height: 760),
            styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false
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
