using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Text.RegularExpressions;
using MessageBox = System.Windows.MessageBox;
using WpfButton = System.Windows.Controls.Button;
using Forms = System.Windows.Forms;

namespace AppleMusicDesktopLyrics;

public partial class ManagementWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly AppleMusicFollowService _followService;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private bool _selectingFont;
    private bool _refreshingStartup = true;
    private bool _syncingArtistColors;
    private readonly List<string> _artistColors = [];
    private static readonly (string Name, string Hex)[] CommonColors =
    [
        ("珊瑚红", "#FF453A"), ("暖橙", "#FF9F0A"), ("明黄", "#FFD60A"),
        ("薄荷绿", "#30D158"), ("湖蓝", "#64D2FF"), ("晴空蓝", "#0A84FF"),
        ("薰衣草", "#BF5AF2"), ("樱花粉", "#FF375F"), ("青绿色", "#39A2A5"),
        ("石板蓝", "#5B87A5")
    ];

    internal ManagementWindow(OverlayWindow overlay, AppleMusicFollowService followService)
    {
        InitializeComponent();
        _overlay = overlay;
        _followService = followService;
        FontComboBox.ItemsSource = _overlay.AvailableFonts;
        FontComboBox.DisplayMemberPath = nameof(FontChoice.DisplayName);
        FontComboBox.SelectedValuePath = nameof(FontChoice.FamilyName);
        _selectingFont = true;
        FontComboBox.SelectedValue = _overlay.CurrentFontFamily;
        _selectingFont = false;
        _refreshTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(500), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => RefreshState(), Dispatcher);
        Loaded += (_, _) => _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
        Activated += (_, _) => RefreshState();
        BuildCommonArtistColors();
        BuildArtistList();
        var initial = ArtistColorEngine.Resolve(
            _overlay.CurrentArtist, _overlay.FallbackHighlightColor);
        SetArtistColors(initial.Colors);
        RefreshState();
    }

    private void RefreshState()
    {
        var artist = string.IsNullOrWhiteSpace(_overlay.CurrentArtist) ? "尚未读取到歌手" : _overlay.CurrentArtist;
        CurrentArtistText.Text = artist;
        GlobalHotkeyStatus.Text = _overlay.GlobalHotkeys?.Status ?? "快捷键尚未初始化";
        CurrentModeText.Text = _overlay.IsAutoColor ? "自动配色已开启；未收录歌手使用所选后备色" : "当前使用手动颜色";
        LyricsSourceText.Text = $"歌词来源：{_overlay.CurrentLyricsSource}";
        LyricsVersionText.Text = _overlay.LyricsCandidateCount > 0
            ? $"{_overlay.LyricsCandidateIndex + 1}/{_overlay.LyricsCandidateCount} · {_overlay.LyricsCandidateLabel}"
            : "当前没有可切换的 LRCLIB 版本";
        LyricsConfidenceText.Text = _overlay.LyricsCandidateCount > 0
            ? $"{_overlay.LyricsCandidateConfidence} · {_overlay.LyricsCandidateConfidenceReason}"
            : "匹配置信度：尚未评估";
        LyricsConfidenceText.Foreground = _overlay.LyricsCandidateConfidence switch
        {
            "高度匹配" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 145, 92)),
            "可能是其他版本" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 120, 24)),
            "低置信度" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 67, 67)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(111, 118, 132))
        };
        AutoModeButton.Content = _overlay.IsAutoColor ? "关闭自动配色" : "开启自动配色";
        AutoTimingButton.Content = _overlay.IsAutomaticLyricsCalibration
            ? "关闭 Apple 自动对时" : "开启 Apple 自动对时";
        KaraokeModeButton.Content = _overlay.IsKaraokeMode ? "卡拉 OK 模式" : "普通模式";
        KaraokeModeButton.ToolTip = _overlay.IsKaraokeMode
            ? "当前为卡拉 OK 扫色模式；点击切换为普通整句模式"
            : "当前为普通整句模式；点击切换为卡拉 OK 扫色模式";
        KaraokeModeDescription.Text = _overlay.IsKaraokeMode
            ? "当前按播放进度从左向右扫色。"
            : "当前整句从一开始就完整显示颜色。";
        _refreshingStartup = true;
        StartupCheckBox.IsChecked = _followService.StartupEnabled;
        _refreshingStartup = false;
        StartupStatusText.Text = _followService.StartupEnabled
            ? "已开启；下次登录 Windows 时会在托盘后台启动。"
            : "已关闭；需要手动运行程序。";
        OffsetText.Text = FormatOffset(_overlay.CurrentOffsetSeconds);
        LocalLyricsStatusText.Text = _overlay.HasLocalLyricsOverride
            ? "当前歌曲正在使用本地永久覆盖。"
            : _overlay.HasCachedLyrics ? "当前歌曲已有 LRCLIB 离线缓存。" : "当前歌曲尚无本地覆盖或缓存。";
        LineLoopButton.Content = _overlay.PracticeLoopStatus == "当前句循环"
            ? "关闭当前句循环" : "循环当前句";
        PlaybackRateButton.Content = $"速度 {_overlay.PlaybackRate:0.##}×";
        DifficultSegmentButton.Content = _overlay.CurrentSegmentIsDifficult
            ? "取消当前句难点" : "标记当前句为难点";
        PracticeStatusText.Text = $"{_overlay.PracticeLoopStatus} · " +
            $"本曲已记录 {_overlay.DifficultSegmentCount} 个难点";
        CurrentPalettePreview.Background = CreateBrush(
            ArtistColorEngine.Resolve(artist, _overlay.FallbackHighlightColor).Colors);
    }

    private void LyricsVersions_Click(object sender, RoutedEventArgs e) => LyricsInteractionWindow.Versions(_overlay, this);
    private void GlobalHotkeys_Click(object sender, RoutedEventArgs e) => LyricsInteractionWindow.Hotkeys(_overlay, this);
    private void PersonalReading_Click(object sender, RoutedEventArgs e) => LyricsInteractionWindow.Reading(_overlay, this);

    private void BuildArtistList()
    {
        ArtistList.Children.Clear();
        var index = 0;
        foreach (var palette in ArtistColorEngine.GetCuratedPalettes())
        {
            var row = new Grid { Height = 56, Background = index++ % 2 == 0 ? System.Windows.Media.Brushes.White :
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 240, 244)) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock
            {
                Text = palette.Identity, Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 76, 88))
            });
            var colorSummary = new Grid { Margin = new Thickness(8, 6, 14, 6) };
            colorSummary.RowDefinitions.Add(new RowDefinition());
            colorSummary.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            colorSummary.Children.Add(new TextBlock
            {
                Text = string.Join("  ", palette.Colors.Select(ToRgbHex)),
                FontSize = 10,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 111, 126)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            });
            var preview = new Border
            {
                Height = 15, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 2, 0, 0),
                Background = CreateBrush(palette.Colors)
            };
            Grid.SetRow(preview, 1);
            colorSummary.Children.Add(preview);
            Grid.SetColumn(colorSummary, 1);
            row.Children.Add(colorSummary);
            var rowButton = new WpfButton
            {
                Content = row, Padding = new Thickness(0), Margin = new Thickness(0),
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Transparent,
                ToolTip = CustomArtistPaletteStore.Current.IsCustom(palette.Identity)
                    ? "自定义配色；点击编辑" : "内置配色；保存后建立自定义覆盖"
            };
            rowButton.Click += (_, _) =>
            {
                ArtistNameEditor.Text = palette.Identity;
                SetArtistColors(palette.Colors);
            };
            ArtistList.Children.Add(rowButton);
        }
    }

    private static string ToRgbHex(string value) => value.Length == 9 && value.StartsWith('#')
        ? "#" + value[3..] : value;

    private void BuildCommonArtistColors()
    {
        CommonArtistColors.Children.Clear();
        foreach (var (name, hex) in CommonColors)
        {
            var color = ParseColor(hex);
            var button = new WpfButton
            {
                Content = name,
                Width = 92,
                Height = 34,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(color),
                Foreground = ContrastingForeground(color),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 222, 230)),
                ToolTip = $"{name} {hex}"
            };
            button.Click += (_, _) => ApplyCommonArtistColor(hex);
            CommonArtistColors.Children.Add(button);
        }
    }

    private void SetArtistColors(IEnumerable<string> colors)
    {
        _artistColors.Clear();
        foreach (var color in colors.Select(ToRgbHex))
        {
            if (!TryNormalizeArtistColor(color, out var normalized) ||
                _artistColors.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                continue;
            _artistColors.Add(normalized);
            if (_artistColors.Count == 5) break;
        }
        SyncArtistColorEditor();
    }

    private void SyncArtistColorEditor()
    {
        _syncingArtistColors = true;
        ArtistColorsEditor.Text = string.Join(", ", _artistColors);
        _syncingArtistColors = false;
        BuildArtistColorChips();
    }

    private void BuildArtistColorChips()
    {
        ArtistColorChips.Children.Clear();
        for (var index = 0; index < _artistColors.Count; index++)
        {
            var capturedIndex = index;
            var color = ParseColor(_artistColors[index]);
            var button = new WpfButton
            {
                Content = $"颜色 {index + 1}",
                MinWidth = 92,
                Height = 38,
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(color),
                Foreground = ContrastingForeground(color),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 222, 230)),
                ToolTip = $"{_artistColors[index]} · 点击修改，右键删除"
            };
            button.Click += (_, _) => EditArtistColor(capturedIndex);
            button.PreviewMouseRightButtonUp += (_, args) =>
            {
                args.Handled = true;
                if (_artistColors.Count <= 1)
                {
                    MessageBox.Show(this, "至少保留一个颜色。", "无法删除");
                    return;
                }
                _artistColors.RemoveAt(capturedIndex);
                SyncArtistColorEditor();
            };
            ArtistColorChips.Children.Add(button);
        }
    }

    private void ApplyCommonArtistColor(string hex)
    {
        if (_artistColors.Count <= 1)
        {
            SetArtistColors([hex]);
            return;
        }
        if (_artistColors.Count >= 5)
        {
            MessageBox.Show(this, "渐变最多使用 5 个颜色。", "颜色已满");
            return;
        }
        if (!_artistColors.Contains(hex, StringComparer.OrdinalIgnoreCase))
            _artistColors.Add(hex);
        SyncArtistColorEditor();
    }

    private void AddArtistColor_Click(object sender, RoutedEventArgs e)
    {
        if (_artistColors.Count >= 5)
        {
            MessageBox.Show(this, "渐变最多使用 5 个颜色。", "颜色已满");
            return;
        }
        var initial = _artistColors.LastOrDefault() ?? "#FF6B6B";
        if (ChooseArtistColor(initial) is not { } selected) return;
        _artistColors.Add(selected);
        SyncArtistColorEditor();
    }

    private void EditArtistColor(int index)
    {
        if (index < 0 || index >= _artistColors.Count) return;
        if (ChooseArtistColor(_artistColors[index]) is not { } selected) return;
        _artistColors[index] = selected;
        SyncArtistColorEditor();
    }

    private string? ChooseArtistColor(string initial)
    {
        var color = ParseColor(initial);
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK
            ? $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"
            : null;
    }

    private void ArtistColorsEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingArtistColors || ArtistColorsEditor is null) return;
        var values = Regex.Split(ArtistColorsEditor.Text, @"[,，;；\s]+")
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (values.Length == 0)
        {
            _artistColors.Clear();
            BuildArtistColorChips();
            return;
        }
        var normalized = new List<string>();
        foreach (var value in values)
        {
            if (!TryNormalizeArtistColor(value, out var color)) return;
            if (!normalized.Contains(color, StringComparer.OrdinalIgnoreCase))
                normalized.Add(color);
        }
        if (normalized.Count > 5) return;
        _artistColors.Clear();
        _artistColors.AddRange(normalized);
        BuildArtistColorChips();
    }

    private static bool TryNormalizeArtistColor(string value, out string normalized)
    {
        var text = value.Trim().ToUpperInvariant();
        if (Regex.IsMatch(text, "^#[0-9A-F]{6}$"))
        {
            normalized = text;
            return true;
        }
        if (Regex.IsMatch(text, "^#FF[0-9A-F]{6}$"))
        {
            normalized = "#" + text[3..];
            return true;
        }
        normalized = "";
        return false;
    }

    private static System.Windows.Media.Color ParseColor(string value) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!;

    private static System.Windows.Media.Brush ContrastingForeground(
        System.Windows.Media.Color color) =>
        color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 165
            ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;

    private static System.Windows.Media.Brush CreateBrush(IReadOnlyList<string> hexColors)
    {
        var colors = hexColors.Select(hex => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)).ToArray();
        if (colors.Length <= 1) return new SolidColorBrush(colors.FirstOrDefault(System.Windows.Media.Color.FromRgb(255, 107, 107)));
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.5), EndPoint = new System.Windows.Point(1, 0.5) };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], i / (double)(colors.Length - 1)));
        return brush;
    }

    private static string FormatOffset(double seconds) => Math.Abs(seconds) < 0.01
        ? "无偏移" : seconds > 0 ? $"快 {seconds:0.0} 秒" : $"慢 {Math.Abs(seconds):0.0} 秒";

    private void AutoModeButton_Click(object sender, RoutedEventArgs e) { _overlay.ToggleAutoColor(); RefreshState(); }
    private void Slow_Click(object sender, RoutedEventArgs e) { _overlay.AdjustLyrics(-0.5); RefreshState(); }
    private void Fast_Click(object sender, RoutedEventArgs e) { _overlay.AdjustLyrics(0.5); RefreshState(); }
    private void Reset_Click(object sender, RoutedEventArgs e) { _overlay.ResetLyricsOffset(); RefreshState(); }
    private void PreviousLyrics_Click(object sender, RoutedEventArgs e) { _overlay.ChangeLyricsCandidate(-1); RefreshState(); }
    private void NextLyrics_Click(object sender, RoutedEventArgs e) { _overlay.ChangeLyricsCandidate(1); RefreshState(); }
    private void AutoTimingButton_Click(object sender, RoutedEventArgs e) { _overlay.ToggleAutomaticLyricsCalibration(); RefreshState(); }
    private void KaraokeModeButton_Click(object sender, RoutedEventArgs e) { _overlay.ToggleKaraokeMode(); RefreshState(); }
    private void LineLoopButton_Click(object sender, RoutedEventArgs e) { _overlay.ToggleCurrentLineLoop(); RefreshState(); }
    private void PracticePointA_Click(object sender, RoutedEventArgs e) { _overlay.SetPracticePointA(); RefreshState(); }
    private void PracticePointB_Click(object sender, RoutedEventArgs e) { _overlay.SetPracticePointB(); RefreshState(); }
    private void ClearPracticeLoop_Click(object sender, RoutedEventArgs e) { _overlay.ClearPracticeLoop(); RefreshState(); }
    private async void PlaybackRateButton_Click(object sender, RoutedEventArgs e) { await _overlay.CyclePlaybackRateAsync(); RefreshState(); }
    private void DifficultSegmentButton_Click(object sender, RoutedEventArgs e) { _overlay.ToggleCurrentDifficultSegment(); RefreshState(); }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshingStartup) return;
        var enabled = StartupCheckBox.IsChecked == true;
        if (!_followService.SetStartupEnabled(enabled))
        {
            MessageBox.Show(this,
                "无法更新 Windows 开机启动项：\n" +
                (_followService.StartupRegistrationError ?? "未知错误"),
                "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshState();
    }

    private void ImportLyrics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入当前歌曲的 LRC / Enhanced LRC 歌词",
            Filter = "LRC 与 Enhanced LRC (*.lrc)|*.lrc|文本文件 (*.txt)|*.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var text = File.ReadAllText(dialog.FileName);
            if (!_overlay.SaveLocalLyrics(text, Path.GetFileName(dialog.FileName), out var error))
                MessageBox.Show(this, error, "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshState();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "读取失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void EditLyrics_Click(object sender, RoutedEventArgs e)
    {
        var editor = new LyricsEditorWindow(
            _overlay.CurrentLrcText, () => _overlay.CurrentPlaybackPosition) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (!_overlay.SaveLocalLyrics(editor.LyricsText, "本地编辑", out var error))
            MessageBox.Show(this, error, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshState();
    }

    private void ExportLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_overlay.CurrentLrcText))
        {
            MessageBox.Show(this, "当前没有可导出的同步歌词。", "无法导出");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出当前 LRC / Enhanced LRC 歌词",
            Filter = "LRC 同步歌词 (*.lrc)|*.lrc|文本文件 (*.txt)|*.txt",
            FileName = "lyrics.lrc"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, _overlay.CurrentLrcText); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RemoveLocalLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (!_overlay.HasLocalLyricsOverride) { MessageBox.Show(this, "当前歌曲没有本地覆盖。"); return; }
        if (MessageBox.Show(this, "删除当前歌曲的本地歌词覆盖？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _overlay.RemoveLocalLyricsOverride();
        RefreshState();
    }

    private void ClearLyricsCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "清空所有 LRCLIB 离线缓存？本地编辑的歌词不会删除。", "确认清空",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _overlay.ClearLyricsCache();
        RefreshState();
    }

    private void UseCurrentArtist_Click(object sender, RoutedEventArgs e)
    {
        ArtistNameEditor.Text = _overlay.CurrentArtist;
        var palette = ArtistColorEngine.Resolve(_overlay.CurrentArtist, _overlay.FallbackHighlightColor);
        SetArtistColors(palette.Colors);
    }

    private void SaveArtistPalette_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var colors = Regex.Split(ArtistColorsEditor.Text, @"[,，;；\s]+")
                .Where(value => !string.IsNullOrWhiteSpace(value));
            CustomArtistPaletteStore.Current.Set(ArtistNameEditor.Text, colors);
            if (CustomArtistPaletteStore.Current.TryGet(ArtistNameEditor.Text, out var savedColors))
                SetArtistColors(savedColors);
            _overlay.RefreshArtistColor();
            BuildArtistList();
            RefreshState();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message + "\n颜色格式示例：#F25F7C, #55B8FF", "无法保存",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteArtistPalette_Click(object sender, RoutedEventArgs e)
    {
        var identity = ArtistNameEditor.Text.Trim();
        if (!CustomArtistPaletteStore.Current.IsCustom(identity))
        {
            MessageBox.Show(this, "该歌手没有自定义覆盖；内置配色不会被删除。", "无需删除");
            return;
        }
        if (MessageBox.Show(this, $"删除“{identity}”的自定义配色？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        CustomArtistPaletteStore.Current.Remove(identity);
        _overlay.RefreshArtistColor();
        BuildArtistList();
        RefreshState();
    }

    private void ImportArtistPalette_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入自定义歌手配色库", Filter = "JSON 配色库 (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var count = CustomArtistPaletteStore.Current.Import(dialog.FileName);
            _overlay.RefreshArtistColor(); BuildArtistList(); RefreshState();
            MessageBox.Show(this, $"已导入 {count} 个歌手配色。", "导入完成");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportArtistPalette_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出自定义歌手配色库", Filter = "JSON 配色库 (*.json)|*.json",
            FileName = "artist-colors.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { CustomArtistPaletteStore.Current.Export(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingFont || FontComboBox.SelectedItem is not FontChoice choice) return;
        _overlay.SetFontFamily(choice.FamilyName);
    }
}
