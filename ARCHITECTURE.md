# Architecture

## Data flow

1. A platform playback adapter reads the current Apple Music track and timeline.
2. `LyricsClient` searches LRCLIB using title, artist, album, and duration, rejects weak
   recording matches, and keeps up to eight deduplicated synchronized candidates.
3. If LRCLIB has no synchronized lyrics, `AppleMusicUiLyricsProvider` reads Apple Music's
   public UI Automation lyric elements as a fallback. It uses `CurrentLine` when available
   and the virtualized visible `Line` sequence used by newer Apple Music builds otherwise.
4. When explicitly enabled, matching Apple line transitions can provide a read-only calibration
   signal without replacing LRCLIB; the default stable path does not mix the two clocks.
5. The controller combines a monotonic playback clock, per-track manual correction, bounded
   automatic calibration, seek detection, and language-aware active-line duration estimation.
6. The native overlay renders current/next lines, progress fill, fonts, notifications, and artist palettes.

## Platform boundaries

The behavior is intentionally split into reusable concepts and native integration layers:

- Reusable: lyric matching, LRC parsing, song identity, timing offsets, and artist palette rules.
- Windows: WPF overlay, tray icon, Win32 window styles, UI Automation lyrics, and Windows Global System Media Transport Controls.
- macOS: SwiftUI/AppKit overlay, menu-bar integration, Music AppleScript playback adapter,
  AXUIElement official-lyric fallback, and the shared LRCLIB-first policy.

The macOS version reproduces behavior with native platform components rather than attempting to run the WPF UI.

## Local state

Windows currently stores settings under `%LocalAppData%\AppleMusicDesktopLyrics`:

- `settings.json`: overlay position, size, color mode, and selected font.
- `song-offsets.json`: per-recording lyric timing corrections.
- `lyrics-choices.json`: the user's selected LRCLIB candidate for each recording.

These files are not part of the repository and are excluded from source control.

macOS uses `UserDefaults` for overlay settings, per-song offsets, lyric candidates, font and color choices. The native panel frame is
stored with `NSStringFromRect`; invalid off-screen frames are ignored when the connected display layout changes.
