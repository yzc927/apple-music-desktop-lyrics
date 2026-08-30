namespace AppleMusicDesktopLyrics;

internal enum LyricsMatchConfidence { High, Medium, Low }

internal sealed record LyricsMatchAssessment(LyricsMatchConfidence Confidence, string Reason)
{
    public string Label => Confidence switch
    {
        LyricsMatchConfidence.High => "高度匹配",
        LyricsMatchConfidence.Medium => "可能是其他版本",
        _ => "低置信度"
    };
}

internal static class LyricsMatchConfidenceEvaluator
{
    public static LyricsMatchAssessment Evaluate(double titleMatch, double artistMatch,
        double durationDifference, double variantPenalty, bool durationKnown)
    {
        if (durationKnown && durationDifference > 6)
            return new(LyricsMatchConfidence.Low,
                $"时长不符（相差 {durationDifference:0.0} 秒）");
        if (titleMatch < 0.82)
            return new(LyricsMatchConfidence.Low, "歌名相似度偏低");
        if (artistMatch < 0.55)
            return new(LyricsMatchConfidence.Low, "歌手信息可能不一致");
        if (variantPenalty > 0)
            return new(LyricsMatchConfidence.Medium, "Live、伴奏或动画版等版本标签不同");
        if (durationKnown && durationDifference <= 2.5 && titleMatch >= 0.92 && artistMatch >= 0.75)
            return new(LyricsMatchConfidence.High, "歌名、歌手和时长均吻合");
        return new(LyricsMatchConfidence.Medium, durationKnown
            ? $"基础信息接近，时长相差 {durationDifference:0.0} 秒"
            : "候选歌词没有提供可靠时长");
    }
}
