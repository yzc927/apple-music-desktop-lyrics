using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AppleMusicDesktopLyrics;

public partial class OverlayWindow : Window, IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private readonly MediaLyricsController _controller;
    private readonly DispatcherTimer _lockedHoverTimer;
    private readonly DispatcherTimer _placementSaveTimer;
    private UnlockWindow? _unlockWindow;
    private DateTimeOffset? _lockedHoverStartedAt;
    private bool _unlockRequestedVisible;
    private double _unlockLeft;
    private double _unlockTop;
    private bool _clickThrough;
    private bool _locked;
    private bool _allowClose;
    private double _highlightProgress;
    private double _highlightTextStart;
    private double _highlightTextWidth;
    private readonly RectangleGeometry _highlightClip = new();
    private System.Windows.Media.Color _highlightColor = System.Windows.Media.Color.FromRgb(255, 59, 48);
    private bool _autoColor;
    private string _fontFamily = "Microsoft YaHei UI";
    private string _lastArtist = "";
    private bool _hasSavedPlacement;
    private static readonly (string Name, string Hex)[] ColorPalette =
    [
        ("珊瑚红", "#FFFF453A"), ("暖橙", "#FFFF9F0A"),
        ("明黄", "#FFFFD60A"), ("薄荷绿", "#FF30D158"),
        ("湖蓝", "#FF64D2FF"), ("晴空蓝", "#FF0A84FF"),
        ("薰衣草紫", "#FFBF5AF2"), ("樱花粉", "#FFFF375F")
    ];
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppleMusicDesktopLyrics", "settings.json");

    public bool IsLocked => _locked;
    public bool IsPinned => Topmost;
    public bool IsAutoColor => _autoColor;
    public string CurrentArtist => _lastArtist;
    public double CurrentOffsetSeconds => _controller.OffsetSeconds;
    public string CurrentFontFamily => _fontFamily;
    public IReadOnlyList<FontChoice> AvailableFonts => GetAvailableFonts();

    public OverlayWindow()
    {
        InitializeComponent();
        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _placementSaveTimer.Tick += (_, _) =>
        {
            _placementSaveTimer.Stop();
            SaveSettings();
        };
        _lockedHoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background, (_, _) => TrackLockedHover(), Dispatcher);
        CurrentHighlightLine.Clip = _highlightClip;
        Loaded += OnLoaded;
        LocationChanged += (_, _) => SchedulePlacementSave();
        SizeChanged += (_, _) =>
        {
            UpdateTypography();
            UpdateHighlightMetrics();
            UpdateHighlightClip();
            SchedulePlacementSave();
        };
        LoadSettings();
        _controller = new MediaLyricsController(SetLines, ShowToast);
        _controller.Start();
    }

    private void SchedulePlacementSave()
    {
        if (!IsLoaded) return;
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_hasSavedPlacement)
        {
            Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2 + SystemParameters.WorkArea.Left;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 72;
        }
        else
        {
            // Keep at least a usable part of the overlay visible if monitor layout changed.
            Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120);
            Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 70);
        }
        ApplyExtendedStyles();
        UpdateTypography();
    }

    private void UpdateTypography()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var widthRatio = ActualWidth / 920d;
        var heightRatio = ActualHeight / 150d;
        var scale = Math.Clamp(Math.Sqrt(widthRatio * heightRatio), 0.58, 2.5);
        CurrentLine.FontSize = 30 * scale;
        CurrentHighlightLine.FontSize = 30 * scale;
        NextLine.FontSize = 19 * scale;
    }

    private void SetLines(string current, string next, double progress, string artist)
    {
        if (!string.Equals(_lastArtist, artist, StringComparison.Ordinal))
        {
            _lastArtist = artist;
            if (_autoColor) ApplyAutomaticColor(artist);
        }
        if (!string.Equals(CurrentLine.Text, current, StringComparison.Ordinal))
        {
            CurrentLine.Text = current;
            CurrentHighlightLine.Text = current;
            UpdateHighlightMetrics();
        }
        if (!string.Equals(NextLine.Text, next, StringComparison.Ordinal))
            NextLine.Text = next;
        _highlightProgress = Math.Clamp(progress, 0, 1);
        UpdateHighlightClip();
    }

    private void UpdateHighlightMetrics()
    {
        if (!IsLoaded || CurrentHighlightLine.ActualWidth <= 0) return;
        var typeface = new Typeface(CurrentHighlightLine.FontFamily,
            CurrentHighlightLine.FontStyle, CurrentHighlightLine.FontWeight,
            CurrentHighlightLine.FontStretch);
        var formatted = new FormattedText(CurrentHighlightLine.Text, CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, typeface, CurrentHighlightLine.FontSize,
            CurrentHighlightLine.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _highlightTextWidth = Math.Min(formatted.WidthIncludingTrailingWhitespace,
            CurrentHighlightLine.ActualWidth);
        _highlightTextStart = Math.Max(0, (CurrentHighlightLine.ActualWidth - _highlightTextWidth) / 2);
    }

    private void UpdateHighlightClip()
    {
        if (!IsLoaded || CurrentHighlightLine.ActualWidth <= 0) return;
        _highlightClip.Rect = new Rect(_highlightTextStart, 0,
            _highlightTextWidth * _highlightProgress, Math.Max(1, CurrentHighlightLine.ActualHeight));
    }

    private void ShowToast(string message)
    {
        Toast.BeginAnimation(OpacityProperty, null);
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 0;

        var storyboard = new Storyboard();
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        Storyboard.SetTarget(fadeIn, Toast);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(360))
        {
            BeginTime = TimeSpan.FromMilliseconds(1180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fadeOut, Toast);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeOut);
        storyboard.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_locked && !_clickThrough && e.ButtonState == MouseButtonState.Pressed &&
            !Toolbar.IsMouseOver && !Grip.IsMouseOver) DragMove();
    }

    private void Surface_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Toolbar.Visibility = Visibility.Visible;
        Surface.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            _locked ? (byte)1 : (byte)64, 0, 0, 0));
    }

    private void Surface_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Toolbar.Visibility = Visibility.Collapsed;
        Surface.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
    }

    private void LockButton_Click(object sender, RoutedEventArgs e) => ToggleLock();

    private void PinButton_Click(object sender, RoutedEventArgs e) => ToggleTopmost();

    private void ColorButton_Click(object sender, RoutedEventArgs e) => ChooseHighlightColor();

    private void AutoColorButton_Click(object sender, RoutedEventArgs e) => ToggleAutoColor();

    private void SlowButton_Click(object sender, RoutedEventArgs e) => AdjustLyrics(-0.5);

    private void FastButton_Click(object sender, RoutedEventArgs e) => AdjustLyrics(0.5);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    public void ToggleLock()
    {
        var locking = !_locked;
        var unlockOrigin = locking
            ? LockButton.TranslatePoint(new System.Windows.Point(-4, -4), this)
            : new System.Windows.Point();
        _locked = locking;
        ResizeMode = _locked ? ResizeMode.NoResize : ResizeMode.CanResize;
        Grip.Visibility = _locked ? Visibility.Collapsed : Visibility.Visible;
        var unlockedVisibility = _locked ? Visibility.Collapsed : Visibility.Visible;
        SlowButton.Visibility = unlockedVisibility;
        FastButton.Visibility = unlockedVisibility;
        PinButton.Visibility = unlockedVisibility;
        ColorButton.Visibility = unlockedVisibility;
        AutoColorButton.Visibility = unlockedVisibility;
        CloseButton.Visibility = unlockedVisibility;
        LockIcon.Data = Geometry.Parse(_locked
            ? "M7,11 V7 C7,3.7 9.2,1 12,1 C14.8,1 17,3.7 17,7 V11 M5,11 H19 V22 H5 Z M12,15 V18"
            : "M8,11 V7 C8,3.7 10.7,1 14,1 C17.3,1 20,3.7 20,7 M5,11 H19 V22 H5 Z M12,15 V18");
        LockButton.Foreground = System.Windows.Media.Brushes.White;
        LockButton.ToolTip = _locked ? "解锁位置和大小" : "锁定位置和大小";
        if (_locked)
        {
            Toolbar.Visibility = Visibility.Collapsed;
            Surface.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
            ApplyExtendedStyles();
            PrepareUnlockWindow(Left + unlockOrigin.X, Top + unlockOrigin.Y);
            _lockedHoverStartedAt = null;
            _unlockRequestedVisible = false;
            _lockedHoverTimer.Start();
        }
        else
        {
            _lockedHoverTimer.Stop();
            _lockedHoverStartedAt = null;
            _unlockRequestedVisible = false;
            _unlockWindow?.HideImmediately();
            ApplyExtendedStyles();
            Toolbar.Visibility = Visibility.Visible;
        }
        _controller.ShowTransient(_locked ? "歌词窗口已锁定" : "歌词窗口已解锁");
    }

    private void PrepareUnlockWindow(double left, double top)
    {
        _unlockWindow ??= new UnlockWindow(ToggleLock);
        _unlockLeft = left;
        _unlockTop = top;
        _unlockWindow.Left = _unlockLeft;
        _unlockWindow.Top = _unlockTop;
        _unlockWindow.Topmost = Topmost;
    }

    private void TrackLockedHover()
    {
        if (!_locked || !IsVisible) return;
        var cursor = System.Windows.Forms.Cursor.Position;
        var local = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        var isInside = new Rect(0, 0, ActualWidth, ActualHeight).Contains(local);
        if (!isInside)
        {
            _lockedHoverStartedAt = null;
            if (_unlockRequestedVisible)
            {
                _unlockRequestedVisible = false;
                _unlockWindow?.HideWithFade();
            }
            return;
        }

        _lockedHoverStartedAt ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - _lockedHoverStartedAt < TimeSpan.FromSeconds(1)) return;
        if (!_unlockRequestedVisible)
        {
            _unlockRequestedVisible = true;
            PrepareUnlockWindow(_unlockLeft, _unlockTop);
            _unlockWindow!.ShowWithFade();
        }
    }

    public void ToggleTopmost()
    {
        Topmost = !Topmost;
        PinSlash.Visibility = Topmost ? Visibility.Collapsed : Visibility.Visible;
        PinButton.Foreground = System.Windows.Media.Brushes.White;
        PinButton.ToolTip = Topmost ? "取消始终置顶" : "固定到最前端";
        _controller.ShowTransient(Topmost ? "已固定到最前端" : "已取消置顶");
    }

    public void ChooseHighlightColor()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false
        };
        foreach (var (name, hex) in ColorPalette)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var panel = new System.Windows.Controls.StackPanel
                { Orientation = System.Windows.Controls.Orientation.Horizontal };
            panel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 13, Height = 13, Margin = new Thickness(0, 0, 8, 0),
                Fill = new SolidColorBrush(color)
            });
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = name });
            var item = new System.Windows.Controls.MenuItem { Header = panel };
            item.Click += (_, _) => SetHighlightColor(color, false);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SetHighlightColor(System.Windows.Media.Color color, bool automatic)
    {
        _highlightColor = color;
        _autoColor = automatic;
        ApplyHighlightBrush(new SolidColorBrush(_highlightColor), _highlightColor, automatic);
    }

    private void ApplyHighlightBrush(System.Windows.Media.Brush brush, System.Windows.Media.Color representative, bool automatic)
    {
        _highlightColor = representative;
        _autoColor = automatic;
        CurrentHighlightLine.Foreground = brush;
        ColorButton.Foreground = new SolidColorBrush(representative);
        AutoColorButton.Foreground = automatic
            ? new SolidColorBrush(representative)
            : System.Windows.Media.Brushes.White;
        SaveSettings();
        UpdateHighlightClip();
    }

    public void ToggleAutoColor()
    {
        _autoColor = !_autoColor;
        if (_autoColor) ApplyAutomaticColor(_lastArtist);
        else
        {
            AutoColorButton.Foreground = System.Windows.Media.Brushes.White;
            SaveSettings();
        }
        _controller.ShowTransient(_autoColor ? "已开启按歌手自动配色" : "已关闭自动配色");
    }

    private void ApplyAutomaticColor(string artist)
    {
        var palette = ArtistColorEngine.Resolve(artist);
        var colors = palette.Colors.Select(hex =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)).ToArray();
        if (colors.Length == 1)
        {
            ApplyHighlightBrush(new SolidColorBrush(colors[0]), colors[0], true);
            return;
        }

        var gradient = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0.5),
            EndPoint = new System.Windows.Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        for (var i = 0; i < colors.Length; i++)
            gradient.GradientStops.Add(new GradientStop(colors[i], i / (double)(colors.Length - 1)));
        ApplyHighlightBrush(gradient, colors[0], true);
    }

    public void AdjustLyrics(double seconds) => _controller.AdjustOffset(seconds);

    public void ResetLyricsOffset() => _controller.ResetOffset();

    public void SetFontFamily(string familyName)
    {
        if (!GetAvailableFonts().Any(item =>
                string.Equals(item.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))) return;
        _fontFamily = familyName;
        ApplyFontFamily();
        SaveSettings();
        _controller.ShowTransient($"已切换字体：{GetAvailableFonts().First(item =>
            string.Equals(item.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).DisplayName}");
    }

    private void ApplyFontFamily()
    {
        var family = new System.Windows.Media.FontFamily(_fontFamily);
        CurrentLine.FontFamily = family;
        CurrentHighlightLine.FontFamily = family;
        NextLine.FontFamily = family;
        UpdateHighlightMetrics();
        UpdateHighlightClip();
    }

    private static IReadOnlyList<FontChoice> GetAvailableFonts()
    {
        var installed = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        FontChoice[] curated =
        [
            new("默认（微软雅黑 UI）", "Microsoft YaHei UI"),
            new("微软雅黑", "Microsoft YaHei"),
            new("等线", "DengXian"),
            new("Segoe UI", "Segoe UI"),
            new("游ゴシック UI", "Yu Gothic UI"),
            new("メイリオ", "Meiryo"),
            new("思源黑体", "Source Han Sans SC"),
            new("Noto Sans CJK", "Noto Sans CJK SC"),
            new("霞鹜文楷", "LXGW WenKai")
        ];
        return curated.Where(item => installed.Contains(item.FamilyName)).ToArray();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(_settingsPath));
            if (settings is null) return;

            _autoColor = settings.AutoColor;
            if (settings.HighlightColor is { Length: > 0 } highlightColor)
            {
                try
                {
                    var converted = System.Windows.Media.ColorConverter.ConvertFromString(highlightColor);
                    if (converted is System.Windows.Media.Color color)
                        _highlightColor = color;
                }
                catch { /* Keep the default color, but continue restoring placement. */ }
            }
            if (!string.IsNullOrWhiteSpace(settings.FontFamily) && GetAvailableFonts().Any(item =>
                    string.Equals(item.FamilyName, settings.FontFamily, StringComparison.OrdinalIgnoreCase)))
                _fontFamily = settings.FontFamily;
            ApplyFontFamily();
            CurrentHighlightLine.Foreground = new SolidColorBrush(_highlightColor);
            ColorButton.Foreground = new SolidColorBrush(_highlightColor);
            AutoColorButton.Foreground = _autoColor
                ? new SolidColorBrush(_highlightColor)
                : System.Windows.Media.Brushes.White;
            if (settings.Width is > 0 && settings.Height is > 0 &&
                settings.Left is { } left && settings.Top is { } top &&
                double.IsFinite(settings.Width.Value) && double.IsFinite(settings.Height.Value) &&
                double.IsFinite(left) && double.IsFinite(top))
            {
                Width = Math.Clamp(settings.Width.Value, MinWidth, 3000);
                Height = Math.Clamp(settings.Height.Value, MinHeight, 1800);
                Left = left;
                Top = top;
                _hasSavedPlacement = true;
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        // Startup failures or a very early shutdown must never erase a placement that was
        // successfully read from disk. Hidden windows remain loaded and are still saved.
        if (!IsLoaded) return;
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                new OverlaySettings(_highlightColor.ToString(), _autoColor,
                    IsLoaded ? Left : null, IsLoaded ? Top : null,
                    IsLoaded ? ActualWidth : null, IsLoaded ? ActualHeight : null,
                    _fontFamily));
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, true);
        }
        catch { }
    }

    private sealed record OverlaySettings(string HighlightColor, bool AutoColor = false,
        double? Left = null, double? Top = null, double? Width = null, double? Height = null,
        string? FontFamily = null);

    public void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        ApplyExtendedStyles();
        _controller.ShowTransient(_clickThrough ? "已开启鼠标穿透（从托盘菜单关闭）" : "已关闭鼠标穿透");
    }

    public void RefreshLyrics() => _controller.RefreshLyrics();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        if (_allowClose) return;
        e.Cancel = true;
        HideToTray();
    }

    public void HideToTray()
    {
        SaveSettings();
        _lockedHoverTimer.Stop();
        _lockedHoverStartedAt = null;
        _unlockRequestedVisible = false;
        _unlockWindow?.HideImmediately();
        Hide();
    }

    public void ShowFromTray()
    {
        Show();
        if (_locked)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var origin = LockButton.TranslatePoint(new System.Windows.Point(-4, -4), this);
                PrepareUnlockWindow(Left + origin.X, Top + origin.Y);
                _lockedHoverStartedAt = null;
                _unlockRequestedVisible = false;
                _lockedHoverTimer.Start();
            });
        }
    }

    public void RequestExit()
    {
        SaveSettings();
        _unlockWindow?.Close();
        _allowClose = true;
        Close();
    }

    private void ApplyExtendedStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow;
        style = (_clickThrough || _locked) ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    public void Dispose()
    {
        SaveSettings();
        _placementSaveTimer.Stop();
        _lockedHoverTimer.Stop();
        _unlockWindow?.Close();
        _controller.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}

public sealed record FontChoice(string DisplayName, string FamilyName);
