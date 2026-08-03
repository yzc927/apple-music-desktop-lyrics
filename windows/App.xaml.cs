using System.Windows;
using Forms = System.Windows.Forms;

namespace AppleMusicDesktopLyrics;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _window;
    private ManagementWindow? _management;
    private Forms.NotifyIcon? _tray;
    private AppleMusicFollowService? _followService;
    private Forms.ToolStripMenuItem? _followItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _window = new OverlayWindow();
        _followService = new AppleMusicFollowService();
        _followService.RunningChanged += ApplyAppleMusicRunningState;
        if (!_followService.Enabled) _window.Show();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开管理界面", null, (_, _) => Dispatcher.Invoke(ShowManagement));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(() =>
        {
            if (_window.IsVisible) _window.HideToTray(); else _window.ShowFromTray();
        }));
        _followItem = new Forms.ToolStripMenuItem("跟随 Apple Music 自动显示 / 隐藏")
        {
            CheckOnClick = false,
            Checked = _followService.Enabled
        };
        _followItem.Click += (_, _) => Dispatcher.Invoke(ToggleFollowAppleMusic);
        menu.Items.Add(_followItem);
        var lockItem = new Forms.ToolStripMenuItem("锁定位置和大小") { CheckOnClick = false };
        lockItem.Click += (_, _) => Dispatcher.Invoke(() => _window.ToggleLock());
        menu.Items.Add(lockItem);
        var topmostItem = new Forms.ToolStripMenuItem("固定到最前端") { CheckOnClick = false };
        topmostItem.Click += (_, _) => Dispatcher.Invoke(() => _window.ToggleTopmost());
        menu.Items.Add(topmostItem);
        menu.Items.Add("选择歌词颜色…", null, (_, _) => Dispatcher.Invoke(() => _window.ChooseHighlightColor()));
        var autoColorItem = new Forms.ToolStripMenuItem("按歌手自动配色") { CheckOnClick = false };
        autoColorItem.Click += (_, _) => Dispatcher.Invoke(() => _window.ToggleAutoColor());
        menu.Items.Add(autoColorItem);
        var timingMenu = new Forms.ToolStripMenuItem("歌词时间调整");
        timingMenu.DropDownItems.Add("慢 0.5 秒", null, (_, _) => Dispatcher.Invoke(() => _window.AdjustLyrics(-0.5)));
        timingMenu.DropDownItems.Add("快 0.5 秒", null, (_, _) => Dispatcher.Invoke(() => _window.AdjustLyrics(0.5)));
        timingMenu.DropDownItems.Add("偏移归零", null, (_, _) => Dispatcher.Invoke(() => _window.ResetLyricsOffset()));
        menu.Items.Add(timingMenu);
        var clickThroughItem = new Forms.ToolStripMenuItem("鼠标穿透") { CheckOnClick = false };
        clickThroughItem.Click += (_, _) => Dispatcher.Invoke(() => _window.ToggleClickThrough());
        menu.Items.Add(clickThroughItem);
        menu.Items.Add("重新获取歌词", null, (_, _) => Dispatcher.Invoke(() => _window.RefreshLyrics()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _window.RequestExit();
            Shutdown();
        }));
        menu.Opening += (_, _) => Dispatcher.Invoke(() =>
        {
            lockItem.Checked = _window.IsLocked;
            topmostItem.Checked = _window.IsPinned;
            autoColorItem.Checked = _window.IsAutoColor;
            clickThroughItem.Checked = _window.IsClickThrough;
            if (_followItem is not null) _followItem.Checked = _followService?.Enabled == true;
        });

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "Apple Music 桌面歌词",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_window.IsVisible) _window.HideToTray(); else _window.ShowFromTray();
        });
        _followService.Start();
        if (e.Args.Any(argument => string.Equals(
                argument, "--management", StringComparison.OrdinalIgnoreCase)))
            ShowManagement();
    }

    private void ToggleFollowAppleMusic()
    {
        if (_window is null || _followService is null) return;
        _followService.SetEnabled(!_followService.Enabled);
        if (_followItem is not null) _followItem.Checked = _followService.Enabled;
        if (_followService.Enabled)
        {
            ApplyAppleMusicRunningState(_followService.IsAppleMusicRunning());
            if (_followService.StartupRegistrationError is { Length: > 0 } error)
            {
                System.Windows.MessageBox.Show(
                    "跟随功能已开启，但无法加入 Windows 登录启动项：\n" + error,
                    "Apple Music 桌面歌词");
            }
        }
        else
        {
            _window.ShowFromTray();
        }
    }

    private void ApplyAppleMusicRunningState(bool running)
    {
        if (_window is null || _followService?.Enabled != true) return;
        if (running)
        {
            _window.ShowFromTray();
        }
        else
        {
            _management?.Hide();
            _window.HideToTray();
        }
    }

    private void ShowManagement()
    {
        if (_window is null) return;
        if (_management is null)
        {
            _management = new ManagementWindow(_window);
            _management.Closed += (_, _) => _management = null;
        }
        if (!_management.IsVisible) _management.Show();
        if (_management.WindowState == WindowState.Minimized)
            _management.WindowState = WindowState.Normal;
        _management.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _followService?.Dispose();
        _tray?.Dispose();
        _window?.Dispose();
        base.OnExit(e);
    }
}
