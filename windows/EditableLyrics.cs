using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace AppleMusicDesktopLyrics;

public sealed class EditableLyricSegment : INotifyPropertyChanged
{
    private TimeSpan? _time;
    private string _text;

    public EditableLyricSegment(string text, TimeSpan? time = null)
    {
        _text = text;
        _time = time;
    }

    public TimeSpan? Time
    {
        get => _time;
        set { _time = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Display => Time is { } time ? $"{LrcTime.Format(time)}  {Text}" : $"--:--.--  {Text}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class EditableLyricLine : INotifyPropertyChanged
{
    private TimeSpan _time;
    private string _text;
    private TimeSpan? _explicitEndTime;

    internal EditableLyricLine(LyricLine line)
    {
        _time = line.Time;
        _text = LyricTiming.IsInstrumental(line.Text) ? "" : line.Text;
        _explicitEndTime = line.ExplicitEndTime;
        Segments = new ObservableCollection<EditableLyricSegment>(
            line.Segments?.Select(segment => new EditableLyricSegment(segment.Text, segment.Time)) ?? []);
        Segments.CollectionChanged += (_, _) => NotifyTimingChanged();
        foreach (var segment in Segments) segment.PropertyChanged += (_, _) => NotifyTimingChanged();
    }

    public TimeSpan Time
    {
        get => _time;
        private set { _time = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeText)); }
    }

    public string TimeText
    {
        get => LrcTime.Format(Time);
        set
        {
            if (LrcTime.TryParse(value, out var parsed)) SetTime(parsed);
            else OnPropertyChanged();
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            if (Segments.Count > 0 && string.Concat(Segments.Select(segment => segment.Text)).TrimEnd() != value.TrimEnd())
            {
                Segments.Clear();
                ExplicitEndTime = null;
            }
            OnPropertyChanged();
            NotifyTimingChanged();
        }
    }

    public ObservableCollection<EditableLyricSegment> Segments { get; }

    public TimeSpan? ExplicitEndTime
    {
        get => _explicitEndTime;
        set { _explicitEndTime = value; OnPropertyChanged(); NotifyTimingChanged(); }
    }

    public string TimingSummary
    {
        get
        {
            if (Segments.Count == 0) return "普通 LRC";
            var recorded = Segments.Count(segment => segment.Time is not null);
            var suffix = ExplicitEndTime is null ? "" : " · 有行尾";
            return $"词级 {recorded}/{Segments.Count}{suffix}";
        }
    }

    public bool HasCompleteWordTiming => Segments.Count > 0 && Segments.All(segment => segment.Time is not null);

    public bool HasValidWordTiming
    {
        get
        {
            if (!HasCompleteWordTiming) return false;
            var previous = Time;
            foreach (var segment in Segments)
            {
                if (segment.Time!.Value < previous) return false;
                previous = segment.Time.Value;
            }
            return ExplicitEndTime is null || ExplicitEndTime >= previous;
        }
    }

    public void SetTime(TimeSpan value)
    {
        value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        Shift(value - Time);
    }

    public void Shift(TimeSpan delta)
    {
        var target = Time + delta;
        if (target < TimeSpan.Zero) delta = -Time;
        Time += delta;
        foreach (var segment in Segments)
            if (segment.Time is { } time) segment.Time = time + delta;
        if (ExplicitEndTime is { } end) ExplicitEndTime = end + delta;
        NotifyTimingChanged();
    }

    public void ReplaceSegments(IEnumerable<string> values)
    {
        Segments.Clear();
        foreach (var value in values.Where(value => value.Length > 0))
        {
            var segment = new EditableLyricSegment(value);
            segment.PropertyChanged += (_, _) => NotifyTimingChanged();
            Segments.Add(segment);
        }
        ExplicitEndTime = null;
        NotifyTimingChanged();
    }

    internal LyricLine ToLyricLine()
    {
        IReadOnlyList<TimedLyricSegment>? segments = null;
        if (HasCompleteWordTiming)
            segments = Segments.Select(segment => new TimedLyricSegment(segment.Time!.Value, segment.Text)).ToArray();
        return new LyricLine(Time, string.IsNullOrWhiteSpace(Text) ? "♪" : Text,
            segments, segments is null ? null : ExplicitEndTime);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void NotifyTimingChanged()
    {
        OnPropertyChanged(nameof(TimingSummary));
        OnPropertyChanged(nameof(HasCompleteWordTiming));
        OnPropertyChanged(nameof(HasValidWordTiming));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class LyricWordSplitter
{
    public static IReadOnlyList<string> Split(string text)
    {
        var result = new List<string>();
        var latin = new StringBuilder();
        void Flush()
        {
            if (latin.Length == 0) return;
            result.Add(latin.ToString());
            latin.Clear();
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value <= 0x024f && (Rune.IsLetterOrDigit(rune) || Rune.IsWhiteSpace(rune)))
            {
                latin.Append(rune.ToString());
                if (Rune.IsWhiteSpace(rune)) Flush();
                continue;
            }
            Flush();
            var value = rune.ToString();
            if (result.Count > 0 && Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.ClosePunctuation or UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation)
                result[^1] += value;
            else
                result.Add(value);
        }
        Flush();
        return result;
    }
}

internal static class LrcTime
{
    public static string Format(TimeSpan value) =>
        $"{Math.Max(0, (int)value.TotalMinutes):00}:{Math.Max(0, value.Seconds):00}.{Math.Max(0, value.Milliseconds / 10):00}";

    public static bool TryParse(string? value, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        var parts = (value ?? "").Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var minutes) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            minutes < 0 || seconds is < 0 or >= 60) return false;
        result = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }
}
