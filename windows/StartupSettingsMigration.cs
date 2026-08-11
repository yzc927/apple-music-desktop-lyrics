namespace AppleMusicDesktopLyrics;

internal static class StartupSettingsMigration
{
    public static bool Resolve(bool? configuredValue, bool hasLegacyRegistration) =>
        configuredValue ?? hasLegacyRegistration;
}
