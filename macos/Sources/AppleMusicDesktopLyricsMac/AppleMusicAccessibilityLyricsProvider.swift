import AppKit
import ApplicationServices
import Foundation

final class AppleMusicAccessibilityLyricsProvider {
    private struct Candidate {
        let element: AXUIElement
        let text: String
        let frame: CGRect
        let emphasis: Double
    }

    func requestPermission(prompt: Bool = true) -> Bool {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: prompt] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    func read(track: TrackInfo) -> AppleLyricSnapshot? {
        guard AXIsProcessTrusted(),
              let music = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.Music").first else {
            return nil
        }
        let app = AXUIElementCreateApplication(music.processIdentifier)
        guard let windows: [AXUIElement] = attribute(app, kAXWindowsAttribute as CFString),
              let window = windows.first,
              let windowFrame = frame(of: window) else { return nil }

        var candidates: [Candidate] = []
        var visited = 0
        collect(
            element: window, depth: 0, visited: &visited, windowFrame: windowFrame,
            excluding: [track.title, track.artist, track.album], into: &candidates
        )
        guard !candidates.isEmpty else { return nil }

        let ordered = candidates
            .filter { $0.frame.intersects(windowFrame) }
            .sorted { lhs, rhs in
                abs(lhs.frame.minY - rhs.frame.minY) < 2 ? lhs.frame.minX < rhs.frame.minX : lhs.frame.minY < rhs.frame.minY
            }
        guard !ordered.isEmpty else { return nil }

        let strongest = ordered.max { $0.emphasis < $1.emphasis }!
        let scoreRange = (ordered.map(\.emphasis).max() ?? 0) - (ordered.map(\.emphasis).min() ?? 0)
        let current: Candidate
        if scoreRange > 0.12 {
            current = strongest
        } else {
            let anchorY = windowFrame.minY + windowFrame.height * 0.22
            current = ordered.min { abs($0.frame.midY - anchorY) < abs($1.frame.midY - anchorY) }!
        }
        guard let index = ordered.firstIndex(where: { CFEqual($0.element, current.element) }) else { return nil }
        let next = index + 1 < ordered.count ? ordered[index + 1].text : ""
        let instrumental = current.text.allSatisfy { $0 == "•" || $0 == "." || $0 == "·" || $0.isWhitespace }
        return AppleLyricSnapshot(current: instrumental ? "•••" : current.text, next: next, isInstrumental: instrumental)
    }

    private func collect(
        element: AXUIElement, depth: Int, visited: inout Int, windowFrame: CGRect,
        excluding: [String], into output: inout [Candidate]
    ) {
        guard depth <= 18, visited < 4_000 else { return }
        visited += 1
        let role: String? = attribute(element, kAXRoleAttribute as CFString)
        if role == (kAXStaticTextRole as String),
           let text: String = attribute(element, kAXValueAttribute as CFString),
           let itemFrame = frame(of: element) {
            let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
            let lyricZone = itemFrame.midX > windowFrame.maxX - max(420, windowFrame.width * 0.42)
            let plausible = trimmed.count > 0 && trimmed.count < 180 && !excluding.contains(trimmed)
            if lyricZone && plausible {
                output.append(Candidate(element: element, text: trimmed, frame: itemFrame, emphasis: emphasis(of: element)))
            }
        }
        guard let children: [AXUIElement] = attribute(element, kAXChildrenAttribute as CFString) else { return }
        for child in children {
            collect(element: child, depth: depth + 1, visited: &visited, windowFrame: windowFrame, excluding: excluding, into: &output)
        }
    }

    private func emphasis(of element: AXUIElement) -> Double {
        let selected: Bool = attribute(element, kAXSelectedAttribute as CFString) ?? false
        var score = selected ? 10.0 : 0.0
        guard let text: String = attribute(element, kAXValueAttribute as CFString), !text.isEmpty else { return score }
        var range = CFRange(location: 0, length: min(1, text.utf16.count))
        guard let rangeValue = AXValueCreate(.cfRange, &range) else { return score }
        var raw: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(
            element, kAXAttributedStringForRangeParameterizedAttribute as CFString, rangeValue, &raw
        ) == .success, let attributed = raw as? NSAttributedString, attributed.length > 0 else { return score }
        let attributes = attributed.attributes(at: 0, effectiveRange: nil)
        if let font = attributes[.font] as? NSFont { score += Double(NSFontManager.shared.weight(of: font)) / 15.0 }
        if let color = (attributes[.foregroundColor] as? NSColor)?.usingColorSpace(.deviceRGB) {
            let luminance = 0.2126 * color.redComponent + 0.7152 * color.greenComponent + 0.0722 * color.blueComponent
            score += Double(abs(luminance - 0.5) * 2) * Double(color.alphaComponent)
        }
        return score
    }

    private func frame(of element: AXUIElement) -> CGRect? {
        guard let positionRef: CFTypeRef = rawAttribute(element, kAXPositionAttribute as CFString),
              let sizeRef: CFTypeRef = rawAttribute(element, kAXSizeAttribute as CFString),
              CFGetTypeID(positionRef) == AXValueGetTypeID(), CFGetTypeID(sizeRef) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero
        var size = CGSize.zero
        guard AXValueGetValue(positionRef as! AXValue, .cgPoint, &point),
              AXValueGetValue(sizeRef as! AXValue, .cgSize, &size) else { return nil }
        return CGRect(origin: point, size: size)
    }

    private func rawAttribute(_ element: AXUIElement, _ name: CFString) -> CFTypeRef? {
        var value: CFTypeRef?
        return AXUIElementCopyAttributeValue(element, name, &value) == .success ? value : nil
    }

    private func attribute<T>(_ element: AXUIElement, _ name: CFString) -> T? {
        rawAttribute(element, name) as? T
    }
}
