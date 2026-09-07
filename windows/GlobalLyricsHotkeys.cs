using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AppleMusicDesktopLyrics;

internal sealed class GlobalLyricsHotkeys : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    private readonly HwndSource _source;
    private readonly Dictionary<string, Action> _actions;
    private readonly Dictionary<int, Action> _registered = new();
    private bool _disposed;
    internal string Status { get; private set; } = "";
    internal GlobalLyricsHotkeys(Dictionary<string, Action> actions)
    {
        _actions = actions;
        _source = new HwndSource(new HwndSourceParameters("Lyrics global shortcuts")
        { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _source.AddHook(Hook);
        Apply();
    }
    internal void Apply()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        var errors = new List<string>();
        var used = new HashSet<(uint, uint)>();
        var nextId = 0x5100;
        foreach (var (name, action) in _actions)
        {
            if (!LyricsPreferences.Current.Hotkeys.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(text)!;
                var modifiers = (uint)gesture.Modifiers;
                var key = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
                if (key == 0 || (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
                    throw new ArgumentException();
                if (!used.Add((modifiers, key))) { errors.Add($"{name}：与另一个动作重复"); continue; }
                var id = nextId++;
                if (RegisterHotKey(_source.Handle, id, modifiers | 0x4000, key)) _registered[id] = action;
                else errors.Add($"{name}：{text} 被系统或其他应用占用（错误 {Marshal.GetLastWin32Error()}）");
            }
            catch { errors.Add($"{name}：格式无效，请使用 Ctrl/Alt/Win 加按键"); }
        }
        Status = errors.Count == 0 ? $"已启用 {_registered.Count} 个快捷键。留空可禁用。" : string.Join("\n", errors);
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _registered.Keys) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
