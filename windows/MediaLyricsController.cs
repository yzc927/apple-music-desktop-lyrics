using System.Windows.Threading;
using Windows.Media.Control;

namespace AppleMusicDesktopLyrics;

internal sealed class MediaLyricsController : IDisposable
{
    private readonly Action<string, string, double, string> _render;
    private readonly Action<string> _notify;
    private readonly LyricsClient _lyricsClient = new();
    private readonly AppleMusicUiLyricsProvider _appleLyrics = new();
    private readonly SongOffsetStore _offsetStore = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _applePollTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private IReadOnlyList<LyricLine> _lines = [];
    private string _mediaKey = "";
    private string _title = "";
    private TimeSpan _clockPosition;
    private DateTimeOffset _clockUpdatedAt;
    private TimeSpan _clockCorrection;
    private bool _clockInitialized;
    private bool _clockPlaying;
    private bool _playing;
    private bool _polling;
    private string _artist = "";
    private TimeSpan _lyricsOffset;
    private CancellationTokenSource _lyricsCts = new();
    private bool _usingAppleLyrics;
    private bool _applePolling;
    private int _appleReadFailures;
    private string _appleCurrent = "";
    private string _appleNext = "";
    private bool _appleInstrumental;
    private TimeSpan _appleLineStartedAt;

    public MediaLyricsController(Action<string, string, double, string> render, Action<string> notify)
    {
        _render = render;
        _notify = notify;
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render,
            (_, _) => Render(), Dispatcher.CurrentDispatcher);
        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            async (_, _) => await PollAsync(), Dispatcher.CurrentDispatcher);
        _applePollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            async (_, _) => await PollAppleLyricsAsync(), Dispatcher.CurrentDispatcher);
    }

    public double OffsetSeconds => _lyricsOffset.TotalSeconds;

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
                if (sampleAge > TimeSpan.Zero && sampleAge < TimeSpan.FromSeconds(5))
                    sampledPosition += sampleAge;
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
            _usingAppleLyrics = false;
            _appleCurrent = "";
            _appleNext = "";
            _appleInstrumental = false;
            _appleReadFailures = 0;
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
        var sessions = manager.GetSessions();
        return sessions.FirstOrDefault(session =>
                   session.SourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ||
                   session.SourceAppUserModelId.Contains("Apple.Music", StringComparison.OrdinalIgnoreCase))
               ?? manager.GetCurrentSession();
    }

    private async Task LoadLyricsAsync(string title, string artist, string album, TimeSpan duration)
    {
        _lyricsCts.Cancel();
        _lyricsCts.Dispose();
        _lyricsCts = new CancellationTokenSource();
        try
        {
            _lines = await _lyricsClient.GetAsync(title, artist, album, duration, _lyricsCts.Token);
            if (_lines.Count > 0) return;

            var appleSnapshot = await _appleLyrics.PrepareAsync(title, _lyricsCts.Token);
            if (appleSnapshot is not null)
            {
                _usingAppleLyrics = true;
                ApplyAppleSnapshot(appleSnapshot);
                return;
            }
            _render(title, "未找到同步歌词", 0, _artist);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _render(title, $"歌词获取失败：{ex.Message}", 0, _artist);
        }
    }

    private void Render()
    {
        if (_usingAppleLyrics)
        {
            RenderAppleLyrics();
            return;
        }
        if (_lines.Count == 0 || _session is null) return;
        var position = AdvancePlaybackClock(DateTimeOffset.UtcNow) + _lyricsOffset;
        var index = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Time > position) break;
            index = i;
        }

        var current = index >= 0 ? _lines[index].Text : _title;
        var next = index + 1 < _lines.Count ? _lines[index + 1].Text : "";
        var progress = 0d;
        if (index >= 0 && index + 1 < _lines.Count)
        {
            var naturalDuration = _lines[index + 1].Time - _lines[index].Time;
            // Line-synced LRC has no word timestamps. Avoid stretching the sweep
            // across a long instrumental gap: finish within a reasonable singing
            // duration, then hold the completed color until the next line.
            var estimatedSeconds = Math.Clamp(_lines[index].Text.Length * 0.28, 1.4, 6.0);
            var activeDuration = naturalDuration > TimeSpan.FromSeconds(estimatedSeconds * 1.35)
                ? TimeSpan.FromSeconds(estimatedSeconds)
                : naturalDuration;
            var lineDuration = activeDuration.TotalMilliseconds;
            if (lineDuration > 0)
                progress = (position - _lines[index].Time).TotalMilliseconds / lineDuration;
        }
        else if (index == _lines.Count - 1)
        {
            progress = 1;
        }
        _render(current, next, Math.Clamp(progress, 0, 1), _artist);
    }

    private async Task PollAppleLyricsAsync()
    {
        if (!_usingAppleLyrics || _applePolling || string.IsNullOrWhiteSpace(_title)) return;
        _applePolling = true;
        try
        {
            var snapshot = await Task.Run(() => _appleLyrics.TryRead());
            if (snapshot is not null)
            {
                _appleReadFailures = 0;
                ApplyAppleSnapshot(snapshot);
                return;
            }

            _appleReadFailures++;
            if (_appleReadFailures == 4)
                _ = Task.Run(_appleLyrics.OpenLyricsPanelIfNeeded);
        }
        finally
        {
            _applePolling = false;
        }
    }

    private void ApplyAppleSnapshot(AppleLyricsSnapshot snapshot)
    {
        if (!string.Equals(_appleCurrent, snapshot.Current, StringComparison.Ordinal))
        {
            _appleCurrent = snapshot.Current;
            _appleLineStartedAt = AdvancePlaybackClock(DateTimeOffset.UtcNow);
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

        var position = AdvancePlaybackClock(DateTimeOffset.UtcNow) + _lyricsOffset;
        var elapsed = Math.Max(0, (position - _appleLineStartedAt).TotalSeconds);
        var estimatedSeconds = Math.Clamp(_appleCurrent.Length * 0.28, 1.4, 6.0);
        var progress = Math.Clamp(elapsed / estimatedSeconds, 0, 1);
        _render(_appleCurrent, _appleNext, progress, _artist);
    }

    private void UpdatePlaybackClock(TimeSpan sample, bool playing, DateTimeOffset now, bool forceReset)
    {
        if (!_clockInitialized || forceReset || playing != _clockPlaying)
        {
            _clockPosition = sample;
            _clockUpdatedAt = now;
            _clockCorrection = TimeSpan.Zero;
            _clockPlaying = playing;
            _clockInitialized = true;
            return;
        }

        var predicted = AdvancePlaybackClock(now);
        var error = sample - predicted;
        if (Math.Abs(error.TotalSeconds) >= 1.75)
        {
            // A large discontinuity is a real seek or track restart.
            _clockPosition = sample;
            _clockUpdatedAt = now;
            _clockCorrection = TimeSpan.Zero;
            return;
        }

        // Merge small timing errors into a bounded correction budget. Render ticks
        // consume it gradually, so the lyric clock never freezes or visibly jumps.
        var mergedSeconds = Math.Clamp(
            _clockCorrection.TotalSeconds + error.TotalSeconds * 0.45, -1.2, 1.2);
        _clockCorrection = TimeSpan.FromSeconds(mergedSeconds);
    }

    private TimeSpan AdvancePlaybackClock(DateTimeOffset now)
    {
        if (!_clockInitialized) return TimeSpan.Zero;
        var elapsed = now - _clockUpdatedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed > TimeSpan.FromSeconds(2)) elapsed = TimeSpan.FromSeconds(2);

        var advance = _clockPlaying ? elapsed : TimeSpan.Zero;
        if (_clockPlaying && _clockCorrection != TimeSpan.Zero)
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
        _clockUpdatedAt = now;
        return _clockPosition;
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
