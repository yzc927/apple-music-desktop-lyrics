using System.Diagnostics;
using System.Windows.Threading;
using Windows.Media.Control;

namespace AppleMusicDesktopLyrics;

internal sealed class MediaLyricsController : IDisposable
{
    private enum LyricsLoadKind { None, Lrclib, Cache, Local, Apple }

    private readonly Action<string, string, double, string> _render;
    private readonly Action<string> _notify;
    private readonly LyricsClient _lyricsClient = new();
    private readonly AppleMusicUiLyricsProvider _appleLyrics = new();
    private readonly SongOffsetStore _offsetStore = new();
    private readonly SongLyricsChoiceStore _choiceStore = new();
    private readonly LocalLyricsStore _localLyrics = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _applePollTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private IReadOnlyList<LyricLine> _lines = [];
    private IReadOnlyList<LyricsCandidate> _candidates = [];
    private int _candidateIndex = -1;
    private string _mediaKey = "";
    private string _title = "";
    private TimeSpan _clockPosition;
    private long _clockUpdatedTicks;
    private TimeSpan _clockCorrection;
    private bool _clockInitialized;
    private bool _clockPlaying;
    private bool _playing;
    private bool _polling;
    private string _artist = "";
    private TimeSpan _lyricsOffset;
    private TimeSpan _automaticOffset;
    private readonly Queue<double> _calibrationSamples = new();
    private double _secondsPerVocalUnit = 0.28;
    private int _lastRenderedIndex = -1;
    private CancellationTokenSource _lyricsCts = new();
    private bool _usingAppleLyrics;
    private bool _applePolling;
    private int _appleReadFailures;
    private string _appleCurrent = "";
    private string _appleNext = "";
    private bool _appleInstrumental;
    private TimeSpan _appleLineStartedAt;
    private string _calibrationAppleCurrent = "";
    private bool _hasAutoCalibration;
    private string _pendingCalibrationCurrent = "";
    private string _pendingCalibrationNext = "";
    private int _pendingCalibrationCount;
    private int _lastCalibrationLineIndex = -1;
    private int _lastProgressIndex = -1;
    private double _lastProgress;
    private bool _automaticCalibrationEnabled;
    private LyricsLoadKind _lyricsLoadKind;
    private long _lastLineAdvancedTicks = Stopwatch.GetTimestamp();
    private long _lastWatchdogReadTicks;
    private long _uiPlaybackEvidenceUntilTicks;

    public MediaLyricsController(Action<string, string, double, string> render, Action<string> notify)
    {
        _render = render;
        _notify = notify;
        // 30 fps keeps short/rap lines smooth. At the old 100 ms cadence a
        // 150–250 ms LRC row only received one or two partial sweep frames.
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
            (_, _) => Render(), Dispatcher.CurrentDispatcher);
        _pollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            async (_, _) => await PollAsync(), Dispatcher.CurrentDispatcher);
        _applePollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            async (_, _) => await PollAppleLyricsAsync(), Dispatcher.CurrentDispatcher);
    }

    public double OffsetSeconds => _lyricsOffset.TotalSeconds;
    public bool AutomaticCalibrationEnabled => _automaticCalibrationEnabled;
    public int LyricsCandidateCount => _candidates.Count;
    public int LyricsCandidateIndex => _candidateIndex;
    public string LyricsCandidateLabel => _candidateIndex >= 0 && _candidateIndex < _candidates.Count
        ? _candidates[_candidateIndex].Label
        : "自动匹配";
    public string LyricsSource => _usingAppleLyrics
        ? "Apple Music 官方歌词（后备）"
        : _lyricsLoadKind switch
        {
            LyricsLoadKind.Local => "本地 LRC（永久覆盖）",
            LyricsLoadKind.Cache => "LRCLIB 本地缓存（离线后备）",
            LyricsLoadKind.Lrclib => _hasAutoCalibration
                ? "LRCLIB 同步歌词（Apple 自动对时）"
                : "LRCLIB 同步歌词（优先）",
            _ => "正在获取歌词"
        };
    public bool HasLocalLyricsOverride => _localLyrics.HasOverride(_mediaKey);
    public bool HasCachedLyrics => _localLyrics.HasCache(_mediaKey);
    public string CurrentLrcText => _lines.Count == 0 ? "" : LrcParser.Serialize(_lines);

    public async void Start()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _pollTimer.Start();
            _renderTimer.Start();
            _applePollTimer.Start();
            await PollAsync();
        }
        catch (Exception ex)
        {
            _render("无法读取 Windows 媒体会话", ex.Message, 0, "");
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _manager is null) return;
        _polling = true;
        try
        {
            _session = FindAppleMusicSession(_manager);
            if (_session is null)
            {
                _playing = false;
                _render("正在等待 Apple Music…", "请先在 Apple Music 中播放一首歌曲", 0, "");
                return;
            }

            var media = await _session.TryGetMediaPropertiesAsync();
            var timeline = _session.GetTimelineProperties();
            var playback = _session.GetPlaybackInfo();
            var now = DateTimeOffset.UtcNow;
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var sampledPosition = timeline.Position;
            if (playing)
            {
                var sampleAge = now - timeline.LastUpdatedTime;
                // GSMTC's Position is the value at LastUpdatedTime. Apple Music can
                // leave that timestamp untouched for an entire track, so limiting
                // extrapolation to five seconds repeatedly snapped our clock back
                // to a stale position. Keep extrapolating until the track ends;
                // a seek updates the sample and is still detected below.
                if (sampleAge > TimeSpan.Zero && sampleAge < TimeSpan.FromHours(12))
                {
                    sampledPosition += sampleAge;
                    if (timeline.EndTime > TimeSpan.Zero && sampledPosition > timeline.EndTime)
                        sampledPosition = timeline.EndTime;
                }
            }

            var key = $"{media.Title}\n{media.Artist}\n{timeline.EndTime.TotalSeconds:0}";
            var mediaChanged = key != _mediaKey;
            UpdatePlaybackClock(sampledPosition, playing, now, mediaChanged);
            _playing = playing;
            if (!mediaChanged) return;
            _mediaKey = key;
            _lyricsOffset = _offsetStore.Get(_mediaKey);
            _title = media.Title;
            _artist = media.Artist;
            _lines = [];
            _candidates = [];
            _candidateIndex = -1;
            _usingAppleLyrics = false;
            _lyricsLoadKind = LyricsLoadKind.None;
            _appleCurrent = "";
            _appleNext = "";
            _appleInstrumental = false;
            _appleReadFailures = 0;
            ResetTimingState();
            _render(media.Title, $"{media.Artist} · 正在读取 Apple Music 歌词…", 0, _artist);
            await LoadLyricsAsync(media.Title, media.Artist, media.AlbumTitle, timeline.EndTime);
        }
        catch (Exception ex)
        {
            _render(string.IsNullOrWhiteSpace(_title) ? "读取播放信息失败" : _title, ex.Message, 0, _artist);
        }
        finally
        {
            _polling = false;
        }
    }

    private static GlobalSystemMediaTransportControlsSession? FindAppleMusicSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        static bool IsAppleMusic(GlobalSystemMediaTransportControlsSession session) =>
            session.SourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ||
            session.SourceAppUserModelId.Contains("Apple.Music", StringComparison.OrdinalIgnoreCase);

        var current = manager.GetCurrentSession();
        if (current is not null && IsAppleMusic(current)) return current;
        return manager.GetSessions()
            .Where(IsAppleMusic)
            .OrderByDescending(session => session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            .ThenByDescending(session => session.GetTimelineProperties().LastUpdatedTime)
            .FirstOrDefault();
    }

    private async Task LoadLyricsAsync(string title, string artist, string album, TimeSpan duration)
    {
        _lyricsCts.Cancel();
        _lyricsCts.Dispose();
        _lyricsCts = new CancellationTokenSource();
        try
        {
            var local = _localLyrics.GetOverride(_mediaKey);
            if (local is not null && TryApplyStoredLyrics(local, LyricsLoadKind.Local)) return;

            var search = await _lyricsClient.SearchAsync(title, artist, album, duration, _lyricsCts.Token);
            _candidates = search.Candidates;
            if (_candidates.Count > 0)
            {
                var remembered = _choiceStore.Get(_mediaKey);
                _candidateIndex = remembered is null
                    ? 0
                    : Math.Max(0, _candidates.ToList().FindIndex(item => item.Key == remembered));
                ApplyCandidate(_candidateIndex, remember: false);
                return;
            }

            var cached = _localLyrics.GetCache(_mediaKey);
            if (cached is not null && TryApplyStoredLyrics(cached, LyricsLoadKind.Cache)) return;
            if (await TryAppleLyricsFallbackAsync(title)) return;
            _render(title, "未找到同步歌词", 0, _artist);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            try
            {
                var cached = _localLyrics.GetCache(_mediaKey);
                if (cached is not null && TryApplyStoredLyrics(cached, LyricsLoadKind.Cache)) return;
                if (await TryAppleLyricsFallbackAsync(title)) return;
            }
            catch (OperationCanceledException) { return; }
            catch { /* Preserve the original LRCLIB error below. */ }
            _render(title, $"歌词获取失败：{ex.Message}", 0, _artist);
        }
    }

    private async Task<bool> TryAppleLyricsFallbackAsync(string title)
    {
        var appleSnapshot = await _appleLyrics.PrepareAsync(title, _lyricsCts.Token);
        if (appleSnapshot is null) return false;
        _usingAppleLyrics = true;
        _lyricsLoadKind = LyricsLoadKind.Apple;
        ApplyAppleSnapshot(appleSnapshot);
        return true;
    }

    private void Render()
    {
        if (_usingAppleLyrics)
        {
            RenderAppleLyrics();
            return;
        }
        if (_lines.Count == 0 || _session is null) return;
        var position = EffectivePosition();
        var index = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Time > position) break;
            index = i;
        }

        // Clock corrections and Apple calibration are deliberately gradual. Never
        // let those tiny corrections move the displayed row backwards; a real seek
        // resets this guard in UpdatePlaybackClock.
        var previousIndex = _lastRenderedIndex;
        if (_lastRenderedIndex >= 0 && index < _lastRenderedIndex)
            index = _lastRenderedIndex;
        if (index > previousIndex)
            _lastLineAdvancedTicks = Stopwatch.GetTimestamp();
        _lastRenderedIndex = index;
        var current = index >= 0 ? LyricTiming.DisplayText(_lines[index].Text) : _title;
        var next = index + 1 < _lines.Count ? LyricTiming.DisplayText(_lines[index + 1].Text) : "";
        var progress = LyricTiming.Progress(_lines, index, position, _secondsPerVocalUnit);
        if (index == _lastProgressIndex)
            progress = Math.Max(progress, _lastProgress);
        else
        {
            _lastProgressIndex = index;
            _lastProgress = 0;
        }
        _lastProgress = progress;
        _render(current, next, Math.Clamp(progress, 0, 1), _artist);
    }

    private async Task PollAppleLyricsAsync()
    {
        if (_applePolling || string.IsNullOrWhiteSpace(_title)) return;
        var nowTicks = Stopwatch.GetTimestamp();
        var stalled = !_usingAppleLyrics && _lines.Count > 0 &&
            Stopwatch.GetElapsedTime(_lastLineAdvancedTicks, nowTicks) >= TimeSpan.FromSeconds(6);
        if (!_usingAppleLyrics && (_lines.Count == 0 || (!_automaticCalibrationEnabled && !stalled)))
            return;
        if (stalled && !_automaticCalibrationEnabled &&
            Stopwatch.GetElapsedTime(_lastWatchdogReadTicks, nowTicks) < TimeSpan.FromMilliseconds(750))
            return;
        _lastWatchdogReadTicks = nowTicks;
        _applePolling = true;
        try
        {
            var snapshot = await Task.Run(() => _appleLyrics.TryRead());
            if (snapshot is not null)
            {
                _appleReadFailures = 0;
                if (_usingAppleLyrics) ApplyAppleSnapshot(snapshot);
                else
                {
                    if (stalled && ApplyStallRecoverySnapshot(snapshot)) return;
                    ApplyCalibrationSnapshot(snapshot);
                }
                return;
            }

            _appleReadFailures++;
            if (_usingAppleLyrics && _appleReadFailures == 4)
                _ = Task.Run(_appleLyrics.OpenLyricsPanelIfNeeded);
        }
        finally
        {
            _applePolling = false;
        }
    }

    private bool ApplyStallRecoverySnapshot(AppleLyricsSnapshot snapshot)
    {
        if (snapshot.IsInstrumental || string.IsNullOrWhiteSpace(snapshot.Current)) return false;
        var lineIndex = LyricTiming.FindForwardRecoveryLine(
            _lines, snapshot.Current, snapshot.Next, _lastRenderedIndex);
        if (lineIndex < 0) return false;
        var rawPosition = AdvancePlaybackClock(Stopwatch.GetTimestamp());
        _automaticOffset = _lines[lineIndex].Time - rawPosition - _lyricsOffset;
        _automaticOffset = TimeSpan.FromSeconds(
            Math.Clamp(_automaticOffset.TotalSeconds, -120, 120));
        _lastRenderedIndex = lineIndex - 1;
        _lastProgressIndex = -1;
        _lastProgress = 0;
        _lastLineAdvancedTicks = Stopwatch.GetTimestamp();
        _uiPlaybackEvidenceUntilTicks = Stopwatch.GetTimestamp() +
            (long)(Stopwatch.Frequency * 5d);
        _notify("检测到歌词停滞，已自动恢复同步");
        return true;
    }

    private void ApplyAppleSnapshot(AppleLyricsSnapshot snapshot)
    {
        if (!string.Equals(_appleCurrent, snapshot.Current, StringComparison.Ordinal))
        {
            _appleCurrent = snapshot.Current;
            _appleLineStartedAt = AdvancePlaybackClock(Stopwatch.GetTimestamp());
        }
        _appleNext = snapshot.Next;
        _appleInstrumental = snapshot.IsInstrumental;
    }

    private void RenderAppleLyrics()
    {
        if (string.IsNullOrWhiteSpace(_appleCurrent)) return;
        if (_appleInstrumental)
        {
            _render(_appleCurrent, _appleNext, 0, _artist);
            return;
        }

        var position = AdvancePlaybackClock(Stopwatch.GetTimestamp()) + _lyricsOffset;
        var elapsed = Math.Max(0, (position - _appleLineStartedAt).TotalSeconds);
        var estimatedSeconds = Math.Clamp(_appleCurrent.Length * 0.28, 1.4, 6.0);
        var progress = Math.Clamp(elapsed / estimatedSeconds, 0, 1);
        _render(_appleCurrent, _appleNext, progress, _artist);
    }

    private void ApplyCalibrationSnapshot(AppleLyricsSnapshot snapshot)
    {
        if (!_automaticCalibrationEnabled || snapshot.IsInstrumental ||
            string.IsNullOrWhiteSpace(snapshot.Current)) return;
        if (!string.Equals(_pendingCalibrationCurrent, snapshot.Current, StringComparison.Ordinal))
        {
            _pendingCalibrationCurrent = snapshot.Current;
            _pendingCalibrationNext = snapshot.Next;
            _pendingCalibrationCount = 1;
            return;
        }
        _pendingCalibrationNext = snapshot.Next;
        _pendingCalibrationCount++;
        if (_pendingCalibrationCount < 2 ||
            string.Equals(_calibrationAppleCurrent, snapshot.Current, StringComparison.Ordinal)) return;

        var rawPosition = AdvancePlaybackClock(Stopwatch.GetTimestamp());
        var expected = rawPosition + _lyricsOffset + _automaticOffset;
        var lineIndex = LyricTiming.FindCalibrationLine(
            _lines, snapshot.Current, _pendingCalibrationNext, expected);
        if (lineIndex < 0 || lineIndex <= _lastCalibrationLineIndex) return;
        // Automatic calibration corrects the source timeline only. The user's
        // remembered manual offset is intentionally applied afterwards and must
        // never be cancelled by background calibration.
        var correction = (_lines[lineIndex].Time - rawPosition).TotalSeconds;
        if (Math.Abs(correction) > 120) return;
        if (!_hasAutoCalibration && Math.Abs(correction) > 8)
        {
            _automaticOffset = TimeSpan.FromSeconds(correction);
            _hasAutoCalibration = true;
            _calibrationAppleCurrent = snapshot.Current;
            _lastCalibrationLineIndex = lineIndex;
            _lastRenderedIndex = lineIndex - 1;
            _lastProgressIndex = -1;
            _lastProgress = 0;
            _notify("已按 Apple Music 当前歌词恢复同步");
            return;
        }
        var current = _automaticOffset.TotalSeconds;
        if (_hasAutoCalibration && Math.Abs(correction - current) > 1.25) return;
        _calibrationSamples.Enqueue(correction);
        while (_calibrationSamples.Count > 5) _calibrationSamples.Dequeue();
        var ordered = _calibrationSamples.Order().ToArray();
        var median = ordered[ordered.Length / 2];
        var calibrated = _calibrationSamples.Count == 1
            ? median
            : current + Math.Clamp(median - current, -0.2, 0.2);
        _automaticOffset = TimeSpan.FromSeconds(Math.Clamp(calibrated, -6, 6));
        _hasAutoCalibration = true;
        _calibrationAppleCurrent = snapshot.Current;
        _lastCalibrationLineIndex = lineIndex;
    }

    private TimeSpan EffectivePosition() =>
        AdvancePlaybackClock(Stopwatch.GetTimestamp()) + _lyricsOffset + _automaticOffset;

    private void ApplyCandidate(int index, bool remember)
    {
        if (index < 0 || index >= _candidates.Count) return;
        _candidateIndex = index;
        _lines = _candidates[index].Lines;
        _usingAppleLyrics = false;
        _lyricsLoadKind = LyricsLoadKind.Lrclib;
        if (remember) _choiceStore.Set(_mediaKey, _candidates[index].Key);
        _localLyrics.SetCache(_mediaKey, LrcParser.Serialize(_lines), _candidates[index].Label);
        ResetTimingState();
        _secondsPerVocalUnit = LyricTiming.EstimateSecondsPerUnit(_lines);
    }

    private void ResetTimingState()
    {
        _automaticOffset = TimeSpan.Zero;
        _calibrationSamples.Clear();
        _calibrationAppleCurrent = "";
        _hasAutoCalibration = false;
        _pendingCalibrationCurrent = "";
        _pendingCalibrationNext = "";
        _pendingCalibrationCount = 0;
        _lastCalibrationLineIndex = -1;
        _lastRenderedIndex = -1;
        _lastProgressIndex = -1;
        _lastProgress = 0;
        _lastLineAdvancedTicks = Stopwatch.GetTimestamp();
        _secondsPerVocalUnit = 0.28;
    }

    private void UpdatePlaybackClock(TimeSpan sample, bool playing, DateTimeOffset now, bool forceReset)
    {
        var monotonicNow = Stopwatch.GetTimestamp();
        if (!_clockInitialized || forceReset || playing != _clockPlaying)
        {
            _clockPosition = sample;
            _clockUpdatedTicks = monotonicNow;
            _clockCorrection = TimeSpan.Zero;
            _clockPlaying = playing;
            _clockInitialized = true;
            _lastRenderedIndex = -1;
            _lastProgressIndex = -1;
            _lastProgress = 0;
            return;
        }

        var predicted = AdvancePlaybackClock(monotonicNow);
        var error = sample - predicted;
        if (Math.Abs(error.TotalSeconds) >= 1.75)
        {
            // A large discontinuity is a real seek or track restart.
            _clockPosition = sample;
            _clockUpdatedTicks = monotonicNow;
            _clockCorrection = TimeSpan.Zero;
            _automaticOffset = TimeSpan.Zero;
            _calibrationSamples.Clear();
            _calibrationAppleCurrent = "";
            _hasAutoCalibration = false;
            _pendingCalibrationCurrent = "";
            _pendingCalibrationNext = "";
            _pendingCalibrationCount = 0;
            _lastCalibrationLineIndex = -1;
            _lastRenderedIndex = -1;
            _lastProgressIndex = -1;
            _lastProgress = 0;
            return;
        }

        // Merge small timing errors into a bounded correction budget. Render ticks
        // consume it gradually, so the lyric clock never freezes or visibly jumps.
        var mergedSeconds = Math.Clamp(
            _clockCorrection.TotalSeconds + error.TotalSeconds * 0.45, -1.2, 1.2);
        _clockCorrection = TimeSpan.FromSeconds(mergedSeconds);
    }

    private TimeSpan AdvancePlaybackClock(long nowTicks)
    {
        if (!_clockInitialized) return TimeSpan.Zero;
        var elapsedSeconds = (nowTicks - _clockUpdatedTicks) / (double)Stopwatch.Frequency;
        var elapsed = TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
        if (elapsed > TimeSpan.FromSeconds(2)) elapsed = TimeSpan.FromSeconds(2);

        var shouldAdvance = _clockPlaying || nowTicks < _uiPlaybackEvidenceUntilTicks;
        var advance = shouldAdvance ? elapsed : TimeSpan.Zero;
        if (shouldAdvance && _clockCorrection != TimeSpan.Zero)
        {
            // Slew by at most 20% of real time. Negative correction still leaves
            // the playback clock moving forward at 80% speed.
            var maximumStep = elapsed.TotalSeconds * 0.20;
            var stepSeconds = Math.Clamp(_clockCorrection.TotalSeconds, -maximumStep, maximumStep);
            var correctionStep = TimeSpan.FromSeconds(stepSeconds);
            advance += correctionStep;
            _clockCorrection -= correctionStep;
        }

        _clockPosition += advance;
        _clockUpdatedTicks = nowTicks;
        return _clockPosition;
    }

    public void ChangeLyricsCandidate(int delta)
    {
        if (_candidates.Count <= 1)
        {
            ShowTransient("当前只有一个匹配版本");
            return;
        }
        var next = (_candidateIndex + delta) % _candidates.Count;
        if (next < 0) next += _candidates.Count;
        ApplyCandidate(next, remember: true);
        ShowTransient($"已切换歌词版本 {_candidateIndex + 1}/{_candidates.Count}");
    }

    private bool TryApplyStoredLyrics(StoredLyrics stored, LyricsLoadKind kind)
    {
        var parsed = LrcParser.Parse(stored.Lrc);
        if (parsed.Count == 0) return false;
        _lines = parsed;
        _candidates = [];
        _candidateIndex = -1;
        _usingAppleLyrics = false;
        _lyricsLoadKind = kind;
        ResetTimingState();
        _secondsPerVocalUnit = LyricTiming.EstimateSecondsPerUnit(_lines);
        return true;
    }

    public bool SetLocalLyrics(string lrc, string label, out string error)
    {
        if (string.IsNullOrWhiteSpace(_mediaKey))
        {
            error = "当前没有正在播放的歌曲";
            return false;
        }
        var parsed = LrcParser.Parse(lrc);
        if (parsed.Count == 0)
        {
            error = "没有找到 [分:秒] 格式的有效时间戳";
            return false;
        }
        _localLyrics.SetOverride(_mediaKey, LrcParser.Serialize(parsed), label);
        TryApplyStoredLyrics(_localLyrics.GetOverride(_mediaKey)!, LyricsLoadKind.Local);
        error = "";
        ShowTransient($"已保存本地歌词（{parsed.Count} 行）");
        return true;
    }

    public void RemoveLocalLyricsOverride()
    {
        if (!_localLyrics.HasOverride(_mediaKey))
        {
            ShowTransient("当前歌曲没有本地歌词覆盖");
            return;
        }
        _localLyrics.RemoveOverride(_mediaKey);
        ShowTransient("已删除本地覆盖，正在重新获取歌词");
        RefreshLyrics();
    }

    public void ClearLyricsCache()
    {
        _localLyrics.ClearCache();
        ShowTransient("LRCLIB 本地缓存已清空");
    }

    public void SetAutomaticCalibration(bool enabled, bool notify = true)
    {
        _automaticCalibrationEnabled = enabled;
        ResetTimingState();
        if (notify)
            ShowTransient(enabled ? "已开启 Apple 自动对时" : "已关闭 Apple 自动对时");
    }

    public void RefreshLyrics()
    {
        _mediaKey = "";
        _ = PollAsync();
    }

    public void ShowTransient(string message)
    {
        _notify(message);
    }

    public void AdjustOffset(double seconds)
    {
        _lyricsOffset += TimeSpan.FromSeconds(seconds);
        _lastRenderedIndex = -1;
        _lastProgressIndex = -1;
        _lastProgress = 0;
        if (!string.IsNullOrWhiteSpace(_mediaKey))
            _offsetStore.Set(_mediaKey, _lyricsOffset);
        var total = _lyricsOffset.TotalSeconds;
        var description = Math.Abs(total) < 0.01
            ? "歌词偏移已归零"
            : total > 0
                ? $"歌词已快 {total:0.0} 秒"
                : $"歌词已慢 {Math.Abs(total):0.0} 秒";
        ShowTransient(description);
    }

    public void ResetOffset()
    {
        _lyricsOffset = TimeSpan.Zero;
        _lastRenderedIndex = -1;
        _lastProgressIndex = -1;
        _lastProgress = 0;
        if (!string.IsNullOrWhiteSpace(_mediaKey))
            _offsetStore.Set(_mediaKey, TimeSpan.Zero);
        ShowTransient("歌词偏移已归零");
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _renderTimer.Stop();
        _applePollTimer.Stop();
        _lyricsCts.Cancel();
        _lyricsCts.Dispose();
    }
}
