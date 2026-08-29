namespace JavMetaLite.Core.Models;

public sealed record AppPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool RememberSavePreferences { get; init; }

    public OrganizationTargetMode TargetMode { get; init; } = OrganizationTargetMode.VideoDirectory;

    public string? CustomRootDirectory { get; init; }

    public bool RenameVideo { get; init; }

    public bool WriteNfo { get; init; } = true;

    public bool DownloadPoster { get; init; } = true;

    public bool DownloadFanart { get; init; } = true;

    public bool DownloadExtrafanart { get; init; }

    public static AppPreferences CreateSafeDefaults() => new();
}

public sealed record AppPreferencesLoadResult(
    AppPreferences Preferences,
    string? Warning = null,
    bool CanOverwrite = true)
{
    public bool UsedSafeDefaults => !Preferences.RememberSavePreferences;
}
