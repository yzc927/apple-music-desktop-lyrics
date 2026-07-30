import AppKit
import Foundation

final class AppleMusicPlayer {
    func requestAutomationPermission() {
        NSAppleScript(source: "tell application \"Music\" to get name")?.executeAndReturnError(nil)
    }

    func readTrack() async -> TrackInfo? {
        await Task.detached(priority: .utility) {
            let script = #"""
            tell application "Music"
                if not running then return ""
                if player state is stopped then return ""
                set unitSeparator to ASCII character 31
                set currentItem to current track
                set trackName to name of currentItem
                set trackArtist to artist of currentItem
                set trackAlbum to album of currentItem
                set trackDuration to duration of currentItem
                set trackPosition to player position
                if player state is playing then
                    set stateText to "1"
                else
                    set stateText to "0"
                end if
                return trackName & unitSeparator & trackArtist & unitSeparator & trackAlbum & unitSeparator & trackDuration & unitSeparator & trackPosition & unitSeparator & stateText
            end tell
            """#
            var error: NSDictionary?
            guard let output = NSAppleScript(source: script)?.executeAndReturnError(&error).stringValue,
                  error == nil, !output.isEmpty else { return nil }
            let fields = output.components(separatedBy: String(UnicodeScalar(31)!))
            guard fields.count >= 6 else { return nil }
            return TrackInfo(
                title: fields[0], artist: fields[1], album: fields[2],
                duration: Double(fields[3]) ?? 0, position: Double(fields[4]) ?? 0,
                isPlaying: fields[5] == "1"
            )
        }.value
    }
}
