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

    func testSakuraMikoUsesReferenceHairColor() {
        XCTAssertEqual(ArtistColorEngine.colors(for: "さくらみこ"), ["#FFF25F7C"])
        XCTAssertEqual(ArtistColorEngine.colors(for: "miComet").first, "#FFF25F7C")
    }

    func testCollaboratingArtistsCombineStableColors() {
        let colors = ArtistColorEngine.colors(for: "YOASOBI & Ado")
        XCTAssertEqual(colors.count, 2)
        XCTAssertEqual(colors.first, "#FF4D79FF")
        XCTAssertEqual(colors.last, "#FF5965FF")
    }

    func testLineProgressUsesStableTimestampIntervalWithoutGuessing() {
        let lines = [LyricLine(time: 0, text: "短句"), LyricLine(time: 10, text: "下一句")]
        let progress = LyricTiming.progress(
            lines: lines, index: 0, position: 2, secondsPerUnit: LyricTiming.secondsPerUnit(lines)
        )
        XCTAssertEqual(progress, 0.2, accuracy: 0.001)
    }

    func testSmartProgressKeepsSlowShortLyricAcrossNormalLineDuration() {
        let lines = [LyricLine(time: 0, text: "长音"), LyricLine(time: 4, text: "下一句")]
        XCTAssertEqual(
            LyricTiming.progress(lines: lines, index: 0, position: 2, secondsPerUnit: 0.28),
            0.5, accuracy: 0.001
        )
    }

    func testInstrumentalRowsNeverReceiveHighlightProgress() {
        let lines = [LyricLine(time: 0, text: "♪"), LyricLine(time: 5, text: "下一句")]
        XCTAssertEqual(
            LyricTiming.progress(lines: lines, index: 0, position: 4, secondsPerUnit: 0.28), 0
        )
        XCTAssertEqual(LyricTiming.displayText("♪"), "•••")
    }

    func testCalibrationUsesCurrentAndNextLineToDisambiguateRepeatedLyrics() {
        let lines = [
            LyricLine(time: 5, text: "相同一句"), LyricLine(time: 8, text: "第一段"),
            LyricLine(time: 35, text: "相同一句"), LyricLine(time: 38, text: "第二段")
        ]
        XCTAssertEqual(
            LyricTiming.calibrationLine(
                lines: lines, current: "相同一句！", next: "第二段", expectedPosition: 34.5
            ), 2
        )
    }

    func testCollaborationSeparatorsDoNotRequireSpaces() {
        XCTAssertEqual(
            ArtistColorEngine.colors(for: "宝鐘マリン&Gawr Gura"),
            ["#FFFF4B5E", "#FF4CA4D9"]
        )
        XCTAssertEqual(
            ArtistColorEngine.colors(for: "GuraMarine"),
            ["#FFFF4B5E", "#FF4CA4D9"]
        )
    }

    func testCharacterCreditUsesVoiceActorPalette() {
        XCTAssertEqual(
            ArtistColorEngine.colors(for: "高木さん(CV.高橋李依)"),
            ["#FFE86F9A"]
        )
    }

    func testUnknownArtistUsesSelectedFallbackWithoutDisablingAutoMode() {
        XCTAssertEqual(
            ArtistColorEngine.colors(for: "An Unlisted Artist", unknownArtistColor: "#FFFF6FAE"),
            ["#FFFF6FAE"]
        )
        XCTAssertEqual(
            ArtistColorEngine.colors(for: "Gawr Gura & An Unlisted Artist", unknownArtistColor: "#FFFF6FAE"),
            ["#FF4CA4D9", "#FFFF6FAE"]
        )
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
