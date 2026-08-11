using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace AppleMusicDesktopLyrics;

public partial class LyricsEditorWindow : Window, INotifyPropertyChanged
{
    private readonly Func<TimeSpan> _playbackPosition;
    private bool _synchronizing;
    private EditableLyricLine? _selectedLine;

    public ObservableCollection<EditableLyricLine> Lines { get; } = [];

    public EditableLyricLine? SelectedLine
    {
        get => _selectedLine;
        private set { _selectedLine = value; OnPropertyChanged(); }
    }

    public string LyricsText
    {
        get
        {
            CommitActiveView();
            return LrcParser.Serialize(Lines.Select(line => line.ToLyricLine()).OrderBy(line => line.Time).ToArray());
        }
    }

    public LyricsEditorWindow(string initialText, Func<TimeSpan> playbackPosition)
    {
        _playbackPosition = playbackPosition;
        InitializeComponent();
        DataContext = this;
        ReplaceLines(LrcParser.Parse(initialText));
        SourceEditor.Text = LrcParser.Serialize(Lines.Select(line => line.ToLyricLine()).ToArray());
        if (Lines.Count > 0) LinesGrid.SelectedIndex = 0;
        UpdateValidation();
    }

    private void ReplaceLines(IEnumerable<LyricLine> lines)
    {
        Lines.Clear();
        foreach (var line in lines) Lines.Add(new EditableLyricLine(line));
    }

    private void CommitActiveView()
    {
        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (EditorTabs.SelectedIndex != 1) return;
        ReplaceLines(LrcParser.Parse(SourceEditor.Text));
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || EditorTabs is null || SourceEditor is null || LinesGrid is null) return;
        if (e.Source != EditorTabs) return;
        _synchronizing = true;
        try
        {
            if (EditorTabs.SelectedIndex == 1)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                SourceEditor.Text = LrcParser.Serialize(Lines.Select(line => line.ToLyricLine()).ToArray());
            }
            else
            {
                ReplaceLines(LrcParser.Parse(SourceEditor.Text));
                LinesGrid.SelectedIndex = Lines.Count > 0 ? 0 : -1;
            }
        }
        finally { _synchronizing = false; }
        UpdateValidation();
    }

    private void LinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedLine = LinesGrid.SelectedItem as EditableLyricLine;
        SegmentsList.SelectedIndex = SelectedLine?.Segments.Count > 0 ? 0 : -1;
    }

    private void SourceEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_synchronizing) UpdateValidation();
    }

    private void StampLine_Click(object sender, RoutedEventArgs e) => StampSelectedLine(advance: true);
    private void LineEarlier_Click(object sender, RoutedEventArgs e) => ShiftSelected(TimeSpan.FromMilliseconds(-50));
    private void LineLater_Click(object sender, RoutedEventArgs e) => ShiftSelected(TimeSpan.FromMilliseconds(50));
    private void AllEarlier_Click(object sender, RoutedEventArgs e) => ShiftAll(TimeSpan.FromMilliseconds(-500));
    private void AllLater_Click(object sender, RoutedEventArgs e) => ShiftAll(TimeSpan.FromMilliseconds(500));

    private void StampSelectedLine(bool advance)
    {
        if (SelectedLine is null) return;
        SelectedLine.SetTime(_playbackPosition());
        LinesGrid.Items.Refresh();
        if (advance && LinesGrid.SelectedIndex + 1 < Lines.Count)
        {
            LinesGrid.SelectedIndex++;
            LinesGrid.ScrollIntoView(LinesGrid.SelectedItem);
        }
        UpdateValidation();
    }

    private void ShiftSelected(TimeSpan delta)
    {
        SelectedLine?.Shift(delta);
        LinesGrid.Items.Refresh();
        UpdateValidation();
    }

    private void ShiftAll(TimeSpan delta)
    {
        if (Lines.Count == 0) return;
        var earliest = Lines.Min(line => line.Time);
        if (earliest + delta < TimeSpan.Zero) delta = -earliest;
        foreach (var line in Lines) line.Shift(delta);
        LinesGrid.Items.Refresh();
        UpdateValidation();
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        var line = new EditableLyricLine(new LyricLine(_playbackPosition(), "新歌词"));
        var index = LinesGrid.SelectedIndex < 0 ? Lines.Count : LinesGrid.SelectedIndex + 1;
        Lines.Insert(index, line);
        LinesGrid.SelectedIndex = index;
        LinesGrid.ScrollIntoView(line);
        UpdateValidation();
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        var index = LinesGrid.SelectedIndex;
        if (index < 0 || index >= Lines.Count) return;
        Lines.RemoveAt(index);
        LinesGrid.SelectedIndex = Math.Min(index, Lines.Count - 1);
        UpdateValidation();
    }

    private void SplitWords_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLine is null || string.IsNullOrWhiteSpace(SelectedLine.Text)) return;
        SelectedLine.ReplaceSegments(LyricWordSplitter.Split(SelectedLine.Text));
        SegmentsList.Items.Refresh();
        SegmentsList.SelectedIndex = SelectedLine.Segments.Count > 0 ? 0 : -1;
        UpdateValidation();
    }

    private void StampWord_Click(object sender, RoutedEventArgs e) => StampSelectedWord();

    private void StampSelectedWord()
    {
        if (SelectedLine is null) return;
        if (SelectedLine.Segments.Count == 0) SplitWords_Click(this, new RoutedEventArgs());
        if (SelectedLine.Segments.Count == 0) return;
        var index = SegmentsList.SelectedIndex < 0 ? 0 : SegmentsList.SelectedIndex;
        var position = _playbackPosition();
        if (index == 0 && position < SelectedLine.Time) SelectedLine.SetTime(position);
        if (index > 0 && SelectedLine.Segments[index - 1].Time is { } previous && position < previous)
            position = previous;
        SelectedLine.Segments[index].Time = position;
        for (var later = index + 1; later < SelectedLine.Segments.Count; later++)
        {
            if (SelectedLine.Segments[later].Time is { } laterTime && laterTime < position)
                SelectedLine.Segments[later].Time = null;
        }
        if (SelectedLine.ExplicitEndTime is { } end && end < position)
            SelectedLine.ExplicitEndTime = null;
        if (index + 1 < SelectedLine.Segments.Count)
        {
            SegmentsList.SelectedIndex = index + 1;
            SegmentsList.ScrollIntoView(SegmentsList.SelectedItem);
        }
        LinesGrid.Items.Refresh();
        UpdateValidation();
    }

    private void StampEnd_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLine is null || SelectedLine.Segments.Count == 0) return;
        var position = _playbackPosition();
        var last = SelectedLine.Segments.Last().Time;
        SelectedLine.ExplicitEndTime = last is { } value && position < value ? value : position;
        LinesGrid.Items.Refresh();
        UpdateValidation();
    }

    private void ClearWords_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLine is null) return;
        SelectedLine.Segments.Clear();
        SelectedLine.ExplicitEndTime = null;
        LinesGrid.Items.Refresh();
        UpdateValidation();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F8 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (EditorTabs.SelectedIndex != 0) EditorTabs.SelectedIndex = 0;
            StampSelectedWord();
            e.Handled = true;
        }
        else if (e.Key == Key.F8)
        {
            if (EditorTabs.SelectedIndex != 0) EditorTabs.SelectedIndex = 0;
            StampSelectedLine(advance: true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key is Key.Left or Key.Right)
        {
            ShiftSelected(TimeSpan.FromMilliseconds(e.Key == Key.Left ? -50 : 50));
            e.Handled = true;
        }
    }

    private void UpdateValidation()
    {
        if (ValidationText is null) return;
        var lines = EditorTabs?.SelectedIndex == 1 ? LrcParser.Parse(SourceEditor.Text) :
            Lines.Select(line => line.ToLyricLine()).ToArray();
        var enhanced = lines.Count(line => line.HasWordTiming);
        var incomplete = EditorTabs?.SelectedIndex == 0
            ? Lines.Count(line => line.Segments.Count > 0 && !line.HasCompleteWordTiming)
            : 0;
        ValidationText.Text = lines.Count == 0
            ? "尚未识别到有效时间戳"
            : $"已识别 {lines.Count} 行 · Enhanced {enhanced} 行" +
              (incomplete > 0 ? $" · {incomplete} 行词级打点未完成" : "");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveView();
        if (Lines.Count == 0)
        {
            MessageBox.Show(this, "请至少输入一行带时间戳的歌词。", "无法保存",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var incomplete = Lines.Count(line => line.Segments.Count > 0 && !line.HasCompleteWordTiming);
        if (incomplete > 0)
        {
            MessageBox.Show(this, $"还有 {incomplete} 行词级打点未完成。请完成打点或清除这些行的词级时间。",
                "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var invalid = Lines.Count(line => line.Segments.Count > 0 && !line.HasValidWordTiming);
        if (invalid > 0)
        {
            MessageBox.Show(this, $"还有 {invalid} 行的词级时间不是递增顺序，或行尾早于最后一个词。",
                "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
