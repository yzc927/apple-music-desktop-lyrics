using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Text.RegularExpressions;
using MessageBox = System.Windows.MessageBox;
using WpfButton = System.Windows.Controls.Button;

namespace AppleMusicDesktopLyrics;

public partial class ManagementWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private bool _selectingFont;

    public ManagementWindow(OverlayWindow overlay)
    {
        InitializeComponent();
        _overlay = overlay;
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
        BuildArtistList();
        RefreshState();
    }

    private void RefreshState()
    {
        var artist = string.IsNullOrWhiteSpace(_overlay.CurrentArtist) ? "尚未读取到歌手" : _overlay.CurrentArtist;
        CurrentArtistText.Text = artist;
        CurrentModeText.Text = _overlay.IsAutoColor ? "自动配色已开启；未收录歌手使用所选后备色" : "当前使用手动颜色";
        LyricsSourceText.Text = $"歌词来源：{_overlay.CurrentLyricsSource}";
        LyricsVersionText.Text = _overlay.LyricsCandidateCount > 0
            ? $"{_overlay.LyricsCandidateIndex + 1}/{_overlay.LyricsCandidateCount} · {_overlay.LyricsCandidateLabel}"
            : "当前没有可切换的 LRCLIB 版本";
        AutoModeButton.Content = _overlay.IsAutoColor ? "关闭自动配色" : "开启自动配色";
        AutoTimingButton.Content = _overlay.IsAutomaticLyricsCalibration
            ? "关闭 Apple 自动对时" : "开启 Apple 自动对时";
        KaraokeModeButton.Content = _overlay.IsKaraokeMode ? "普通模式" : "卡拉 OK 模式";
        KaraokeModeDescription.Text = _overlay.IsKaraokeMode
            ? "当前按播放进度从左向右扫色。"
            : "当前整句从一开始就完整显示颜色。";
        OffsetText.Text = FormatOffset(_overlay.CurrentOffsetSeconds);
        LocalLyricsStatusText.Text = _overlay.HasLocalLyricsOverride
            ? "当前歌曲正在使用本地永久覆盖。"
            : _overlay.HasCachedLyrics ? "当前歌曲已有 LRCLIB 离线缓存。" : "当前歌曲尚无本地覆盖或缓存。";
        CurrentPalettePreview.Background = CreateBrush(
            ArtistColorEngine.Resolve(artist, _overlay.FallbackHighlightColor).Colors);
    }

    private void BuildArtistList()
    {
        ArtistList.Children.Clear();
        var index = 0;
        foreach (var palette in ArtistColorEngine.GetCuratedPalettes())
        {
            var row = new Grid { Height = 46, Background = index++ % 2 == 0 ? System.Windows.Media.Brushes.White :
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 240, 244)) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock
            {
                Text = palette.Identity, Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 76, 88))
            });
            var preview = new Border
            {
                Width = 210, Height = 22, CornerRadius = new CornerRadius(11),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0),
                Background = CreateBrush(palette.Colors)
            };
            Grid.SetColumn(preview, 1);
            row.Children.Add(preview);
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
                ArtistColorsEditor.Text = string.Join(", ", palette.Colors.Select(ToRgbHex));
            };
            ArtistList.Children.Add(rowButton);
        }
    }

    private static string ToRgbHex(string value) => value.Length == 9 && value.StartsWith('#')
        ? "#" + value[3..] : value;

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

    private void ImportLyrics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入当前歌曲的 LRC 歌词", Filter = "LRC 同步歌词 (*.lrc)|*.lrc|文本文件 (*.txt)|*.txt"
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
        var editor = new LyricsEditorWindow(_overlay.CurrentLrcText) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (!_overlay.SaveLocalLyrics(editor.LyricsText, "本地编辑", out var error))
            MessageBox.Show(this, error, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        RefreshState();
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
        ArtistColorsEditor.Text = string.Join(", ", palette.Colors.Select(ToRgbHex));
    }

    private void SaveArtistPalette_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var colors = Regex.Split(ArtistColorsEditor.Text, @"[,，;；\s]+")
                .Where(value => !string.IsNullOrWhiteSpace(value));
            CustomArtistPaletteStore.Current.Set(ArtistNameEditor.Text, colors);
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
