namespace AppleMusicDesktopLyrics;

internal static class AppleUiPollingPolicy
{
    public static bool ShouldRead(bool usingAppleLyrics, bool automaticCalibrationEnabled,
        int lyricLineCount) =>
        usingAppleLyrics || (automaticCalibrationEnabled && lyricLineCount > 0);

    public static TimeSpan NextDelay(bool succeeded, int consecutiveFailures,
        TimeSpan readElapsed)
    {
        if (succeeded)
            return readElapsed >= TimeSpan.FromMilliseconds(500)
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromMilliseconds(1500);

        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 4);
        return TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, exponent)));
    }
}
