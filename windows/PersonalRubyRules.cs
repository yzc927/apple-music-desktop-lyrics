namespace AppleMusicDesktopLyrics;

internal static class PersonalRubyRules
{
    internal static IReadOnlyList<RubySegment> Apply(string text, IReadOnlyList<RubySegment> automatic,
        IReadOnlyDictionary<string, ReadingPreference> entries, bool onlyUnfamiliar)
    {
        var result = new List<RubySegment>();
        var position = 0;
        var automaticPositions = new Dictionary<int, RubySegment>();
        foreach (var segment in automatic) { automaticPositions[position] = segment; position += segment.DisplayText.Length; }
        var boundaries = automaticPositions.Keys.Append(position).ToHashSet();
        bool Applicable(string word, int start) => word.Length > 0 &&
            text.AsSpan(start).StartsWith(word, StringComparison.Ordinal) &&
            (entries[word].Reading.Length > 0 ||
             (boundaries.Contains(start) && boundaries.Contains(start + word.Length)));
        // Empty readings mean "keep automatic", not "erase". A reading such
        // as 明日/あす cannot safely be split into per-character readings.
        // Therefore partial empty/familiar rules leave that automatic token intact.
        position = 0;
        while (position < text.Length)
        {
            var custom = entries.Keys.Where(word => Applicable(word, position))
                .OrderByDescending(word => word.Length).FirstOrDefault();
            if (custom is not null)
            {
                var preference = entries[custom];
                var reading = preference.Reading;
                if (reading.Length == 0)
                {
                    // A familiar phrase can span multiple automatically analyzed words.
                    var end = position + custom.Length;
                    reading = string.Concat(automaticPositions.Where(pair => pair.Key >= position &&
                        pair.Key + pair.Value.DisplayText.Length <= end).OrderBy(pair => pair.Key)
                        .Select(pair => pair.Value.ReadingText.Length > 0 ? pair.Value.ReadingText : pair.Value.DisplayText));
                }
                result.Add(new(custom, onlyUnfamiliar && preference.Familiar ? "" : reading));
                position += custom.Length;
            }
            else if (automaticPositions.TryGetValue(position, out var segment) &&
                !entries.Keys.Any(word => word.Length > 0 && Enumerable.Range(position + 1, Math.Max(0, segment.DisplayText.Length - 1))
                    .Any(start => Applicable(word, start))))
            {
                result.Add(segment); position += segment.DisplayText.Length;
            }
            else { result.Add(new(text[position].ToString(), "")); position++; }
        }
        return result.Any(segment => segment.ReadingText.Length > 0) ? result : [];
    }
}
