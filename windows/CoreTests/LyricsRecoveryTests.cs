using AppleMusicDesktopLyrics;
using System.IO;
using System.Text.Json;

internal static class LyricsRecoveryTests
{
    private static int _checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
    }

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lyrics-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "local-lyrics.json");
            var store = new LocalLyricsStore(directory);
            store.SetOverride("song", "[00:01.00]old");
            store.SetOverride("song", "[00:01.00]new");
            Check(File.Exists(file + ".bak"), "local edits create last-good backup");
            File.WriteAllText(file, "{broken");
            store = new LocalLyricsStore(directory);
            Check(store.GetOverride("song")?.Lrc.Contains("old") == true, "recover local lyrics from backup");
            store.SetOverride("another", "[00:01.00]another");
            Check(new LocalLyricsStore(directory).HasOverride("song"), "saving another song preserves recovered lyrics");
            Check(Directory.GetFiles(directory, "*.corrupt-*").Any(path => File.ReadAllText(path) == "{broken"), "damaged original retained byte-for-byte");

            var before = File.ReadAllText(file);
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!PersistenceOperation.Try(() => store.SetOverride("song", "[00:01.00]unsaved"), out var error), "locked local edit fails gracefully");
                Check(error.Contains("未能保存"), "persistence error is actionable");
                Check(store.GetOverride("song")?.Lrc.Contains("old") == true, "failed edit retains committed memory");
                Check(!PersistenceOperation.Try(() => store.RemoveOverride("song"), out _), "locked local delete fails gracefully");
                Check(store.HasOverride("song"), "failed delete retains memory");
            }
            Check(File.ReadAllText(file) == before, "failed edits retain committed disk bytes");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed save removes temporary files");

            // An unreadable startup must not cause a later successful save to
            // overwrite existing songs with the startup's temporary empty view.
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                store = new LocalLyricsStore(directory);
            store.SetOverride("after-unlock", "[00:01.00]safe");
            Check(store.HasOverride("another"), "refresh persisted state before a mutation after read failure");

            File.WriteAllText(file, "{\"song\":null}");
            store = new LocalLyricsStore(directory);
            Check(store.HasOverride("song"), "structurally invalid primary recovers backup");
            File.WriteAllText(file, "{bad-primary");
            File.WriteAllText(file + ".bak", "{bad-backup");
            store = new LocalLyricsStore(directory);
            store.SetOverride("new", "[00:01.00]new");
            Check(Directory.GetFiles(directory, "*.corrupt-*").Any(path => File.ReadAllText(path) == "{bad-primary"), "both broken: preserve original on new save");
            Check(File.ReadAllText(file + ".bak") == "{bad-backup", "both broken: do not destroy backup evidence");

            store.SetCache("song", "[00:01.00]cached", "version", "version-9", LyricsMatchConfidence.High);
            var cacheFile = Path.Combine(directory, "lyrics-cache.json");
            using (var locked = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!PersistenceOperation.Try(() => store.ClearCache(), out _), "cache clear failure is contained");
                Check(store.HasCache("song"), "cache clear failure preserves memory");
                Check(!PersistenceOperation.Try(() => store.RemoveCache("song"), out _), "cache removal failure is contained");
                Check(store.HasCache("song"), "cache removal failure preserves memory");
                Check(!PersistenceOperation.Try(() => store.SetCache("song", "[00:01.00]bad", "bad"), out _), "cache overwrite failure is contained");
                Check(store.GetCache("song")?.CandidateKey == "version-9", "failed cache update retains provenance");
            }
            Check(new LocalLyricsStore(directory).GetCache("song")?.CandidateKey == "version-9", "cache provenance survives restart");
            foreach (var exception in new Exception[] { new IOException("full"), new UnauthorizedAccessException("denied"), new JsonException("invalid") })
                Check(!PersistenceOperation.Try(() => throw exception, out _), "expected persistence exception is contained");

            var preferences = new LyricsPreferences();
            preferences.Rejected["song"] = Enumerable.Range(1, 8).Select(i => "version-" + i).ToHashSet();
            var candidates = Enumerable.Range(1, 12).Select(i => new LyricsCandidate("version-" + i, "album-" + i,
                LrcParser.Parse("[00:01.00]line " + i), i,
                new LyricsMatchAssessment(LyricsMatchConfidence.High, "test"))).ToArray();
            var ranked = LyricsClient.RankCandidates(candidates);
            Check(ranked.Count == 12, "search ranking retains candidates beyond eight");
            var visible = LyricsCandidatePolicy.Visible(ranked, "song", preferences);
            Check(visible.Count == 4 && visible[0].Key == "version-9", "ninth candidate fills vacancy after rejection");
            Check(LyricsCandidatePolicy.Visible(ranked, "other", preferences).Count == 8, "display cap follows per-song filtering");
            Check(LyricsCandidatePolicy.Automatic(visible, "song", preferences, "version-1")?.Key == "version-9", "rejected remembered version cannot override exclusion");
            var low = candidates[8] with { Match = new(LyricsMatchConfidence.Low, "low") };
            Check(LyricsCandidatePolicy.Automatic([low], "song", preferences, null) is null, "unchecked low confidence is not preloaded");
            Check(LyricsCandidatePolicy.Automatic([low], "song", preferences, low.Key)?.Key == low.Key, "explicit remembered choice remains available");
            var cached = store.GetCache("song")!;
            Check(LyricsCandidatePolicy.CanUseCache(cached, "song", preferences), "non-rejected provenance cache allowed");
            preferences.Rejected["song"].Add(cached.CandidateKey!);
            Check(!LyricsCandidatePolicy.CanUseCache(cached, "song", preferences), "late rejection blocks an existing cache");
            Check(!LyricsCandidatePolicy.CanUseCache(cached with { UserSelected = true }, "song", preferences), "rejection overrides manual selection metadata");
            Check(!LyricsCandidatePolicy.CanUseCache(cached with { CandidateKey = null }, "song", preferences), "unknown legacy version cannot bypass exclusions");
            Check(LyricsCandidatePolicy.CanUseCache(cached with { CandidateKey = null }, "other", preferences), "safe legacy offline caches remain compatible");
            Check(!LyricsCandidatePolicy.CanUseCache(cached with { CandidateKey = null, Label = "预加载 · old" }, "other", preferences), "old unchecked preloads ignored");
            Check(!LyricsCandidatePolicy.CanUseCache(cached with { Confidence = LyricsMatchConfidence.Low }, "other", preferences), "low confidence cache requires explicit selection");
            Check(LyricsCandidatePolicy.CanUseCache(cached with { Confidence = LyricsMatchConfidence.Low, UserSelected = true }, "other", preferences), "manual low confidence selection can work offline");
            Check(LyricsCandidatePolicy.Automatic(ranked, "song", preferences, null)?.Key == "version-10", "preload rechecks newly changed exclusions");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine($"Lyrics recovery/selection: {_checks} checks passed (isolated temporary files).");
    }
}
