using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace AppleMusicDesktopLyrics;

internal sealed record AppleLyricsSnapshot(string Current, string Next, bool IsInstrumental = false);

/// <summary>
/// Reads lyric elements that Apple Music exposes through Windows UI Automation.
/// Each Apple UI shape is isolated behind a strategy so one changed selector does
/// not disable the remaining fallbacks.
/// </summary>
internal sealed class AppleMusicUiLyricsProvider
{
    private const uint WmNull = 0;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const string CurrentLineId = "CurrentLine";
    private const string CurrentInstrumentalId = "CurrentInstrumental";
    private const string LineId = "Line";
    private const string TimeBasedLyricsId = "TimeBasedLyrics";
    private const string TrackTitleId = "ScrollingText";

    private readonly IReadOnlyList<IAppleLyricsReadStrategy> _strategies =
    [
        new CurrentInstrumentalStrategy(),
        new CurrentLineStrategy(),
        new VirtualizedLineStrategy()
    ];

    public string LastFailureReason { get; private set; } = "尚未读取 Apple Music 歌词";
    public string LastStrategyName { get; private set; } = "";

    private readonly BoundedAsyncReader<AppleLyricsSnapshot> _reader = new();

    public async Task<AppleLyricsSnapshot?> ReadAsync(string title,
        bool allowBoundaryEstimate, CancellationToken cancellationToken)
    {
        var snapshot = await _reader.ReadAsync(() => TryRead(title, allowBoundaryEstimate),
            TimeSpan.FromSeconds(2), cancellationToken);
        if (_reader.Disabled)
            LastFailureReason = "Apple 歌词读取超时，本次运行已停用；请使用在线或本地 LRC";
        return snapshot;
    }

    public Task<AppleLyricsSnapshot?> PrepareAsync(string title, CancellationToken cancellationToken) =>
        ReadAsync(title, true, cancellationToken);

    private AppleLyricsSnapshot? TryRead(
        string? title = null, bool allowBoundaryEstimate = true)
    {
        LastStrategyName = "";
        var roots = GetRoots(out var foundUnresponsiveWindow);
        if (roots.Count == 0)
        {
            LastFailureReason = foundUnresponsiveWindow
                ? "Apple Music 主窗口无响应，已暂停官方歌词读取"
                : "未找到可访问的 Apple Music 主窗口";
            return null;
        }

        var matchedTrack = false;
        var attempted = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(title) && !MatchesCurrentTrack(root, title))
                    continue;
                matchedTrack = true;
                var context = new AppleLyricsReadContext(root, allowBoundaryEstimate);
                foreach (var strategy in _strategies)
                {
                    attempted.Add(strategy.Name);
                    try
                    {
                        var snapshot = strategy.TryRead(context);
                        if (snapshot is null) continue;
                        LastStrategyName = strategy.Name;
                        LastFailureReason = "";
                        return snapshot;
                    }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                    catch (COMException) { }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (COMException) { }
        }

        LastFailureReason = !matchedTrack
            ? "Apple Music 歌词面板与当前歌曲不一致"
            : $"Apple Music 未暴露可读取的当前歌词行（已尝试 {string.Join("、", attempted.Distinct())}）";
        return null;
    }

    private static AppleLyricsSnapshot ReadInstrumental(AutomationElement instrumental)
    {
        var walker = TreeWalker.RawViewWalker;
        var sibling = walker.GetNextSibling(instrumental);
        while (sibling is not null)
        {
            var nextLine = sibling.FindFirst(TreeScope.Element | TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, LineId));
            var next = nextLine?.Current.Name?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(next))
                return new AppleLyricsSnapshot("•••", next, true);
            sibling = walker.GetNextSibling(sibling);
        }
        return new AppleLyricsSnapshot("•••", "", true);
    }

    private static AppleLyricsSnapshot? ReadCurrentLine(AutomationElement root)
    {
        var currentElement = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, CurrentLineId));
        if (currentElement is null) return null;
        var current = currentElement.Current.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(current)) return null;

        var walker = TreeWalker.RawViewWalker;
        var currentGroup = walker.GetParent(currentElement);
        var nextGroup = currentGroup is null ? null : walker.GetNextSibling(currentGroup);
        var nextElement = nextGroup?.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, LineId));
        return new AppleLyricsSnapshot(current, nextElement?.Current.Name?.Trim() ?? "");
    }

    private static AppleLyricsSnapshot? ReadVirtualizedLines(
        AutomationElement root, bool allowBoundaryEstimate)
    {
        var lyricsView = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, TimeBasedLyricsId));
        if (lyricsView is null) return null;

        var viewport = lyricsView.Current.BoundingRectangle;
        if (!double.IsFinite(viewport.Top) || !double.IsFinite(viewport.Height) || viewport.Height <= 0)
            return null;
        var highlightAnchor = viewport.Top + viewport.Height * 0.08;

        var lines = lyricsView.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, LineId));
        AutomationElement? currentElement = null;
        var currentIndex = -1;
        var firstVisibleIndex = -1;
        var lastVisibleIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var bounds = line.Current.BoundingRectangle;
            if (string.IsNullOrWhiteSpace(line.Current.Name) ||
                !double.IsFinite(bounds.Top) || bounds.Height <= 0 ||
                bounds.Bottom <= viewport.Top || bounds.Top >= viewport.Bottom)
                continue;
            if (firstVisibleIndex < 0) firstVisibleIndex = index;
            lastVisibleIndex = index;
            var distance = Math.Abs(bounds.Top - highlightAnchor);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            currentElement = line;
            currentIndex = index;
        }

        if (currentElement is null) return null;
        if (!allowBoundaryEstimate &&
            (firstVisibleIndex == 0 || lastVisibleIndex == lines.Count - 1))
            return null;
        var current = currentElement.Current.Name?.Trim() ?? "";
        var next = currentIndex + 1 < lines.Count
            ? lines[currentIndex + 1].Current.Name?.Trim() ?? ""
            : "";
        return string.IsNullOrWhiteSpace(current)
            ? null
            : new AppleLyricsSnapshot(current, next);
    }

    private static bool MatchesCurrentTrack(AutomationElement root, string title)
    {
        var titleElement = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, TrackTitleId));
        var candidate = titleElement?.Current.Name?.Trim();
        return string.Equals(candidate, title.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AutomationElement> GetRoots(out bool foundUnresponsiveWindow)
    {
        foundUnresponsiveWindow = false;
        var roots = new List<AutomationElement>();
        var handles = new HashSet<IntPtr>();
        var processes = Process.GetProcessesByName("AppleMusic");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero || !handles.Add(handle)) continue;
                    if (!IsWindowResponsive(handle))
                    {
                        foundUnresponsiveWindow = true;
                        continue;
                    }
                    roots.Add(AutomationElement.FromHandle(handle));
                }
                catch (InvalidOperationException) { }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return roots;
    }

    private static bool IsWindowResponsive(IntPtr handle) =>
        SendMessageTimeout(handle, WmNull, UIntPtr.Zero, IntPtr.Zero,
            SmtoBlock | SmtoAbortIfHung, 250, out _) != IntPtr.Zero;

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW",
        ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg,
        UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    private sealed record AppleLyricsReadContext(
        AutomationElement Root, bool AllowBoundaryEstimate);

    private interface IAppleLyricsReadStrategy
    {
        string Name { get; }
        AppleLyricsSnapshot? TryRead(AppleLyricsReadContext context);
    }

    private sealed class CurrentInstrumentalStrategy : IAppleLyricsReadStrategy
    {
        public string Name => "当前伴奏标记";
        public AppleLyricsSnapshot? TryRead(AppleLyricsReadContext context)
        {
            var instrumental = context.Root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, CurrentInstrumentalId));
            return instrumental is null ? null : ReadInstrumental(instrumental);
        }
    }

    private sealed class CurrentLineStrategy : IAppleLyricsReadStrategy
    {
        public string Name => "CurrentLine 当前行";
        public AppleLyricsSnapshot? TryRead(AppleLyricsReadContext context) =>
            ReadCurrentLine(context.Root);
    }

    private sealed class VirtualizedLineStrategy : IAppleLyricsReadStrategy
    {
        public string Name => "虚拟化 Line 列表";
        public AppleLyricsSnapshot? TryRead(AppleLyricsReadContext context) =>
            ReadVirtualizedLines(context.Root, context.AllowBoundaryEstimate);
    }
}
