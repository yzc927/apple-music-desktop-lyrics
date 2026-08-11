using AppleMusicDesktopLyrics;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

static void Near(double expected, double actual, string name)
{
    if (Math.Abs(expected - actual) > 0.001)
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

var ordinary = LrcParser.Parse("[00:01.50]你好\n[00:03.00]世界");
Equal(2, ordinary.Count, "ordinary line count");
Equal(false, ordinary[0].HasWordTiming, "ordinary fallback");

var enhancedText = "[00:12.00]<00:12.00>君<00:12.30>の<00:12.55>名<00:12.90>は<00:13.20>";
var enhanced = LrcParser.Parse(enhancedText);
Equal(1, enhanced.Count, "enhanced line count");
Equal("君の名は", enhanced[0].Text, "enhanced plain text");
Equal(4, enhanced[0].Segments?.Count ?? 0, "enhanced segment count");
Equal(TimeSpan.FromSeconds(13.2), enhanced[0].ExplicitEndTime, "enhanced explicit end");
Equal(enhancedText, LrcParser.Serialize(enhanced), "enhanced round trip");

Near(0.25, LyricTiming.EnhancedProgress(
    enhanced[0], TimeSpan.FromSeconds(12.3), TimeSpan.FromSeconds(13.2)), "enhanced first word complete");

var split = LyricWordSplitter.Split("Hello world 君の名は");
Equal("Hello ", split[0], "latin word keeps space");
Equal("world ", split[1], "second latin word keeps space");
Equal("君", split[2], "cjk splits by character");

var editable = new EditableLyricLine(enhanced[0]);
editable.Shift(TimeSpan.FromMilliseconds(500));
Equal(TimeSpan.FromSeconds(12.5), editable.Time, "line shift");
Equal(TimeSpan.FromSeconds(12.5), editable.Segments[0].Time, "segment shift");
Equal(TimeSpan.FromSeconds(13.7), editable.ExplicitEndTime, "end shift");

Console.WriteLine("Windows core tests passed.");
