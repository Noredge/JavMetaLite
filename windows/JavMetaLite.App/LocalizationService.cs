using System.Globalization;
using System.Windows;
using JavMetaLite.Core.Models;

namespace JavMetaLite.App;

internal static class LocalizationService
{
    private const string DictionaryMarker = "JavMetaLite.Localization";
    private static ResourceDictionary? _englishFallback;

    public static string CurrentLanguageCode { get; private set; } = UiLanguageCodes.English;

    public static string DetectSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageCodes.TraditionalChinese;
        }

        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageCodes.SimplifiedChinese;
        }

        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageCodes.Japanese;
        }

        if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageCodes.English;
        }

        return UiLanguageCodes.English;
    }

    public static string ResolvePreference(string? languageCode) =>
        string.Equals(languageCode, UiLanguageCodes.System, StringComparison.OrdinalIgnoreCase)
            ? DetectSystemLanguage()
            : UiLanguageCodes.Normalize(languageCode, UiLanguageCodes.English);

    public static void ApplyLanguage(string? languageCode)
    {
        var normalized = ResolvePreference(languageCode);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
        var application = Application.Current;
        if (application is null)
        {
            CurrentLanguageCode = normalized;
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            string.Equals(dictionary["LocalizationMarker"] as string, DictionaryMarker, StringComparison.Ordinal));
        var expectedPath = $"Resources/Strings.{normalized}.xaml";
        if (current?.Source?.OriginalString.EndsWith(expectedPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            CurrentLanguageCode = normalized;
            return;
        }

        var replacement = new ResourceDictionary
        {
            Source = new Uri($"/JavMetaLite;component/{expectedPath}", UriKind.Relative)
        };
        if (current is not null)
        {
            dictionaries.Remove(current);
        }

        dictionaries.Insert(0, replacement);
        CurrentLanguageCode = normalized;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
    }

    public static string Get(string key, params object?[] arguments)
    {
        var value = Application.Current?.TryFindResource(key)?.ToString();
        if (value is null)
        {
            System.Diagnostics.Trace.TraceWarning($"Missing localization key: {CurrentLanguageCode}/{key}");
            if (Application.Current is not null)
            {
                _englishFallback ??= new ResourceDictionary
                { Source = new Uri("/JavMetaLite;component/Resources/Strings.en.xaml", UriKind.Relative) };
                value = _englishFallback[key]?.ToString();
            }
        }
        value ??= key;
        return arguments.Length == 0
            ? value
            : string.Format(CultureInfo.CurrentUICulture, value, arguments);
    }
}
