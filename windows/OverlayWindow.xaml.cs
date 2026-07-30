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

namespace AppleMusicDesktopLyrics;

public partial class OverlayWindow : Window, IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HtTransparent = new(-1);
    private static readonly IntPtr HtClient = new(1);
    private readonly MediaLyricsController _controller;
    private HwndSource? _hwndSource;
    private bool _clickThrough;
    private bool _locked;
    private bool _allowClose;
    private double _highlightProgress;
    private System.Windows.Media.Color _highlightColor = System.Windows.Media.Color.FromRgb(255, 59, 48);
    private bool _autoColor;
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

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) =>
        {
            UpdateTypography();
            UpdateHighlightClip();
        };
        LoadSettings();
        _controller = new MediaLyricsController(SetLines, ShowToast);
        _controller.Start();
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
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WindowMessageHook);
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
        CurrentLine.Text = current;
        CurrentHighlightLine.Text = current;
        NextLine.Text = next;
        _highlightProgress = Math.Clamp(progress, 0, 1);
        UpdateHighlightClip();
    }

    private void UpdateHighlightClip()
    {
        if (!IsLoaded || CurrentHighlightLine.ActualWidth <= 0) return;
        var typeface = new Typeface(CurrentHighlightLine.FontFamily,
            CurrentHighlightLine.FontStyle, CurrentHighlightLine.FontWeight,
            CurrentHighlightLine.FontStretch);
        var formatted = new FormattedText(CurrentHighlightLine.Text, CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, typeface, CurrentHighlightLine.FontSize,
            CurrentHighlightLine.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var textWidth = Math.Min(formatted.WidthIncludingTrailingWhitespace,
            CurrentHighlightLine.ActualWidth);
        var start = Math.Max(0, (CurrentHighlightLine.ActualWidth - textWidth) / 2);
        CurrentHighlightLine.Clip = new RectangleGeometry(new Rect(start, 0,
            textWidth * _highlightProgress, Math.Max(1, CurrentHighlightLine.ActualHeight)));
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
        Toolbar.Visibility = _locked ? Visibility.Visible : Visibility.Collapsed;
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
        _locked = !_locked;
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
            Toolbar.Visibility = Visibility.Visible;
            Surface.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        }
        _controller.ShowTransient(_locked ? "歌词窗口已锁定" : "歌词窗口已解锁");
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest || !_locked) return IntPtr.Zero;

        var packed = lParam.ToInt64();
        var screenX = unchecked((short)(packed & 0xFFFF));
        var screenY = unchecked((short)((packed >> 16) & 0xFFFF));
        var windowPoint = PointFromScreen(new System.Windows.Point(screenX, screenY));
        var lockOrigin = LockButton.TranslatePoint(new System.Windows.Point(0, 0), this);
        var lockHitArea = new Rect(lockOrigin.X - 4, lockOrigin.Y - 4,
            LockButton.ActualWidth + 8, LockButton.ActualHeight + 8);

        handled = true;
        return lockHitArea.Contains(windowPoint) ? HtClient : HtTransparent;
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

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(_settingsPath));
            if (settings?.HighlightColor is not { Length: > 0 }) return;
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(settings.HighlightColor);
            if (converted is not System.Windows.Media.Color color) return;
            _highlightColor = color;
            _autoColor = settings.AutoColor;
            CurrentHighlightLine.Foreground = new SolidColorBrush(color);
            ColorButton.Foreground = new SolidColorBrush(color);
            AutoColorButton.Foreground = _autoColor
                ? new SolidColorBrush(color)
                : System.Windows.Media.Brushes.White;
            if (settings.Width is > 0 && settings.Height is > 0 &&
                settings.Left is { } left && settings.Top is { } top)
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
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(
                new OverlaySettings(_highlightColor.ToString(), _autoColor,
                    IsLoaded ? Left : null, IsLoaded ? Top : null,
                    IsLoaded ? ActualWidth : null, IsLoaded ? ActualHeight : null)));
        }
        catch { }
    }

    private sealed record OverlaySettings(string HighlightColor, bool AutoColor = false,
        double? Left = null, double? Top = null, double? Width = null, double? Height = null);

    public void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        ApplyExtendedStyles();
        _controller.ShowTransient(_clickThrough ? "已开启鼠标穿透（从托盘菜单关闭）" : "已关闭鼠标穿透");
    }

    public void RefreshLyrics() => _controller.RefreshLyrics();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        HideToTray();
    }

    public void HideToTray()
    {
        SaveSettings();
        Hide();
    }

    public void RequestExit()
    {
        SaveSettings();
        _allowClose = true;
        Close();
    }

    private void ApplyExtendedStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow;
        style = _clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    public void Dispose()
    {
        _hwndSource?.RemoveHook(WindowMessageHook);
        _controller.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}
