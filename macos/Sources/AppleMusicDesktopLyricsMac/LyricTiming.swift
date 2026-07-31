import Foundation

enum LyricTiming {
    static func secondsPerUnit(_ lines: [LyricLine]) -> Double {
        var samples: [Double] = []
        for index in lines.indices where index + 1 < lines.count {
            guard !isInstrumental(lines[index].text) else { continue }
            let seconds = lines[index + 1].time - lines[index].time
            let units = vocalUnits(lines[index].text)
            guard seconds >= 0.55, seconds <= 8, units >= 1 else { continue }
            samples.append(min(0.75, max(0.07, seconds / units)))
        }
        guard !samples.isEmpty else { return 0.28 }
        samples.sort()
        let middle = samples.count / 2
        return samples.count.isMultiple(of: 2)
            ? (samples[middle - 1] + samples[middle]) / 2
            : samples[middle]
    }

    static func progress(
        lines: [LyricLine], index: Int, position: Double, secondsPerUnit: Double
    ) -> Double {
        guard index >= 0, index < lines.count, !isInstrumental(lines[index].text) else { return 0 }
        guard index + 1 < lines.count else { return 1 }
        let natural = lines[index + 1].time - lines[index].time
        guard natural > 0 else { return 0 }
        // Keep normal rows tied to their timestamps. Fast rows receive only a
        // small bounded visual lead so the final sweep frame is not lost when
        // the next timestamp arrives.
        let completionLead = natural < 2.5
            ? min(0.09, max(0.035, natural * 0.08))
            : 0
        return min(1, max(0,
            (position - lines[index].time) / max(0.1, natural - completionLead)
        ))
    }

    static func isInstrumental(_ text: String) -> Bool {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty || trimmed == "♪" || trimmed == "•••" ||
            trimmed.allSatisfy { $0 == "•" || $0 == "." || $0 == "·" }
    }

    static func displayText(_ text: String) -> String { isInstrumental(text) ? "•••" : text }

    static func similarity(_ lhs: String, _ rhs: String) -> Double {
        let first = normalized(lhs), second = normalized(rhs)
        guard !first.isEmpty, !second.isEmpty else { return 0 }
        if first == second { return 1 }
        if (first.contains(second) || second.contains(first)) &&
            Double(min(first.count, second.count)) >= Double(max(first.count, second.count)) * 0.7 { return 0.9 }
        let left = bigrams(first), right = bigrams(second)
        guard !left.isEmpty, !right.isEmpty else { return 0 }
        return 2 * Double(left.intersection(right).count) / Double(left.count + right.count)
    }

    static func calibrationLine(
        lines: [LyricLine], current: String, next: String, expectedPosition: Double
    ) -> Int? {
        var best: (index: Int, score: Double)?
        for index in lines.indices where !isInstrumental(lines[index].text) {
            let currentSimilarity = similarity(lines[index].text, current)
            guard currentSimilarity >= 0.72 else { continue }
            let nextSimilarity = index + 1 < lines.count && !isInstrumental(next)
                ? similarity(lines[index + 1].text, next) : 0
            let distance = abs(lines[index].time - expectedPosition)
            guard distance <= 15 else { continue }
            let score = currentSimilarity * 6 + nextSimilarity * 3 - distance * 0.12
            if best == nil || score > best!.score { best = (index, score) }
        }
        return best?.index
    }

    private static func vocalUnits(_ text: String) -> Double {
        var units = 0.0, inLatinWord = false
        for scalar in text.unicodeScalars {
            if CharacterSet.whitespacesAndNewlines.contains(scalar) ||
                CharacterSet.punctuationCharacters.contains(scalar) ||
                CharacterSet.symbols.contains(scalar) {
                inLatinWord = false
            } else if scalar.value <= 0x024f && CharacterSet.alphanumerics.contains(scalar) {
                if !inLatinWord { units += 1.6 }
                inLatinWord = true
            } else {
                units += 1
                inLatinWord = false
            }
        }
        return units
    }

    private static func normalized(_ text: String) -> String {
        text.folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) }.map { String($0) }.joined()
    }

    private static func bigrams(_ value: String) -> Set<String> {
        let characters = Array(value)
        if characters.count == 1 { return [String(characters[0])] }
        guard characters.count > 1 else { return [] }
        return Set((0..<(characters.count - 1)).map { String(characters[$0...($0 + 1)]) })
    }
}
