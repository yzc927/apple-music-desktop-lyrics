namespace AppleMusicDesktopLyrics;

internal static class ApplicationLaunchPolicy
{
    public static bool ShouldShowManagement(bool isFirstRun, IEnumerable<string> arguments) =>
        isFirstRun || arguments.Any(argument => string.Equals(
            argument, "--management", StringComparison.OrdinalIgnoreCase));
}
