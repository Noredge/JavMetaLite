using System.IO;
using System.Windows;
using System.Windows.Controls;
using JavMetaLite.App;
using JavMetaLite.Core.Services;
using static JavMetaLite.Core.Services.BatchMetadataSearchItemStatus;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static int RunFooterStatusChecks()
    {
        AppLog.ConfigureDirectory(Path.Combine(TestSettingsRoot.Value.FullName, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        TestFooterStatus();
        return 0;
    }

    private static void TestFooterStatus()
    {
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        var window = new MainWindow(TestPreferences());
        var footer = (TextBlock)window.FindName("StatusText");
        using var job = new MovieJob();

        void Check(string summary, string details)
        {
            if (footer.Text != summary || !Equals(footer.ToolTip, details))
                throw new InvalidOperationException($"Unexpected footer: {footer.Text} ({footer.ToolTip})");
        }

        void Search(string expected, params BatchMetadataSearchItemStatus[] outcomes)
        {
            var result = new BatchMetadataSearchResult(outcomes.Select(status =>
                new BatchMetadataSearchItemResult(job, status, [], null)).ToArray());
            typeof(MainWindow).GetMethod("ShowBatchSearchSummary", PrivateInstance)!.Invoke(window, [result, "test"]);
            Check(expected, LocalizationService.Get("Status.BatchSourcesComplete", result.SucceededCount,
                result.PartialCount, result.FailedCount, result.CanceledCount, result.NotStartedCount));
        }

        void Save(string expected, int completed, bool cleanup, bool canceled)
        {
            var details = LocalizationService.Get("Status.BatchSaveComplete", completed, 0, canceled ? 1 : 0, 0);
            typeof(MainWindow).GetMethod("SetSaveResultStatus", PrivateInstance)!.Invoke(window,
                [completed, details, cleanup, canceled]);
            Check(expected, cleanup ? details + Environment.NewLine + LocalizationService.Get("Cleanup.Committed") : details);
        }

        try
        {
            foreach (var (language, found, saved) in new[] {
                ("zh-Hans", "已查到 2 部", "已保存 2 部"), ("zh-Hant", "已查到 2 部", "已儲存 2 部"),
                ("en", "Found 2", "Saved 2"), ("ja", "検索結果 2 件", "保存済み 2 件") })
            {
                LocalizationService.ApplyLanguage(language);
                Search(found, Succeeded, Succeeded);
                Save(saved, 2, false, false);
            }
            LocalizationService.ApplyLanguage("zh-Hans");
            Search("已查到 1 部 · 部分来源失败", PartiallySucceeded);
            Search("1 部查询失败", Failed);
            Search("已查到 1 部 · 2 部需检查", PartiallySucceeded, Failed);
            Search("已查到 1 部 · 已取消", Succeeded, Canceled, NotStarted);
            Search("已取消", NotStarted);
            Save("已保存 1 部 · 已取消", 1, false, true);
            Save("已保存 1 部 · 待清理", 1, true, false);
            Console.WriteLine("UI PASS compact search/save footer, details, warnings, cancellation and cleanup; four languages");
        }
        finally
        {
            window.Close();
            LocalizationService.ApplyLanguage(originalLanguage);
        }
    }
}
