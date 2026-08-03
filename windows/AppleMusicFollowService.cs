using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AppleMusicDesktopLyrics;

internal sealed class AppleMusicFollowService : IDisposable
{
    private const string StartupValueName = "AppleMusicDesktopLyrics";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "app-settings.json");

    private readonly DispatcherTimer _timer;
    private bool? _lastRunning;

    public AppleMusicFollowService()
    {
        Enabled = LoadEnabled();
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => Evaluate(), Dispatcher.CurrentDispatcher);
        UpdateStartupRegistration();
    }

    public bool Enabled { get; private set; }
    public string? StartupRegistrationError { get; private set; }
    public event Action<bool>? RunningChanged;

    public bool IsAppleMusicRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("AppleMusic");
            try { return processes.Any(process => !process.HasExited); }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch { return false; }
    }

    public void Start()
    {
        Evaluate(force: true);
        _timer.Start();
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        SaveEnabled();
        UpdateStartupRegistration();
        _lastRunning = null;
        if (Enabled) Evaluate(force: true);
    }

    private void Evaluate(bool force = false)
    {
        if (!Enabled) return;
        var running = IsAppleMusicRunning();
        if (!force && _lastRunning == running) return;
        _lastRunning = running;
        RunningChanged?.Invoke(running);
    }

    private static bool LoadEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return true;
            var settings = JsonSerializer.Deserialize<FollowSettings>(
                File.ReadAllText(SettingsPath));
            return settings?.FollowAppleMusic ?? true;
        }
        catch { return true; }
    }

    private void SaveEnabled()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                new FollowSettings(Enabled)));
            File.Move(temporary, SettingsPath, true);
        }
        catch { }
    }

    private void UpdateStartupRegistration()
    {
        StartupRegistrationError = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) throw new InvalidOperationException("无法打开 Windows 启动项。");
            if (!Enabled)
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(StartupValueName, $"\"{executable}\" --background");
        }
        catch (Exception error)
        {
            StartupRegistrationError = error.Message;
        }
    }

    public void Dispose() => _timer.Stop();

    private sealed record FollowSettings(bool FollowAppleMusic = true);
}
