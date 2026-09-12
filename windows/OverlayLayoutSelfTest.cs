using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AppleMusicDesktopLyrics;

public partial class OverlayWindow
{
    internal static void RunLayoutSelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lyrics-layout-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        void Require(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
        }
        try
        {
            SettingsPersistence.Save(path, new OverlaySettings("#FFFF453A", Width: 1485.714,
                Height: 390.286, Left: 78, Top: 100, FontFamily: "Microsoft YaHei UI"));
            using (var window = new OverlayWindow(path, false))
            {
                Require(window._hasSavedPlacement, "Initialization failed to restore placement");
                Require(Math.Abs(window.Width - 1485.714) < 1, "Saved width lost before showing");
                Require(Math.Abs(window.Height - 390.286) < 1, "Saved height lost before showing");
                window.Show();
                foreach (var size in new[] { (360d, 100d), (1833d, 150d), (1486d, 390d) })
                {
                    window.Width = size.Item1;
                    window.Height = size.Item2;
                    window.SetLines("明日へ向かう長い歌詞を表示して確かめる", 
                        "これは次の行の非常に長い歌詞です ABCDEFGHIJKLMNOPQRSTUVWXYZ 明日へ向かう長い歌詞", 0.5, "");
                    window.UpdateLayout();
                    window.UpdateTypography();
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    var bottom = window.NextLine.TranslatePoint(new System.Windows.Point(0, window.NextLine.ActualHeight), window).Y;
                    Require(bottom <= window.ActualHeight - 2, "Next lyric clipped vertically");
                    Require(window.CurrentLine.TranslatePoint(new System.Windows.Point(), window).Y >= 0, "Current lyric clipped above");
                    var hit = false;
                    var point = window.PointToScreen(new System.Windows.Point(window.ActualWidth / 2, window.ActualHeight - 2));
                    var packed = ((long)(ushort)(short)point.Y << 16) | (ushort)(short)point.X;
                    var code = window.WindowMessageHook(IntPtr.Zero, WmNcHitTest, IntPtr.Zero, new IntPtr(packed), ref hit);
                    Require(hit && code.ToInt32() == 15, "Bottom resize hook missing");
                }
                window.Width = 1100;
                window.Height = 260;
                window.UpdateLayout();
                window.SaveSettings();
                window.RequestExit();
            }
            using (var restored = new OverlayWindow(path, false))
            {
                Require(Math.Abs(restored.Width - 1100) < 1 && Math.Abs(restored.Height - 260) < 1,
                    "Resize dimensions did not survive restart");
                restored.RequestExit();
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
