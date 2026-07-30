"""Generate Windows and macOS icon packages from the shared transparent master."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MASTER = ROOT / "assets" / "app-icon.png"
WINDOWS_ASSETS = ROOT / "windows" / "assets"
MACOS_RESOURCES = ROOT / "macos" / "Resources"


def resized(image: Image.Image, size: int) -> Image.Image:
    return image.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    image = Image.open(MASTER).convert("RGBA")
    image = resized(image, 1024)

    WINDOWS_ASSETS.mkdir(parents=True, exist_ok=True)
    MACOS_RESOURCES.mkdir(parents=True, exist_ok=True)

    # Keep a Windows-local preview for the README and Visual Studio asset browser.
    image.save(WINDOWS_ASSETS / "app-icon.png", optimize=True)
    image.save(
        WINDOWS_ASSETS / "app.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    iconset = MACOS_RESOURCES / "AppIcon.iconset"
    iconset.mkdir(parents=True, exist_ok=True)
    mac_sizes = {
        "icon_16x16.png": 16,
        "icon_16x16@2x.png": 32,
        "icon_32x32.png": 32,
        "icon_32x32@2x.png": 64,
        "icon_128x128.png": 128,
        "icon_128x128@2x.png": 256,
        "icon_256x256.png": 256,
        "icon_256x256@2x.png": 512,
        "icon_512x512.png": 512,
        "icon_512x512@2x.png": 1024,
    }
    for filename, size in mac_sizes.items():
        resized(image, size).save(iconset / filename, optimize=True)

    # Pillow writes a multi-resolution ICNS from the 1024 px RGBA master.
    image.save(MACOS_RESOURCES / "AppIcon.icns", format="ICNS")

    print("Generated Windows ICO and macOS ICNS/iconset from", MASTER)


if __name__ == "__main__":
    main()
