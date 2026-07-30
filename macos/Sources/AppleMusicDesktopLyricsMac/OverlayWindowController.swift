import AppKit
import Combine
import SwiftUI

@MainActor
final class OverlayWindowController: NSObject, NSWindowDelegate {
    private let settings: AppSettings
    private let coordinator: LyricsCoordinator
    private let panel: NSPanel
    private let unlockPanel: NSPanel
    private var cancellables: Set<AnyCancellable> = []
    private var hoverTimer: Timer?
    private var hoverStarted: Date?
    private var unlockShowing = false

    init(settings: AppSettings, coordinator: LyricsCoordinator) {
        self.settings = settings
        self.coordinator = coordinator
        panel = NSPanel(
            contentRect: NSRect(x: 260, y: 110, width: 760, height: 170),
            styleMask: [.borderless, .resizable, .nonactivatingPanel],
            backing: .buffered, defer: false
        )
        unlockPanel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 44, height: 42),
            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false
        )
        super.init()

        configurePanel()
        configureUnlockPanel()
        restoreFrame()
        bindSettings()
        startHoverTracking()
    }

    deinit { hoverTimer?.invalidate() }

    func show() { panel.orderFrontRegardless() }
    func hide() { panel.orderOut(nil); hideUnlock() }
    func toggleVisibility() { panel.isVisible ? hide() : show() }
    var isVisible: Bool { panel.isVisible }

    private func configurePanel() {
        panel.title = "Apple Music 桌面歌词"
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.isMovableByWindowBackground = true
        panel.minSize = NSSize(width: 360, height: 105)
        panel.maxSize = NSSize(width: 1_800, height: 520)
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        panel.hidesOnDeactivate = false
        panel.delegate = self
        panel.contentView = NSHostingView(rootView: OverlayView(
            display: coordinator.display, settings: settings, coordinator: coordinator,
            close: { [weak self] in self?.hide() }
        ))
    }

    private func configureUnlockPanel() {
        unlockPanel.isOpaque = false
        unlockPanel.backgroundColor = .clear
        unlockPanel.hasShadow = false
        unlockPanel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        unlockPanel.hidesOnDeactivate = false
        unlockPanel.contentView = NSHostingView(rootView:
            Button { [weak self] in self?.settings.locked = false } label: {
                Image(systemName: "lock.fill")
                    .font(.system(size: 23, weight: .bold))
                    .foregroundStyle(.white)
                    .frame(width: 40, height: 38)
                    .background(.black.opacity(0.82), in: RoundedRectangle(cornerRadius: 5))
            }.buttonStyle(.plain).help("解锁歌词位置")
        )
    }

    private func bindSettings() {
        settings.$locked.sink { [weak self] locked in
            guard let self else { return }
            panel.ignoresMouseEvents = locked
            if !locked { self.hideUnlock() }
        }.store(in: &cancellables)
        settings.$alwaysOnTop.sink { [weak self] top in
            guard let self else { return }
            panel.level = top ? .floating : .normal
            unlockPanel.level = top ? .statusBar : .floating
        }.store(in: &cancellables)
    }

    private func startHoverTracking() {
        hoverTimer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [controller = self] _ in
            Task { @MainActor in controller.updateUnlockHover() }
        }
    }

    private func updateUnlockHover() {
        guard settings.locked, panel.isVisible else { hoverStarted = nil; hideUnlock(); return }
        let inside = panel.frame.contains(NSEvent.mouseLocation)
        if inside {
            if hoverStarted == nil { hoverStarted = Date() }
            if Date().timeIntervalSince(hoverStarted!) >= 1 {
                let frame = panel.frame
                unlockPanel.setFrameOrigin(NSPoint(x: frame.maxX - 48, y: frame.maxY - 45))
                showUnlock()
            }
        } else {
            hoverStarted = nil
            hideUnlock()
        }
    }

    private func showUnlock() {
        guard !unlockShowing else { return }
        unlockShowing = true
        unlockPanel.alphaValue = 0
        unlockPanel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.22
            unlockPanel.animator().alphaValue = 1
        }
    }

    private func hideUnlock() {
        guard unlockShowing else { unlockPanel.orderOut(nil); return }
        unlockShowing = false
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.18
            unlockPanel.animator().alphaValue = 0
        }, completionHandler: { [weak unlockPanel = self.unlockPanel] in
            unlockPanel?.orderOut(nil)
        })
    }

    func windowDidMove(_ notification: Notification) { saveFrame() }
    func windowDidResize(_ notification: Notification) { saveFrame() }

    private func saveFrame() {
        UserDefaults.standard.set(NSStringFromRect(panel.frame), forKey: "OverlayWindowFrame")
    }

    private func restoreFrame() {
        var restoredSuccessfully = false
        if let value = UserDefaults.standard.string(forKey: "OverlayWindowFrame") {
            let restored = NSRectFromString(value)
            if restored.width >= 360, restored.height >= 105,
               NSScreen.screens.contains(where: { $0.visibleFrame.intersects(restored) }) {
                panel.setFrame(restored, display: false)
                restoredSuccessfully = true
            }
        }
        if !restoredSuccessfully, let screen = NSScreen.main {
            let x = screen.visibleFrame.midX - panel.frame.width / 2
            panel.setFrameOrigin(NSPoint(x: x, y: screen.visibleFrame.minY + 70))
        }
    }
}
