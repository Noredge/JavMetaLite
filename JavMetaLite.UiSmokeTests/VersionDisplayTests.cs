using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static int RunVersionDisplayTests(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/version-display");
        AppLog.ConfigureDirectory(Path.Combine(root, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        TestVersionDisplay(root);
        return 0;
    }

    private static void TestVersionDisplay(string root)
    {
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "unused-settings.json")))
            { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var language = (ComboBox)window.FindName("LanguageComboBox");
            var versionText = (TextBlock)window.FindName("VersionText");
            if (versionText.Text != MainWindow.FormatVersionText(MainWindow.ApplicationVersion))
                throw new InvalidOperationException("Initial footer does not use the running assembly version.");
            foreach (var (code, previewLabel) in new[]
                     { ("en", "Preview"), ("zh-Hans", "预览版"), ("zh-Hant", "預覽版"), ("ja", "プレビュー") })
            {
                language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == code);
                window.UpdateLayout();
                var isPrerelease = MainWindow.ApplicationVersion.Split('+')[0].Contains('-');
                var expectedFooter = "v" + MainWindow.ApplicationVersion + (isPrerelease ? " · " + previewLabel : "");
                if (versionText.Text != expectedFooter ||
                    MainWindow.FormatVersionText("1.2.0") != "v1.2.0" ||
                    MainWindow.FormatVersionText("1.2.0+build-review") != "v1.2.0+build-review" ||
                    MainWindow.FormatVersionText("1.2.0-preview.56") != $"v1.2.0-preview.56 · {previewLabel}" ||
                    MainWindow.FormatVersionText("1.2.1-rc.1+review") != $"v1.2.1-rc.1+review · {previewLabel}")
                    throw new InvalidOperationException($"Release/prerelease footer mismatch for {code}.");
            }
            var informational = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            var fileVersion = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
            if (informational != MainWindow.ApplicationVersion || fileVersion != "1.2.0.0")
                throw new InvalidOperationException("Assembly version identity is inconsistent.");
            Console.WriteLine($"UI PASS version={informational} fileVersion={fileVersion} releaseFooterFourLanguages=True previewFooterFourLanguages=True buildMetadataNotPrerelease=True languageSwitchRefresh=True");
        }
        finally
        {
            window.Close();
            LocalizationService.ApplyLanguage(originalLanguage);
        }
    }
}
