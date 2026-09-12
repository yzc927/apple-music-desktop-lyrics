namespace AppleMusicDesktopLyrics;

internal static class RubyOverlapTests
{
    internal static void Run()
    {
        var automatic = new RubySegment[] { new("明日", "あす"), new("へ", ""), new("行く", "いく") };
        foreach (var word in new[] { "明", "日", "日へ" })
        foreach (var familiar in new[] { false, true })
        foreach (var only in new[] { false, true })
        {
            var result = PersonalRubyRules.Apply("明日へ行く", automatic,
                new Dictionary<string, ReadingPreference> { [word] = new("", familiar) }, only);
            if (!result.SequenceEqual(automatic)) throw new Exception("Partial empty rule lost automatic ruby: " + word);
        }
        var whole = PersonalRubyRules.Apply("明日へ行く", automatic,
            new Dictionary<string, ReadingPreference> { ["明日へ"] = new("", false) }, false);
        if (whole[0].ReadingText != "あすへ") throw new Exception("Aligned phrase fallback failed");
        var hidden = PersonalRubyRules.Apply("明日へ行く", automatic,
            new Dictionary<string, ReadingPreference> { ["明日"] = new("", true) }, true);
        if (hidden[0].ReadingText != "" || hidden[^1].ReadingText != "いく") throw new Exception("Whole familiar rule failed");
        var explicitPartial = PersonalRubyRules.Apply("明日へ行く", automatic,
            new Dictionary<string, ReadingPreference> { ["日"] = new("ひ", false) }, false);
        if (string.Concat(explicitPartial.Select(s => s.DisplayText)) != "明日へ行く" ||
            !explicitPartial.Any(s => s.DisplayText == "日" && s.ReadingText == "ひ"))
            throw new Exception("Explicit partial correction failed");
        Console.WriteLine("Ruby overlap tests passed: 15 cases");
    }
}
