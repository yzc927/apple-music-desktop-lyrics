# macOS version (planned)

This directory is reserved for a future native macOS port.

## Intended implementation

- Swift and SwiftUI/AppKit
- Transparent always-on-top desktop overlay
- Menu-bar management entry
- Lock/unlock, click behavior, window placement persistence, lyric timing controls, and artist palettes matching Windows
- Public Apple Music/Music.app playback integration where available
- Shared LRCLIB request semantics and compatible local settings data

## Porting order

1. Music playback metadata and timeline adapter
2. LRCLIB client and LRC parser
3. Native overlay and menu-bar controller
4. Per-song offset persistence
5. Artist palette management and Windows behavior parity

No macOS executable or implementation is included yet.
