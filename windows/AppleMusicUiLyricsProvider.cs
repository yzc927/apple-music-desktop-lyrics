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

    public AppleLyricsSnapshot? TryRead(string? title = null)
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
                return TryReadVisibleLines(root);

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

    private static AppleLyricsSnapshot? TryReadVisibleLines(AutomationElement root)
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
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var bounds = line.Current.BoundingRectangle;
            if (string.IsNullOrWhiteSpace(line.Current.Name) ||
                !double.IsFinite(bounds.Top) || bounds.Height <= 0 ||
                bounds.Bottom <= viewport.Top || bounds.Top >= viewport.Bottom)
                continue;

            var distance = Math.Abs(bounds.Top - highlightAnchor);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            currentElement = line;
            currentIndex = index;
        }

        if (currentElement is null) return null;
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
        var titleElements = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, TrackTitleId));
        for (var index = 0; index < titleElements.Count; index++)
        {
            var candidate = titleElements[index].Current.Name?.Trim();
            if (string.Equals(candidate, title.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static AutomationElement? GetRoot()
    {
        var process = Process.GetProcessesByName("AppleMusic")
            .FirstOrDefault(item => item.MainWindowHandle != IntPtr.Zero);
        return process is null ? null : AutomationElement.FromHandle(process.MainWindowHandle);
    }
}
