using System.Diagnostics;
using System.Windows.Automation;

namespace AppleMusicDesktopLyrics;

internal sealed class AppleMusicUiPlaybackController
{
    private const string ScrubberAutomationId = "LCDScrubber";

    public bool TrySeek(TimeSpan position)
    {
        var processes = Process.GetProcessesByName("AppleMusic");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    var root = AutomationElement.FromHandle(process.MainWindowHandle);
                    var scrubber = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty,
                            ScrubberAutomationId));
                    if (scrubber?.TryGetCurrentPattern(RangeValuePattern.Pattern, out var raw) != true ||
                        raw is not RangeValuePattern range || range.Current.IsReadOnly) continue;
                    var target = Math.Clamp(position.TotalSeconds,
                        range.Current.Minimum, range.Current.Maximum);
                    range.SetValue(target);
                    Thread.Sleep(80);
                    if (Math.Abs(range.Current.Value - target) <= 1.25) return true;
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
                catch (System.Runtime.InteropServices.COMException) { }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return false;
    }
}
