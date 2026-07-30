// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "AppleMusicDesktopLyricsMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "AppleMusicDesktopLyricsMac", targets: ["AppleMusicDesktopLyricsMac"])
    ],
    targets: [
        .executableTarget(
            name: "AppleMusicDesktopLyricsMac",
            path: "Sources/AppleMusicDesktopLyricsMac"
        ),
        .testTarget(
            name: "AppleMusicDesktopLyricsMacTests",
            dependencies: ["AppleMusicDesktopLyricsMac"],
            path: "Tests/AppleMusicDesktopLyricsMacTests"
        )
    ]
)
