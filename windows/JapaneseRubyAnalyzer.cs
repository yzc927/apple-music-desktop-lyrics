using System.Text;
using Windows.Globalization;

namespace AppleMusicDesktopLyrics;

internal sealed record RubySegment(string DisplayText, string ReadingText);

internal static class JapaneseRubyAnalyzer
{
    public static IReadOnlyList<RubySegment> Analyze(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > 100 || !ContainsKanji(text) || !ContainsKana(text))
            return [];
        try
        {
            var result = new List<RubySegment>();
            foreach (var phoneme in JapanesePhoneticAnalyzer.GetWords(text, true))
                AddSegment(result, phoneme.DisplayText, ToHiragana(phoneme.YomiText));
            return result.Any(item => item.ReadingText.Length > 0) ? result : [];
        }
        catch
        {
            // Missing Japanese language components must never break lyric display.
            return [];
        }
    }

    private static void AddSegment(List<RubySegment> result, string display, string reading)
    {
        if (display.Length == 0) return;
        if (!ContainsKanji(display) || reading.Length == 0)
        {
            result.Add(new RubySegment(display, ""));
            return;
        }
        var prefix = 0;
        while (prefix < display.Length && prefix < reading.Length &&
               display[prefix] == reading[prefix] && IsKana(display[prefix]))
            prefix++;
        var suffix = 0;
        while (suffix < display.Length - prefix && suffix < reading.Length - prefix &&
               display[^(suffix + 1)] == reading[^(suffix + 1)] &&
               IsKana(display[^(suffix + 1)]))
            suffix++;
        if (prefix > 0)
            result.Add(new RubySegment(display[..prefix], ""));
        var displayEnd = suffix == 0 ? display.Length : display.Length - suffix;
        var readingEnd = suffix == 0 ? reading.Length : reading.Length - suffix;
        if (displayEnd > prefix)
            result.Add(new RubySegment(display[prefix..displayEnd], reading[prefix..readingEnd]));
        if (suffix > 0)
            result.Add(new RubySegment(display[displayEnd..], ""));
    }

    private static bool ContainsKanji(string value) => value.Any(character =>
        character is >= '\u4e00' and <= '\u9fff' or '々' or '〆' or 'ヵ' or 'ヶ');

    private static bool ContainsKana(string value) => value.Any(IsKana);

    private static bool IsKana(char value) =>
        value is >= '\u3041' and <= '\u3096' or >= '\u30a1' and <= '\u30fa' or 'ー';

    private static string ToHiragana(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
            result.Append(character is >= '\u30a1' and <= '\u30f6'
                ? (char)(character - 0x60)
                : character);
        return result.ToString();
    }
}
