using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace AppleMusicDesktopLyrics;

public partial class LyricsEditorWindow : Window
{
    public string LyricsText => Editor.Text;

    public LyricsEditorWindow(string initialText)
    {
        InitializeComponent();
        Editor.Text = initialText;
        Editor.CaretIndex = 0;
        UpdateValidation();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        if (ValidationText is null || Editor is null) return;
        var count = LrcParser.Parse(Editor.Text).Count;
        ValidationText.Text = count > 0 ? $"已识别 {count} 行同步歌词" : "尚未识别到有效时间戳";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (LrcParser.Parse(Editor.Text).Count == 0)
        {
            MessageBox.Show(this, "请至少输入一行带时间戳的歌词。", "无法保存",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
