namespace JavMetaLite.Core.Models;

public sealed record AppPreferences
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string UiLanguage { get; init; } = UiLanguageCodes.System;

    public bool RememberSavePreferences { get; init; }

    public bool DirectSaveOverwrite { get; init; }

    public CrossVolumeVerificationMode CrossVolumeVerification { get; init; } =
        CrossVolumeVerificationMode.FullSha256;

    public OrganizationTargetMode TargetMode { get; init; } = OrganizationTargetMode.VideoDirectory;

    public string? CustomRootDirectory { get; init; }

    public string[] RecentCustomRootDirectories { get; init; } = [];

    public bool RenameVideo { get; init; }

    public bool WriteNfo { get; init; } = true;

    public bool DownloadPoster { get; init; } = true;

    public bool DownloadFanart { get; init; } = true;

    public bool DownloadExtrafanart { get; init; }

    public static AppPreferences CreateSafeDefaults() => new();
}

public static class UiLanguageCodes
{
    public const string System = "system";
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string English = "en";
    public const string Japanese = "ja";

    public static IReadOnlyList<string> Supported { get; } =
    [
        SimplifiedChinese,
        TraditionalChinese,
        English,
        Japanese
    ];

    public static string Normalize(string? languageCode, string fallback = System)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return fallback;
        }

        return Supported.FirstOrDefault(code =>
                   string.Equals(code, languageCode.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? (string.Equals(languageCode.Trim(), System, StringComparison.OrdinalIgnoreCase)
                   ? System
                   : fallback);
    }
}

public sealed record AppPreferencesLoadResult(
    AppPreferences Preferences,
    string? Warning = null,
    bool CanOverwrite = true)
{
    public bool UsedSafeDefaults => !Preferences.RememberSavePreferences;
}
