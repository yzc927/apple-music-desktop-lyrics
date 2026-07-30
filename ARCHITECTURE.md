# Architecture

## Data flow

1. A platform playback adapter reads the current Apple Music track and timeline.
2. `LyricsClient` searches LRCLIB using title, artist, album, and duration.
3. `LrcParser` converts line timestamps into a platform-neutral lyric timeline.
4. The controller applies per-track timing corrections and guards against small timeline regressions.
5. The native overlay renders current/next lines, progress fill, notifications, and artist palettes.

## Platform boundaries

The behavior is intentionally split into reusable concepts and native integration layers:

- Reusable: lyric matching, LRC parsing, song identity, timing offsets, and artist palette rules.
- Windows: WPF overlay, tray icon, Win32 window styles, and Windows Global System Media Transport Controls.
- macOS (planned): SwiftUI/AppKit overlay, menu-bar integration, and a public Music playback adapter.

The macOS version should reproduce behavior and data formats rather than attempt to run the WPF UI.

## Local state

Windows currently stores settings under `%LocalAppData%\AppleMusicDesktopLyrics`:

- `settings.json`: overlay position, size, and color mode.
- `song-offsets.json`: per-recording lyric timing corrections.

These files are not part of the repository and are excluded from source control.
