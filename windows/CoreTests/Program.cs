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
Equal(-1, LyricTiming.ActiveLineIndex(ordinary, TimeSpan.Zero), "before first lyric has no active line");
Equal(0, LyricTiming.ActiveLineIndex(ordinary, TimeSpan.FromSeconds(1.5)), "first lyric activates on timestamp");
Equal(1, LyricTiming.ActiveLineIndex(ordinary, TimeSpan.FromSeconds(4)), "last lyric remains active");

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

Equal(false, StartupSettingsMigration.Resolve(null, false), "new install startup default");
Equal(true, StartupSettingsMigration.Resolve(null, true), "legacy startup is preserved");
Equal(false, StartupSettingsMigration.Resolve(false, true), "explicit startup setting wins");
Equal(false, ReleaseDefaults.AutomaticLyricsCalibration,
    "new install automatic Apple calibration is opt-in");
Equal(true, ApplicationLaunchPolicy.ShouldShowManagement(true, []),
    "first run opens management even without Apple Music");
Equal(true, ApplicationLaunchPolicy.ShouldShowManagement(false, ["--MANAGEMENT"]),
    "management command is case insensitive");
Equal(false, ApplicationLaunchPolicy.ShouldShowManagement(false, ["--background"]),
    "ordinary background startup stays in the tray");
Equal(0, ReleaseSelfTest.Run(), "release self test");

var singleInstanceId = "AppleMusicDesktopLyrics.CoreTests." + Guid.NewGuid().ToString("N");
using (var activationReceived = new ManualResetEventSlim())
using (var primary = new SingleInstanceCoordinator(singleInstanceId, activationReceived.Set))
{
    Equal(true, primary.IsPrimary, "first app instance is primary");
    using var secondary = new SingleInstanceCoordinator(singleInstanceId, activationReceived.Set);
    Equal(false, secondary.IsPrimary, "second app instance is rejected");
    Equal(true, activationReceived.Wait(TimeSpan.FromSeconds(2)),
        "second app instance activates the first");
}

Equal(false, AppleUiPollingPolicy.ShouldRead(false, false, 20),
    "ordinary LRCLIB playback does not touch Apple UI Automation");
Equal(true, AppleUiPollingPolicy.ShouldRead(true, false, 0),
    "official Apple fallback may read UI Automation");
Equal(true, AppleUiPollingPolicy.ShouldRead(false, true, 20),
    "explicit automatic calibration may read UI Automation");
Equal(TimeSpan.FromMilliseconds(1500),
    AppleUiPollingPolicy.NextDelay(true, 0, TimeSpan.FromMilliseconds(100)),
    "successful UI Automation read is rate limited");
Equal(TimeSpan.FromSeconds(5),
    AppleUiPollingPolicy.NextDelay(false, 1, TimeSpan.FromMilliseconds(100)),
    "first failed UI Automation read backs off");
Equal(TimeSpan.FromSeconds(60),
    AppleUiPollingPolicy.NextDelay(false, 20, TimeSpan.FromMilliseconds(100)),
    "failed UI Automation read backoff is capped");

Equal("River Flows In You", SongMetadataNormalizer.CleanTitle(
    "River Flows In You (Remastered 2024)"), "remaster suffix is removed");
Equal("unravel", SongMetadataNormalizer.CleanTitle(
    "unravel【动画版 TV Size】"), "anime version suffix is removed");
Equal("打上花火", SongMetadataNormalizer.CleanTitle(
    "打上花火 feat. 米津玄師"), "featured artist suffix is removed");
Equal(true, SongMetadataNormalizer.VariantMismatchPenalty(
    "Song (Live)", "Song") > 0, "live studio mismatch is penalized");
Equal(0d, SongMetadataNormalizer.VariantMismatchPenalty(
    "Song (Live)", "Song - Live"), "matching live variants are not penalized");
Equal(LyricsMatchConfidence.High, LyricsMatchConfidenceEvaluator.Evaluate(
    1, 1, 0.4, 0, true).Confidence, "exact candidate has high confidence");
Equal(LyricsMatchConfidence.Medium, LyricsMatchConfidenceEvaluator.Evaluate(
    1, 1, 0.4, 34, true).Confidence, "version mismatch needs review");
Equal("时长不符（相差 8.0 秒）", LyricsMatchConfidenceEvaluator.Evaluate(
    1, 1, 8, 0, true).Reason, "duration mismatch is explained");

Console.WriteLine("Windows core tests passed.");
