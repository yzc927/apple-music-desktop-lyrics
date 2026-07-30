using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AppleMusicDesktopLyrics;

public partial class ManagementWindow : Window
{
    private readonly OverlayWindow _overlay;
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
        Activated += (_, _) => RefreshState();
        BuildArtistList();
        RefreshState();
    }

    private void RefreshState()
    {
        var artist = string.IsNullOrWhiteSpace(_overlay.CurrentArtist) ? "尚未读取到歌手" : _overlay.CurrentArtist;
        CurrentArtistText.Text = artist;
        CurrentModeText.Text = _overlay.IsAutoColor ? "自动配色已开启" : "当前使用手动颜色";
        AutoModeButton.Content = _overlay.IsAutoColor ? "关闭自动配色" : "开启自动配色";
        OffsetText.Text = FormatOffset(_overlay.CurrentOffsetSeconds);
        CurrentPalettePreview.Background = CreateBrush(ArtistColorEngine.Resolve(artist).Colors);
    }

    private void BuildArtistList()
    {
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
            ArtistList.Children.Add(row);
        }
    }

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
    private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingFont || FontComboBox.SelectedItem is not FontChoice choice) return;
        _overlay.SetFontFamily(choice.FamilyName);
    }
}
