import AppKit
import Foundation

@main
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let settings = AppSettings.shared
    private lazy var coordinator = LyricsCoordinator(settings: settings)
    private var overlay: OverlayWindowController!
    private var management: ManagementWindowController!
    private var statusItem: NSStatusItem!
    private var showHideItem: NSMenuItem!
    private var lockItem: NSMenuItem!
    private var topItem: NSMenuItem!

    static func main() {
        let application = NSApplication.shared
        let delegate = AppDelegate()
        application.delegate = delegate
        application.run()
        withExtendedLifetime(delegate) {}
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        overlay = OverlayWindowController(settings: settings, coordinator: coordinator)
        management = ManagementWindowController(
            settings: settings, coordinator: coordinator, overlay: overlay,
            requestAccessibility: { [weak self] in self?.coordinator.requestPermissions() }
        )
        configureStatusItem()
        overlay.show()
        coordinator.start()
    }

    func applicationWillTerminate(_ notification: Notification) { coordinator.stop() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func configureStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "captions.bubble.fill", accessibilityDescription: "桌面歌词")
        statusItem.button?.toolTip = "Apple Music 桌面歌词"

        let menu = NSMenu(title: "Apple Music 桌面歌词")
        menu.delegate = self
        showHideItem = menu.addItem(withTitle: "隐藏歌词", action: #selector(toggleOverlay), keyEquivalent: "")
        lockItem = menu.addItem(withTitle: "锁定位置", action: #selector(toggleLock), keyEquivalent: "")
        topItem = menu.addItem(withTitle: "取消置顶", action: #selector(toggleTopmost), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "打开管理界面…", action: #selector(showManagement), keyEquivalent: ",")
        menu.addItem(withTitle: "重新读取歌词", action: #selector(refreshLyrics), keyEquivalent: "r")
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出", action: #selector(quit), keyEquivalent: "q")
        for item in menu.items { item.target = self }
        statusItem.menu = menu
    }

    func menuWillOpen(_ menu: NSMenu) {
        showHideItem.title = overlay.isVisible ? "隐藏歌词" : "显示歌词"
        lockItem.title = settings.locked ? "解锁位置" : "锁定位置"
        topItem.title = settings.alwaysOnTop ? "取消置顶" : "固定到最前端"
    }

    @objc private func toggleOverlay() { overlay.toggleVisibility() }
    @objc private func toggleLock() { settings.locked.toggle() }
    @objc private func toggleTopmost() { settings.alwaysOnTop.toggle() }
    @objc private func showManagement() { management.show() }
    @objc private func refreshLyrics() { coordinator.refresh() }
    @objc private func quit() { NSApp.terminate(nil) }
}
