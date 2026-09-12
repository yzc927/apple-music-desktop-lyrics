using AppleMusicDesktopLyrics;
using System.IO;

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

var correctIdolLyrics = LrcParser.Parse(
    "[00:40.00]誰もが目を奪われていく\n[00:44.00]君は完璧で究極のアイドル");
Equal(false, LyricsContentCompatibility.IsClearlyIncompatible(
    correctIdolLyrics, "誰もが目を奪われていく", "君は完璧で究極のアイドル"),
    "matching official lines keep LRCLIB timeline");
var corruptedIdolLyrics = LrcParser.Parse(
    "[00:43.12]負う不 楽しく 離脱\n[00:49.17]音字 乖離 政治");
Equal(true, LyricsContentCompatibility.IsClearlyIncompatible(
    corruptedIdolLyrics, "嘘か本当か知り得ない", "そんな言葉にまた踊る"),
    "unrelated official line pair rejects corrupted LRCLIB lyrics");

var preferences = new LyricsPreferences();
preferences.Rejected["song-a"] = new() { "wrong-version" };
Equal(true, preferences.IsRejected("song-a", "wrong-version"), "rejected version remembered");
Equal(false, preferences.IsRejected("song-b", "wrong-version"), "rejections isolated by song");
preferences.Readings["song-a"] = new() { ["明日"] = new("あした", true) };
var roundTrip = System.Text.Json.JsonSerializer.Deserialize<LyricsPreferences>(
    System.Text.Json.JsonSerializer.Serialize(preferences))!;
Equal("あした", roundTrip.GetReading("song-a", "明日")?.Reading, "reading persistence roundtrip");
Equal<ReadingPreference?>(null, roundTrip.GetReading("song-b", "明日"), "readings isolated by song");
var automaticRuby = new RubySegment[] { new("明日", "あす"), new("へ", "") };
var correctedRuby = PersonalRubyRules.Apply("明日へ", automaticRuby, preferences.Readings["song-a"], false);
Equal("あした", correctedRuby[0].ReadingText, "correct special sung reading");
Equal("明日へ", string.Concat(correctedRuby.Select(segment => segment.DisplayText)), "preserve lyric surface");
Equal(0, PersonalRubyRules.Apply("明日へ", automaticRuby, preferences.Readings["song-a"], true).Count, "hide familiar readings");
var rubyWithoutAnalyzer = PersonalRubyRules.Apply("明日", [], preferences.Readings["song-a"], false);
Equal("あした", rubyWithoutAnalyzer[0].ReadingText, "custom reading works without language components");
var longestRuby = PersonalRubyRules.Apply("明日へ", automaticRuby,
    new Dictionary<string, ReadingPreference> { ["明"] = new("めい", false), ["明日"] = new("あした", false) }, false);
Equal("明日", longestRuby[0].DisplayText, "longest override wins");
Exception? hotkeyFailure = null;
var longTimeline = Enumerable.Range(0, 30).Select(i => new LyricLine(TimeSpan.FromSeconds(i), "line " + i)).ToArray();
var changedEnding = longTimeline.ToArray();
changedEnding[29] = changedEnding[29] with { Text = "different ending" };
Equal(false, LyricsClient.TimelineFingerprint(longTimeline) == LyricsClient.TimelineFingerprint(changedEnding), "dedupe includes lyrics after line 24");
var timedVersion = longTimeline.ToArray();
timedVersion[0] = timedVersion[0] with { Segments = [new(TimeSpan.Zero, "line "), new(TimeSpan.FromMilliseconds(300), "0")] };
Equal(false, LyricsClient.TimelineFingerprint(longTimeline) == LyricsClient.TimelineFingerprint(timedVersion), "dedupe preserves word timing");
var endVersion = longTimeline.ToArray();
endVersion[0] = endVersion[0] with { ExplicitEndTime = TimeSpan.FromMilliseconds(800) };
Equal(false, LyricsClient.TimelineFingerprint(longTimeline) == LyricsClient.TimelineFingerprint(endVersion), "dedupe preserves explicit ending");
Equal(LyricsClient.TimelineFingerprint(longTimeline), LyricsClient.TimelineFingerprint(longTimeline.ToArray()), "identical timeline fingerprint");

var settingsTest = Path.Combine(Path.GetTempPath(), "lyrics-settings-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(settingsTest);
try
{
    var settingsPath = Path.Combine(settingsTest, "settings.json");
    var saved = new LyricsPreferences();
    saved.Readings["song"] = new() { ["word"] = new("reading", false) };
    saved.SaveTo(settingsPath);
    saved.OnlyUnfamiliar = true;
    saved.SaveTo(settingsPath);
    File.WriteAllText(settingsPath, "{broken");
    var recovered = SettingsPersistence.Load(settingsPath, () => new LyricsPreferences(), LyricsPreferences.IsValid);
    Equal("reading", recovered.GetReading("song", "word")?.Reading, "corrupt settings recover last good backup");
    Equal(false, recovered.OnlyUnfamiliar, "backup is previous committed version");
    File.WriteAllText(settingsPath, "{\"Hotkeys\":null,\"Readings\":{\"song\":null}}");
    recovered = SettingsPersistence.Load(settingsPath, () => new LyricsPreferences(), LyricsPreferences.IsValid);
    Equal("reading", recovered.GetReading("song", "word")?.Reading, "structurally invalid JSON recovers backup");
    recovered.SaveTo(settingsPath);
    Equal(true, Directory.GetFiles(settingsTest, "*.corrupt-*").Length > 0, "preserve damaged primary for recovery");
    using (var lockedFile = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        recovered.OnlyUnfamiliar = true;
        var failed = false;
        try { recovered.SaveTo(settingsPath); } catch (IOException) { failed = true; }
        Equal(true, failed, "locked file save reports failure");
        Equal(false, recovered.OnlyUnfamiliar, "failed save rolls memory back");
    }
    Equal(false, SettingsPersistence.Load(settingsPath, () => new LyricsPreferences(), LyricsPreferences.IsValid).OnlyUnfamiliar, "failed save keeps disk unchanged");
    Equal(0, Directory.GetFiles(settingsTest, "*.tmp").Length, "failed writes clean their own temporary files");
    File.WriteAllText(settingsPath, "null"); File.WriteAllText(settingsPath + ".bak", "null");
    Equal(true, SettingsPersistence.Load(settingsPath, () => new LyricsPreferences(), LyricsPreferences.IsValid).Hotkeys.Count > 0, "both files invalid use safe defaults");
}
finally { Directory.Delete(settingsTest, recursive: true); }
if (!args.Contains("--skip-native-hotkeys"))
{
var shortcutThread = new Thread(() =>
{
    var previousKeys = LyricsPreferences.Current.Hotkeys;
    try
    {
        LyricsPreferences.Current.Hotkeys = new() { ["test"] = "Ctrl+Alt+Shift+F24" };
        using var first = new GlobalLyricsHotkeys(new() { ["test"] = () => { } });
        Equal(true, first.Status.Contains("已启用 1 个"), "native hotkey registration");
        using var second = new GlobalLyricsHotkeys(new() { ["test"] = () => { } });
        Equal(true, second.Status.Contains("占用"), "native occupied hotkey notice");
        first.Dispose(); second.Apply();
        Equal(true, second.Status.Contains("已启用 1 个"), "hotkey released on dispose");
        var persisted = false;
        Equal(false, second.TryApply(new Dictionary<string, string> { ["test"] = "nonsense" }, () => persisted = true), "invalid proposal rejected");
        Equal(false, persisted, "invalid proposal not saved");
        Equal(false, second.TryApply(new Dictionary<string, string> { ["test"] = "Ctrl+Alt+Shift+F23" }, () => throw new IOException("disk failure")), "save failure rejects proposal");
        using (var oldKeyProbe = new GlobalLyricsHotkeys(new() { ["test"] = () => { } }))
            Equal(true, oldKeyProbe.Status.Contains("占用"), "old registration retained after failed save");
        LyricsPreferences.Current.Hotkeys = new() { ["test"] = "Ctrl+Alt+Shift+F23" };
        using (var blocker = new GlobalLyricsHotkeys(new() { ["test"] = () => { } }))
        {
            Equal(true, blocker.Status.Contains("已启用 1 个"), "new registration released after failed save");
            Equal(false, second.TryApply(LyricsPreferences.Current.Hotkeys, () => persisted = true), "occupied proposal rejected");
            Equal(false, persisted, "occupied proposal never saved");
        }
        LyricsPreferences.Current.Hotkeys = new() { ["test"] = "Ctrl+Alt+Shift+F24" };
        Equal(true, second.TryApply(LyricsPreferences.Current.Hotkeys), "unchanged key reuses own registration");
        LyricsPreferences.Current.Hotkeys["duplicate"] = "Ctrl+Alt+Shift+F24";
        second.Dispose();
        using var duplicated = new GlobalLyricsHotkeys(new() { ["test"] = () => { }, ["duplicate"] = () => { } });
        Equal(true, duplicated.Status.Contains("重复"), "internal hotkey conflict");
        LyricsPreferences.Current.Hotkeys["test"] = "nonsense";
        LyricsPreferences.Current.Hotkeys["duplicate"] = "";
        duplicated.Apply();
        Equal(true, duplicated.Status.Contains("格式无效"), "invalid shortcut notice");
    }
    catch (Exception ex) { hotkeyFailure = ex; }
    finally { LyricsPreferences.Current.Hotkeys = previousKeys; }
});
shortcutThread.SetApartmentState(ApartmentState.STA);
shortcutThread.Start(); shortcutThread.Join();
if (hotkeyFailure is not null) throw hotkeyFailure;
}
LyricsRecoveryTests.Run();
RubyOverlapTests.Run();
Console.WriteLine("Windows core tests passed (native hotkeys " + (args.Contains("--skip-native-hotkeys") ? "skipped" : "tested") + ").");

// A blocked native provider must not hold up callers or spawn more workers.
var bounded = new BoundedAsyncReader<string>();
using var releaseRead = new ManualResetEventSlim();
var blockedRead = bounded.ReadAsync(() => { releaseRead.Wait(); return "old song"; },
    TimeSpan.FromMilliseconds(100), CancellationToken.None);
Equal<string?>(null, await blockedRead, "hung read times out");
Equal(true, bounded.Disabled, "hung provider disabled for session");
Equal<string?>(null, await bounded.ReadAsync(() => throw new Exception("must not run"),
    TimeSpan.FromSeconds(1), CancellationToken.None), "no second worker after timeout");
releaseRead.Set();
var healthyReader = new BoundedAsyncReader<string>();
Equal("new song", await healthyReader.ReadAsync(() => "new song", TimeSpan.FromSeconds(1),
    CancellationToken.None), "healthy read completes");
using var cancelRead = new CancellationTokenSource();
cancelRead.Cancel();
try {
    await healthyReader.ReadAsync(() => "stale", TimeSpan.FromSeconds(1), cancelRead.Token);
    throw new Exception("cancelled song must not be read");
} catch (OperationCanceledException) { }
Console.WriteLine("Bounded reader regression tests passed.");

Equal(12, OverlayLayout.ResizeHit(450, 2, 920, 150), "top resize");
Equal(15, OverlayLayout.ResizeHit(450, 149, 920, 150), "bottom resize");
Equal(17, OverlayLayout.ResizeHit(919, 149, 920, 150), "corner resize");
Equal(0, OverlayLayout.ResizeHit(450, 75, 920, 150), "interior is not resize");
foreach (var size in new[] { (360d, 100d), (1833d, 150d), (1486d, 390d) })
{
    var scale = OverlayLayout.Scale(size.Item1, size.Item2, 1200, 1800, 105);
    Equal(true, scale * 1800 <= size.Item1 - 64 + 0.001, "long next lyric fits horizontally");
    Equal(true, scale * 105 <= size.Item2 - 54 + 0.001, "ruby and both lines fit vertically");
}
Console.WriteLine("Overlay resize and typography tests passed.");

var libraryArtists = System.Text.Json.JsonSerializer.Deserialize<string[]>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LibraryArtists.json")))!;
const string missingPalette = "#01020304";
foreach (var artist in libraryArtists)
    Equal(false, ArtistColorEngine.Resolve(artist, missingPalette).Colors.Contains(missingPalette),
        $"library artist has a fixed palette: {artist}");
var ensemble = ArtistColorEngine.Resolve("夜々(CV:原田ひとみ)、いろり(CV:茅野愛衣)、小紫(CV:小倉唯)");
Equal(3, ensemble.Colors.Count, "all separate CV performers are retained");
Equal(ArtistColorEngine.Resolve("茅野愛衣").Colors[0], ensemble.Colors[1], "second CV palette");
var lightMusic = ArtistColorEngine.Resolve("桜高軽音部(CV:豊崎愛生、日笠陽子、佐藤聡美、寿美菜子)");
Equal(4, lightMusic.Colors.Count, "four performers inside one CV credit");
Equal(ArtistColorEngine.Resolve("寿美菜子").Colors[0], lightMusic.Colors[3], "fourth CV palette");
Equal(ArtistColorEngine.Resolve("小倉唯").Colors[0], ArtistColorEngine.Resolve("小倉 唯").Colors[0], "whitespace alias");
Equal(string.Join(",", ArtistColorEngine.Resolve("Taylor Swift").Colors),
    string.Join(",", ArtistColorEngine.Resolve("テイラー・スウィフト").Colors), "localized metadata alias");
foreach (var custom in CustomArtistPaletteStore.Current.GetAll())
    Equal(string.Join(",", custom.Colors), string.Join(",", ArtistColorEngine.Resolve(custom.Identity).Colors),
        "user palette has priority");
Console.WriteLine($"Artist palette coverage passed for {libraryArtists.Length} library/observed credits.");
