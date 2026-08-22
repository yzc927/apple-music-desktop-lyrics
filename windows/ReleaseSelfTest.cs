using System.IO;

namespace AppleMusicDesktopLyrics;

internal static class ReleaseSelfTest
{
    public static int Run()
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return 10;
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathFullyQualified(localAppData))
                return 11;
            if (!Uri.TryCreate("https://lrclib.net/", UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps)
                return 12;
            return 0;
        }
        catch
        {
            return 20;
        }
    }
}
