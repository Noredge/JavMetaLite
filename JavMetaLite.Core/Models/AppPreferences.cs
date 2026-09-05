using System.Text.Json.Serialization;

namespace JavMetaLite.Core.Models;

public sealed record AppPreferences
{
    public const int CurrentSchemaVersion = 12;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string UiLanguage { get; init; } = UiLanguageCodes.System;

    public bool RememberSavePreferences { get; init; }

    public string SearchSourceMode { get; init; } = MetadataSearchSourceModes.LibreDmm;

    public bool SkipSavePreview { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DirectSaveOverwrite { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkipBatchSavePreview { get; init; }

    public CrossVolumeVerificationMode CrossVolumeVerification { get; init; } =
        CrossVolumeVerificationMode.FullSha256;

    public OrganizationTargetMode TargetMode { get; init; } = OrganizationTargetMode.VideoDirectory;

    public string? CustomRootDirectory { get; init; }

    public string[] RecentCustomRootDirectories { get; init; } = [];

    public bool RenameVideo { get; init; }

    public bool WriteNfo { get; init; } = true;

    public bool IncludeIdInTitle { get; init; } = true;

    public bool DownloadPoster { get; init; } = true;

    public bool DownloadFanart { get; init; } = true;

    public bool DownloadExtrafanart { get; init; }

    public bool ReplaceLocalExtrafanart { get; init; }

    public MetadataSourcePreferenceProfile CustomSourceProfile { get; init; } = new();

    public static AppPreferences CreateSafeDefaults() => new();
}

public static class MetadataSearchSourceModes
{
    public const string Auto = "auto"; // Legacy preference value; now defaults to LibreDMM.
    public const string LibreDmm = "libredmm";
    public const string R18Dev = "r18dev";
    public const string JavLibrary = "javlibrary"; // Legacy preference value; browser import only.
    public const string Custom = "custom";
    public const string Manual = "manual";

    public static IReadOnlyList<string> Supported { get; } =
    [
        LibreDmm,
        R18Dev,
        Custom,
        Manual
    ];

    public static string Normalize(string? mode) =>
        string.Equals(mode?.Trim(), JavLibrary, StringComparison.OrdinalIgnoreCase)
            ? Manual
            : Supported.FirstOrDefault(candidate =>
                string.Equals(candidate, mode?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? LibreDmm;

    // Shared by single-movie search, queue search and source-scoped failure retry.
    public static IReadOnlyList<string> AutomaticSources(string? mode) => Normalize(mode) switch
    {
        Custom => [LibreDmm, R18Dev],
        R18Dev => [R18Dev],
        Manual => [],
        _ => [LibreDmm]
    };
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
