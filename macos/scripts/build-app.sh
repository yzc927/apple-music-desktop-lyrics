#!/bin/zsh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
swift build -c release

APP="$ROOT/dist/Apple Music Desktop Lyrics.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/.build/release/AppleMusicDesktopLyricsMac" "$APP/Contents/MacOS/"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/AppleMusicDesktopLyricsMac"

if [[ -n "${DEVELOPMENT_TEAM:-}" ]]; then
  codesign --force --deep --sign - "$APP"
fi

echo "Created: $APP"
