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
    private readonly Dictionary<(uint Modifiers, uint Key), int> _gestures = new();
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
        => TryApply(LyricsPreferences.Current.Hotkeys);

    // Reserve new gestures while old registrations stay active. Reuse existing
    // gestures (including swaps); persist only after every reservation succeeds.
    internal bool TryApply(IReadOnlyDictionary<string, string> proposal, Action? persist = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlobalLyricsHotkeys));
        var errors = new List<string>();
        var desired = new Dictionary<(uint, uint), (string Name, Action Action)>();
        foreach (var (name, action) in _actions)
        {
            if (!proposal.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(text)!;
                var modifiers = (uint)gesture.Modifiers;
                var key = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
                if (key == 0 || (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
                    throw new ArgumentException();
                if (!desired.TryAdd((modifiers, key), (name, action))) errors.Add($"{name}：与另一个动作重复");
            }
            catch { errors.Add($"{name}：格式无效，请使用 Ctrl/Alt/Win 加按键"); }
        }
        var reserved = new Dictionary<(uint, uint), int>();
        if (errors.Count == 0)
        {
            foreach (var (gesture, binding) in desired)
            {
                if (_gestures.ContainsKey(gesture)) continue;
                // IDs are local to this message window, not global atoms.
                var id = Enumerable.Range(0x5100, 0x1000).First(candidate =>
                    !_registered.ContainsKey(candidate) && !reserved.ContainsValue(candidate));
                if (RegisterHotKey(_source.Handle, id, gesture.Item1 | 0x4000, gesture.Item2)) reserved[gesture] = id;
                else { errors.Add($"{binding.Name}：被系统或其他应用占用（错误 {Marshal.GetLastWin32Error()}）"); break; }
            }
        }
        if (errors.Count == 0)
        {
            try { persist?.Invoke(); }
            catch (Exception ex) { errors.Add("保存失败：" + ex.Message); }
        }
        if (errors.Count > 0)
        {
            foreach (var id in reserved.Values) UnregisterHotKey(_source.Handle, id);
            Status = string.Join("\n", errors) + "\n未应用更改，旧快捷键方案保持不变。";
            return false;
        }
        foreach (var (gesture, id) in _gestures.ToArray())
            if (!desired.ContainsKey(gesture)) { UnregisterHotKey(_source.Handle, id); _registered.Remove(id); _gestures.Remove(gesture); }
        foreach (var (gesture, binding) in desired)
        {
            if (!_gestures.TryGetValue(gesture, out var id)) _gestures[gesture] = id = reserved[gesture];
            _registered[id] = binding.Action;
        }
        Status = $"已启用 {_registered.Count} 个快捷键。留空可禁用。";
        return true;
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
