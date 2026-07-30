import XCTest
@testable import AppleMusicDesktopLyricsMac

final class CoreTests: XCTestCase {
    func testLRCParsesMultipleTimestampsAndInstrumentalRows() {
        let lines = LRCParser.parse("[00:01.50][00:03.00]你好\n[00:08.25]")
        XCTAssertEqual(lines.count, 3)
        XCTAssertEqual(lines[0], LyricLine(time: 1.5, text: "你好"))
        XCTAssertEqual(lines[1], LyricLine(time: 3.0, text: "你好"))
        XCTAssertEqual(lines[2], LyricLine(time: 8.25, text: "♪"))
    }

    func testLiyuuUsesBluePalette() {
        XCTAssertEqual(ArtistColorEngine.colors(for: "Liyuu"), ["#FF4D9CFF", "#FF63C7FF"])
    }

    func testCollaboratingArtistsCombineStableColors() {
        let colors = ArtistColorEngine.colors(for: "YOASOBI & Ado")
        XCTAssertEqual(colors.count, 2)
        XCTAssertEqual(colors.first, "#FF4D79FF")
        XCTAssertEqual(colors.last, "#FF5965FF")
    }

    func testArtistIdentityIgnoresWhitespaceButKeepsLanguageAliasesDistinct() {
        XCTAssertEqual(ArtistColorEngine.normalizedKey(for: " 藤井 風 "), ArtistColorEngine.normalizedKey(for: "藤井風"))
        XCTAssertEqual(ArtistColorEngine.colors(for: "藤井風"), ArtistColorEngine.colors(for: "藤井 風"))
        XCTAssertNotEqual(ArtistColorEngine.normalizedKey(for: "周杰伦"), ArtistColorEngine.normalizedKey(for: "周杰倫"))
        XCTAssertEqual(
            ArtistColorEngine.curatedPalettes.map { ArtistColorEngine.normalizedKey(for: $0.identity) }.count,
            Set(ArtistColorEngine.curatedPalettes.map { ArtistColorEngine.normalizedKey(for: $0.identity) }).count
        )
    }
}
