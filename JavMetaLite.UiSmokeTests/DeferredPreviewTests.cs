using System.Collections;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static async Task TestDeferredPreviewAsync(string root)
    {
        Directory.CreateDirectory(root);
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        using var handler = new CoverCheckHandler();
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "settings")))
            { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(handler));
        window.Show();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            var first = CoverJob("DEF-001");
            var next = CoverJob("DEF-002");
            var missing = CoverJob("DEF-003", "https://cover.invalid/missing.png");
            var empty = new MovieJob();
            empty.ResetForVideo(@"C:\Synthetic\DEF-004.mp4", "DEF-004");
            var slow = CoverJob("DEF-005", "https://cover.invalid/slow.png");
            foreach (var job in new[] { first, next, missing, empty, slow })
            {
                typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!.Invoke(window, [job]);
                queue.Add(job);
            }
            Task Activate(MovieJob job, bool load) => (Task)typeof(MainWindow)
                .GetMethod("ActivateMovieJobAsync", PrivateInstance)!.Invoke(window, [job, load])!;
            var poster = (Image)window.FindName("PosterImage");
            var fanart = (Image)window.FindName("FanartImage");
            var posterText = (TextBlock)window.FindName("PosterPreviewStateText");
            var fanartText = (TextBlock)window.FindName("FanartPreviewStateText");
            void AssertPlaceholder(string key)
            {
                CoverAssert(poster.Source is null && fanart.Source is null, "Unexpected cached artwork in placeholder test.");
                CoverAssert(posterText.Text == LocalizationService.Get(key) && fanartText.Text == LocalizationService.Get(key),
                    "Wrong preview state: " + key);
                CoverAssert((posterText.ToolTip is not null) == (key == "ArtworkPreview.Deferred") &&
                    (fanartText.ToolTip is not null) == (key == "ArtworkPreview.Deferred"), "Stale preview tooltip.");
            }
            await Activate(first, false);
            foreach (var language in new[] { "zh-Hans", "zh-Hant", "en", "ja" })
            {
                var combo = (ComboBox)window.FindName("LanguageComboBox");
                combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(item => item.Tag?.ToString() == language);
                window.UpdateLayout();
                AssertPlaceholder("ArtworkPreview.Deferred");
                CoverAssert(posterText.ToolTip?.ToString() == LocalizationService.Get("ArtworkPreview.DeferredHint"), "Tooltip did not localize.");
                CoverAssert(FitsInside(posterText, (FrameworkElement)window.FindName("PosterPreviewBorder")) &&
                    FitsInside(fanartText, (FrameworkElement)window.FindName("FanartPreviewBorder")), "Deferred text overflows in " + language);
                var visual = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                visual.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(visual));
                using var output = File.Create(Path.Combine(root, "deferred-" + language + ".png"));
                encoder.Save(output);
            }
            CoverAssert(handler.Calls == 0, "Deferred state downloaded artwork.");
            await Activate(first, true);
            CoverAssert(poster.Source is not null && fanart.Source is not null, "Normal preview did not load.");
            var cachedPoster = poster.Source;
            var calls = handler.Calls;
            await Activate(next, false);
            AssertPlaceholder("ArtworkPreview.Deferred");
            await Activate(first, false);
            CoverAssert(ReferenceEquals(cachedPoster, poster.Source) && handler.Calls == calls, "Cached preview was replaced by deferred state.");
            // Exercise the actual batch-save completion path, not only direct activation.
            first.BeginSave(first.ReviewRevision);
            first.MarkSaveCompleted();
            await (Task)typeof(MainWindow).GetMethod("RemoveCompletedQueueJobsAsync", PrivateInstance)!
                .Invoke(window, [new[] { first }])!;
            AssertPlaceholder("ArtworkPreview.Deferred");
            CoverAssert(handler.Calls == calls, "Batch completion loaded the next preview.");
            await Activate(missing, false);
            AssertPlaceholder("ArtworkPreview.Deferred");
            await Activate(missing, true);
            AssertPlaceholder("ArtworkPreview.Unavailable");
            await Activate(next, false);
            await Activate(missing, false);
            AssertPlaceholder("ArtworkPreview.Unavailable");
            await Activate(empty, false);
            AssertPlaceholder("ArtworkPreview.Unavailable");

            handler.Hold = true;
            var operation = (Task)typeof(MainWindow).GetMethod("RunBusyAsync", PrivateInstance)!
                .Invoke(window, ["Synthetic deferred-preview cancellation", new Func<Task>(() => Activate(slow, true))])!;
            await UntilStableAsync(() => handler.Pending > 0, "Cancelable preview did not start.");
            CoverAssert(posterText.Text == LocalizationService.Get("ArtworkPreview.Loading"), "Deferred state replaced active loading.");
            ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await operation;
            handler.Hold = false;
            await Activate(next, false);
            await Activate(slow, false);
            AssertPlaceholder("ArtworkPreview.Deferred");
            await Activate(slow, true);
            CoverAssert(poster.Source is not null && fanart.Source is not null, "Preview did not recover after cancellation.");
            Console.WriteLine("UI PASS deferredPreviewFourLanguages=True deferredPreviewNoHttp=True cachedPreviewRetained=True " +
                "batchSaveNextPreviewDeferred=True missingNotDeferred=True failedCacheNotDeferred=True cancelPreviewRecovery=True");
        }
        finally { window.Close(); LocalizationService.ApplyLanguage(originalLanguage); }
    }
}
