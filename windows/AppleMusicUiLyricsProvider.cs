using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace AppleMusicDesktopLyrics;

internal sealed record AppleLyricsSnapshot(string Current, string Next, bool IsInstrumental = false);

/// <summary>
/// Reads the lyric elements that Apple Music already exposes to Windows UI Automation.
/// No Apple credentials, cookies or private binaries are accessed.
/// </summary>
internal sealed class AppleMusicUiLyricsProvider
{
    private const string LyricsButtonId = "LyricsToggleButton";
    private const string CurrentLineId = "CurrentLine";
    private const string CurrentInstrumentalId = "CurrentInstrumental";
    private const string LineId = "Line";
    private const string TimeBasedLyricsId = "TimeBasedLyrics";
    private const string TrackTitleId = "ScrollingText";

    public async Task<AppleLyricsSnapshot?> PrepareAsync(string title, CancellationToken cancellationToken)
    {
        await Task.Run(OpenLyricsPanelIfNeeded, cancellationToken);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await Task.Run(() => TryRead(title), cancellationToken);
            if (snapshot is not null) return snapshot;
            await Task.Delay(250, cancellationToken);
        }
        return null;
    }

    public AppleLyricsSnapshot? TryRead(
        string? title = null, bool allowBoundaryEstimate = true)
    {
        try
        {
            var root = GetRoot();
            if (root is null || (!string.IsNullOrWhiteSpace(title) && !MatchesCurrentTrack(root, title)))
                return null;

            var instrumental = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, CurrentInstrumentalId));
            if (instrumental is not null)
                return ReadInstrumental(instrumental);

            var currentElement = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, CurrentLineId));
            if (currentElement is null)
                return TryReadVisibleLines(root, allowBoundaryEstimate);

            var current = currentElement.Current.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(current)) return null;

            // WinUI places every lyric TextBlock inside a sibling group. Walking to
            // the next group is much cheaper than scanning the entire Apple window.
            var walker = TreeWalker.RawViewWalker;
            var currentGroup = walker.GetParent(currentElement);
            var nextGroup = currentGroup is null ? null : walker.GetNextSibling(currentGroup);
            var nextElement = nextGroup?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, LineId));
            return new AppleLyricsSnapshot(current, nextElement?.Current.Name?.Trim() ?? "");
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }
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

    private static AppleLyricsSnapshot? TryReadVisibleLines(
        AutomationElement root, bool allowBoundaryEstimate)
    {
        // Recent Apple Music builds no longer expose a dedicated CurrentLine id.
        // Their lyric scroller keeps the highlighted row at a stable vertical
        // anchor. IsOffscreen cannot be used here: WinUI may mark the previous
        // partially clipped row as off-screen even while it remains visible.
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
        {
            // Near the beginning and end Apple clamps the scroller instead of
            // keeping the highlighted row at the usual anchor. UI Automation no
            // longer exposes which of the visible rows is highlighted, so using
            // the anchor here would manufacture a large, incorrect calibration.
            return null;
        }
        var current = currentElement.Current.Name?.Trim() ?? "";
        var next = currentIndex + 1 < lines.Count
            ? lines[currentIndex + 1].Current.Name?.Trim() ?? ""
            : "";
        return string.IsNullOrWhiteSpace(current)
            ? null
            : new AppleLyricsSnapshot(current, next);
    }

    public void OpenLyricsPanelIfNeeded()
    {
        try
        {
            var root = GetRoot();
            var button = root?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, LyricsButtonId));
            if (button?.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) == true &&
                pattern is TogglePattern toggle && toggle.Current.ToggleState == ToggleState.Off)
                toggle.Toggle();
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }
    }

    private static bool MatchesCurrentTrack(AutomationElement root, string title)
    {
        // The playback title is near the top of Apple Music's automation tree.
        // FindAll forces WinUI to materialise every matching element in large
        // search/library pages and can peg Apple Music's UI thread. The first
        // playback-title element is sufficient for the stale-panel guard.
        var titleElement = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, TrackTitleId));
        var candidate = titleElement?.Current.Name?.Trim();
        return string.Equals(candidate, title.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static AutomationElement? GetRoot()
    {
        var process = Process.GetProcessesByName("AppleMusic")
            .FirstOrDefault(item => item.MainWindowHandle != IntPtr.Zero);
        return process is null ? null : AutomationElement.FromHandle(process.MainWindowHandle);
    }
}
