# Architecture

## Data flow

1. A platform playback adapter reads the current Apple Music track and timeline.
2. On Windows, `AppleMusicUiLyricsProvider` first reads Apple Music's public UI Automation
   lyric elements. It uses `CurrentLine` when available and the virtualized visible `Line`
   sequence used by newer Apple Music builds otherwise.
3. If those elements are unavailable, `LyricsClient` searches LRCLIB using title, artist,
   album, and duration, and `LrcParser` builds the fallback lyric timeline.
4. The controller applies per-track timing corrections and guards against small timeline regressions.
5. The native overlay renders current/next lines, progress fill, fonts, notifications, and artist palettes.

## Platform boundaries

The behavior is intentionally split into reusable concepts and native integration layers:

- Reusable: lyric matching, LRC parsing, song identity, timing offsets, and artist palette rules.
- Windows: WPF overlay, tray icon, Win32 window styles, UI Automation lyrics, and Windows Global System Media Transport Controls.
- macOS (planned): SwiftUI/AppKit overlay, menu-bar integration, and a public Music playback adapter.

The macOS version should reproduce behavior and data formats rather than attempt to run the WPF UI.

## Local state

Windows currently stores settings under `%LocalAppData%\AppleMusicDesktopLyrics`:

- `settings.json`: overlay position, size, color mode, and selected font.
- `song-offsets.json`: per-recording lyric timing corrections.

These files are not part of the repository and are excluded from source control.
