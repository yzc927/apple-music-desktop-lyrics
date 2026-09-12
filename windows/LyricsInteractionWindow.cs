using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace AppleMusicDesktopLyrics;

internal sealed class LyricsInteractionWindow : Window
{
    private readonly StackPanel _body = new() { Margin = new Thickness(18) };
    internal LyricsInteractionWindow(string title, Window owner)
    {
        Title = title; Owner = owner; Width = 640; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private void Text(string text) => _body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
    private void Action(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { System.Windows.MessageBox.Show(this, "未能完成或保存：" + ex.Message); } };
        _body.Children.Add(button);
    }
    internal static void Versions(OverlayWindow overlay, Window owner)
    {
        var window = new LyricsInteractionWindow("选择歌词版本", owner);
        var song = overlay.CurrentSongKey;
        window.Text("推荐候选的歌名吻合、时长相差不超过 2.5 秒，且版本标签相同。请核对是否为同一歌手；翻唱者不能记成原歌手别名。歌曲切换后请重新打开。");
        var items = new ListBox { MaxHeight = 340 };
        foreach (var candidate in overlay.LyricsCandidates)
            items.Items.Add(new ListBoxItem { Tag = candidate.Key, Content = new TextBlock { Text =
                (candidate.AliasRecommendation ? $"推荐确认歌手：{candidate.ExpectedArtist} ↔ {candidate.CandidateArtist}\n" : "") +
                candidate.Preview, TextWrapping = TextWrapping.Wrap, MaxWidth = 535, Margin = new Thickness(4, 8, 4, 8) } });
        items.SelectedIndex = overlay.LyricsCandidateIndex;
        window._body.Children.Add(items);
        void CheckSong() { if (song != overlay.CurrentSongKey) throw new InvalidOperationException("歌曲已经切换，请重新打开版本列表。"); }
        window.Action("使用所选版本", () => { CheckSong(); if (items.SelectedItem is ListBoxItem item) { overlay.SelectLyricsVersion(song, (string)item.Tag); window.Close(); } });
        window.Text("“使用所选版本”只记住本曲。下面的确认会保存歌手关系，以后其他歌曲也可自动匹配。");
        window.Action("确认是同一歌手，记住别名并使用", () =>
        {
            CheckSong();
            if (items.SelectedItem is not ListBoxItem item) throw new InvalidOperationException("请先选择一个推荐候选。");
            var candidate = overlay.LyricsCandidates.First(value => value.Key == (string)item.Tag);
            if (!candidate.AliasRecommendation) throw new InvalidOperationException("此候选无需或不适合建立歌手别名关系。");
            if (System.Windows.MessageBox.Show(window,
                $"确认 {candidate.ExpectedArtist} 与 {candidate.CandidateArtist} 是同一歌手？\n以后这两个名字会自动匹配。翻唱版本请只使用本曲版本。",
                "确认歌手别名", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            overlay.ConfirmLyricsAlias(song, candidate.Key); window.Close();
        });
        window.Action("这个版本不对，记住并排除", () => { CheckSong(); if (items.SelectedItem is ListBoxItem item) { overlay.RejectLyricsVersion(song, (string)item.Tag); window.Close(); } });
        window.Action("恢复本曲已排除的版本", () => { CheckSong(); overlay.RestoreLyricsVersions(); window.Close(); });
        if (items.Items.Count == 0) window.Text("当前没有在线候选。可关闭窗口后重新获取歌词，或恢复已排除的版本。");
        if (ConfirmedArtistAliases.Current.Items.Count > 0)
        {
            window.Text("已记住的歌手别名（撤销只影响跨歌曲匹配，本曲已选版本仍保留）：");
            foreach (var alias in ConfirmedArtistAliases.Current.Items)
                window.Action($"撤销：{alias.First} ↔ {alias.Second}", () =>
                { ConfirmedArtistAliases.Current.Remove(alias); overlay.RefreshLyrics(); window.Close(); });
        }
        window.ShowDialog();
    }
    internal static void Hotkeys(OverlayWindow overlay, Window owner)
    {
        var window = new LyricsInteractionWindow("全局快捷键", owner);
        window.Text("点击输入框后按组合键；也可以输入 Ctrl+Alt+H。留空禁用。保存时检测系统占用和内部重复。");
        var fields = new Dictionary<string, TextBox>();
        foreach (var (name, value) in LyricsPreferences.Current.Hotkeys)
        {
            window.Text(name);
            var field = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 5) };
            field.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                var modifiers = System.Windows.Input.Keyboard.Modifiers;
                if (modifiers == System.Windows.Input.ModifierKeys.None) return;
                if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin) return;
                try { field.Text = new System.Windows.Input.KeyGestureConverter().ConvertToInvariantString(new System.Windows.Input.KeyGesture(key, modifiers))!; e.Handled = true; } catch { }
            };
            fields[name] = field; window._body.Children.Add(field);
        }
        var status = new TextBlock { Text = overlay.GlobalHotkeys?.Status, TextWrapping = TextWrapping.Wrap };
        window.Action("保存并检测冲突", () =>
        {
            var proposed = fields.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Trim());
            if (overlay.GlobalHotkeys is null) throw new InvalidOperationException("快捷键尚未初始化，未保存更改。");
            overlay.GlobalHotkeys.TryApply(proposed, () => LyricsPreferences.Current.SaveHotkeys(proposed));
            status.Text = overlay.GlobalHotkeys?.Status;
        });
        window._body.Children.Add(status);
        window.ShowDialog();
    }
    internal static void Reading(OverlayWindow overlay, Window owner, string? selectedWord = null)
    {
        var window = new LyricsInteractionWindow("本曲日语读音", owner);
        var song = overlay.CurrentSongKey;
        if (string.IsNullOrWhiteSpace(song)) { System.Windows.MessageBox.Show(owner, "请先播放一首歌曲。"); return; }
        window.Text("点选词语修改读音，或输入完整词语（支持人名和特殊唱法）。熟词由你手动标记，仅影响本曲。");
        var words = new ComboBox { IsEditable = true, ItemsSource = overlay.CurrentRubyWords, Text = selectedWord ?? "", Margin = new Thickness(0, 6, 0, 6) };
        var reading = new TextBox { Margin = new Thickness(0, 6, 0, 6) };
        var familiar = new CheckBox { Content = "这个词已经熟悉，不需要注音" };
        void LoadWord(string word)
        {
            var saved = LyricsPreferences.Current.GetReading(song, word);
            reading.Text = saved?.Reading ?? ""; familiar.IsChecked = saved?.Familiar ?? false;
        }
        words.SelectionChanged += (_, _) => LoadWord(words.SelectedItem as string ?? words.Text);
        words.LostKeyboardFocus += (_, _) => LoadWord(words.Text);
        window._body.Children.Add(words); window.Text("纠正读音（留空保留自动读音）");
        window._body.Children.Add(reading); window._body.Children.Add(familiar);
        var only = new CheckBox { Content = "仅显示生词注音（隐藏已标记的熟词）", IsChecked = LyricsPreferences.Current.OnlyUnfamiliar, Margin = new Thickness(0, 16, 0, 8) };
        window._body.Children.Add(only); LoadWord(words.Text);
        window.Action("保存", () =>
        {
            if (song != overlay.CurrentSongKey) throw new InvalidOperationException("歌曲已切换，请重新打开。");
            var preferences = LyricsPreferences.Current; var word = words.Text.Trim();
            if (word.Length > 100 || reading.Text.Length > 100) throw new InvalidOperationException("词语和读音请保持在 100 字以内。");
            if (word.Length > 0)
            {
                if (!preferences.Readings.TryGetValue(song, out var entries)) preferences.Readings[song] = entries = new();
                entries[word] = new(reading.Text.Trim(), familiar.IsChecked == true);
            }
            preferences.OnlyUnfamiliar = only.IsChecked == true;
            preferences.Save(); overlay.RefreshPersonalRuby(); window.Close();
        });
        window.Action("恢复所选词的自动读音和生词状态", () =>
        {
            if (LyricsPreferences.Current.Readings.TryGetValue(song, out var entries)) entries.Remove(words.Text.Trim());
            LyricsPreferences.Current.Save(); overlay.RefreshPersonalRuby(); LoadWord(words.Text);
        });
        window.ShowDialog();
    }
}
