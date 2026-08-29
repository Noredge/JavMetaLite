using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class AppPreferencesStore
{
    public const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _settingsDirectory;

    public AppPreferencesStore(string? settingsDirectory = null)
    {
        _settingsDirectory = string.IsNullOrWhiteSpace(settingsDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JavMetaLite")
            : Path.GetFullPath(settingsDirectory);
    }

    public string SettingsPath => Path.Combine(_settingsDirectory, SettingsFileName);

    public AppPreferencesLoadResult Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppPreferencesLoadResult(AppPreferences.CreateSafeDefaults());
        }

        try
        {
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var preferences = JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions)
                ?? throw new JsonException("配置内容为空。");
            if (preferences.SchemaVersion != AppPreferences.CurrentSchemaVersion)
            {
                return new AppPreferencesLoadResult(
                    AppPreferences.CreateSafeDefaults(),
                    $"偏好配置版本 {preferences.SchemaVersion} 不受当前程序支持，已使用安全默认值。",
                    CanOverwrite: false);
            }

            if (!preferences.RememberSavePreferences)
            {
                return new AppPreferencesLoadResult(AppPreferences.CreateSafeDefaults());
            }

            return new AppPreferencesLoadResult(Normalize(preferences));
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppPreferencesLoadResult(
                AppPreferences.CreateSafeDefaults(),
                $"偏好配置无法读取，已使用安全默认值：{exception.Message}");
        }
    }

    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.RememberSavePreferences)
        {
            Clear();
            return;
        }

        Directory.CreateDirectory(_settingsDirectory);
        var temporaryPath = Path.Combine(
            _settingsDirectory,
            $".{SettingsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var normalized = Normalize(preferences) with
            {
                SchemaVersion = AppPreferences.CurrentSchemaVersion,
                RememberSavePreferences = true
            };
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A failed best-effort cleanup must not hide the original save result.
                }
                catch (UnauthorizedAccessException)
                {
                    // A failed best-effort cleanup must not hide the original save result.
                }
            }
        }
    }

    public void Clear()
    {
        if (File.Exists(SettingsPath))
        {
            File.Delete(SettingsPath);
        }
    }

    private static AppPreferences Normalize(AppPreferences preferences) => preferences with
    {
        SchemaVersion = AppPreferences.CurrentSchemaVersion,
        TargetMode = Enum.IsDefined(preferences.TargetMode)
            ? preferences.TargetMode
            : OrganizationTargetMode.VideoDirectory,
        CustomRootDirectory = string.IsNullOrWhiteSpace(preferences.CustomRootDirectory)
            ? null
            : preferences.CustomRootDirectory.Trim()
    };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
