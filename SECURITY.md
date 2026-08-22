# Security and privacy

Apple Music Desktop Lyrics is a local desktop companion. It does not include telemetry, advertising, crash
reporting, or analytics SDKs. It never reads Apple ID credentials, cookies, or tokens.

The Windows app sends a cleaned track title and artist name to `https://lrclib.net/` to search for synchronized
lyrics. Album and duration are used locally to rank results. Settings, song choices, offsets, local lyrics,
cached lyrics, and custom artist colors remain under `%LOCALAPPDATA%\AppleMusicDesktopLyrics` as ordinary JSON.

Official portable builds are self-contained Windows x64 executables for Windows 10 version 2004 (19041) or
later and Windows 11. They are currently unsigned, so Windows SmartScreen may show an unknown publisher warning.
Every release includes a SHA-256 checksum for verification.

Please report suspected vulnerabilities through GitHub's private security advisory feature rather than a public
issue. Do not include Apple account details or private lyrics files in a report.
