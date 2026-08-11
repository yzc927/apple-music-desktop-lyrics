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
        var settings = LoadSettings();
        Enabled = settings.FollowAppleMusic;
        StartupEnabled = StartupSettingsMigration.Resolve(
            settings.StartWithWindows, HasStartupRegistration());
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => Evaluate(), Dispatcher.CurrentDispatcher);
        UpdateStartupRegistration();
        if (settings.StartWithWindows is null) SaveSettings();
    }

    public bool Enabled { get; private set; }
    public bool StartupEnabled { get; private set; }
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
        SaveSettings();
        _lastRunning = null;
        if (Enabled) Evaluate(force: true);
    }

    public bool SetStartupEnabled(bool enabled)
    {
        if (StartupEnabled == enabled) return true;
        var previous = StartupEnabled;
        StartupEnabled = enabled;
        if (!UpdateStartupRegistration())
        {
            StartupEnabled = previous;
            return false;
        }
        SaveSettings();
        return true;
    }

    private void Evaluate(bool force = false)
    {
        if (!Enabled) return;
        var running = IsAppleMusicRunning();
        if (!force && _lastRunning == running) return;
        _lastRunning = running;
        RunningChanged?.Invoke(running);
    }

    private static FollowSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new FollowSettings();
            return JsonSerializer.Deserialize<FollowSettings>(File.ReadAllText(SettingsPath))
                ?? new FollowSettings();
        }
        catch { return new FollowSettings(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                new FollowSettings(Enabled, StartupEnabled)));
            File.Move(temporary, SettingsPath, true);
        }
        catch { }
    }

    private static bool HasStartupRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(StartupValueName) is string value &&
                !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    private bool UpdateStartupRegistration()
    {
        StartupRegistrationError = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) throw new InvalidOperationException("无法打开 Windows 启动项。");
            if (!StartupEnabled)
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(StartupValueName, $"\"{executable}\" --background");
            return true;
        }
        catch (Exception error)
        {
            StartupRegistrationError = error.Message;
            return false;
        }
    }

    public void Dispose() => _timer.Stop();

    private sealed record FollowSettings(
        bool FollowAppleMusic = true,
        bool? StartWithWindows = null);
}
