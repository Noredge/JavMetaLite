using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestPerformanceCloseout()
    {
        TestRefreshCoalescing();
        TestPreviewBudget();
        TestLocalizationContracts();
        var task = TestImportAndLocalizedPreviewAsync();
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
        Console.WriteLine("UI PASS queueRefreshCoalescing=True selectedCount1000=True filteredQueuePreserved=True boundedPreviewCache=True originalDimensions=True previewLanguageRestore=True localizationPlaceholders=True localizedDiagnostics=True backgroundImport=True multipartDuplicates=True corruptArtworkPreservedChecks=True importCancellation=True");
    }

    private static void TestRefreshCoalescing()
    {
        var calls = 0;
        var viewRequested = false;
        var scheduler = new QueueRefreshCoordinator(Dispatcher.CurrentDispatcher, view => { calls++; viewRequested |= view; });
        for (var n = 0; n < 1000; n++) scheduler.Request(n == 4);
        scheduler.Flush();
        if (calls != 1 || !viewRequested) throw new InvalidOperationException("Refresh requests were not consolidated.");
        scheduler.Request(false);
        scheduler.Cancel();

        var window = new MainWindow();
        window.Show();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            var attach = typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!;
            var view = (ICollectionView)typeof(MainWindow).GetField("_movieQueueView", PrivateInstance)!.GetValue(window)!;
            for (var n = 0; n < 1000; n++)
            {
                var job = new MovieJob(); job.ResetForVideo($@"C:\Synthetic\PERF-{n:D4}.mp4", $"PERF-{n:D4}");
                attach.Invoke(window, [job]); queue.Add(job);
            }
            typeof(MainWindow).GetMethod("RefreshQueueUi", PrivateInstance)!.Invoke(window, []);
            var resets = 0;
            view.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
            var set = typeof(MainWindow).GetMethod("SetBatchSelection", PrivateInstance)!;
            set.Invoke(window, [false]);
            if (resets != 0 || queue.Cast<MovieJob>().Any(j => j.IsSelectedForBatch) ||
                ((Button)window.FindName("SaveSelectedButton")).IsEnabled)
                throw new InvalidOperationException("Deselect did not update counts or unnecessarily reset the view.");
            set.Invoke(window, [true]);
            if (resets != 0 || queue.Cast<MovieJob>().Any(j => !j.IsSelectedForBatch) ||
                !((Button)window.FindName("SaveSelectedButton")).IsEnabled)
                throw new InvalidOperationException("Select-all regressed.");
            var filter = (ComboBox)window.FindName("QueueFilterComboBox");
            filter.SelectedItem = filter.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "issues");
            var bad = (MovieJob)queue[0]!;
            bad.Metadata.Id = "";
            WaitForCondition(() => ((ListBox)window.FindName("MovieQueueList")).Items.Count == 1,
                "Filtered queue did not react to ID changes.");
            bad.Metadata.Id = "PERF-0000";
            WaitForCondition(() => ((ListBox)window.FindName("MovieQueueList")).Items.Count == 0,
                "Corrected movie remained in issues.");
        }
        finally { window.Close(); }
    }

    private static void TestPreviewBudget()
    {
        var cache = new MoviePreviewCache(3, 50000);
        var bitmap = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32); bitmap.Freeze();
        var state = new MoviePreviewState(bitmap, null, null, Visibility.Collapsed);
        using var first = new MovieJob(); using var second = new MovieJob(); using var third = new MovieJob();
        cache.Store(first, state); cache.Store(second, state);
        if (cache.Contains(first) || !cache.Contains(second) || cache.DecodedBytes != 40000)
            throw new InvalidOperationException("Decoded-byte budget did not evict old preview.");
        cache.Clear();
        var empty = new MoviePreviewState(null, null, null, Visibility.Collapsed);
        var lru = new MoviePreviewCache(2);
        lru.Store(first, empty); lru.Store(second, empty); lru.TryGetValue(first, out _); lru.Store(third, empty);
        if (!lru.Contains(first) || lru.Contains(second) || !lru.Contains(third))
            throw new InvalidOperationException("Preview cache is not LRU.");
        lru.Remove(first); lru.Clear();
        if (lru.Count != 0 || lru.DecodedBytes != 0) throw new InvalidOperationException("Preview removal retained entries.");
        cache.Store(first, state with { Fanart = bitmap });
        if (cache.Count != 0) throw new InvalidOperationException("Oversized preview entered the cache.");
    }

    private static void TestLocalizationContracts()
    {
        var language = LocalizationService.CurrentLanguageCode;
        var english = new ResourceDictionary { Source = new Uri("/JavMetaLite;component/Resources/Strings.en.xaml", UriKind.Relative) };
        static string[] Placeholders(string value) => Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[^{}]*)\}")
            .Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
        try
        {
            foreach (var code in new[] { "zh-Hans", "zh-Hant", "en", "ja" })
            {
                LocalizationService.ApplyLanguage(code);
                var dictionary = new ResourceDictionary { Source = new Uri($"/JavMetaLite;component/Resources/Strings.{code}.xaml", UriKind.Relative) };
                foreach (string key in english.Keys)
                {
                    if (english[key] is not string baseline) continue;
                    var value = (string)dictionary[key];
                    if (string.IsNullOrWhiteSpace(value) || !Placeholders(value).SequenceEqual(Placeholders(baseline)))
                        throw new InvalidOperationException($"Localization placeholder contract: {code}/{key}");
                    var format = CompositeFormat.Parse(value);
                    _ = string.Format(CultureInfo.CurrentUICulture, format, Enumerable.Repeat<object>(1234.5, format.MinimumArgumentCount).ToArray());
                }
                var diagnostic = new MovieFileDiscoveryDiagnostic("fixture", "固定中文")
                { Kind = MovieFileDiscoveryDiagnosticKind.PathUnavailable };
                var shown = DiscoveryDiagnosticText.Format(diagnostic);
                if (shown.Contains("固定中文") || !shown.Contains(LocalizationService.Get("Discovery.PathUnavailable")))
                    throw new InvalidOperationException("Known diagnostic was not localized.");
            }
            LocalizationService.ApplyLanguage("ja");
            var current = Application.Current.Resources.MergedDictionaries.Single(d => d.Source?.OriginalString.EndsWith("Strings.ja.xaml") == true);
            var saved = current["Discovery.PathUnavailable"];
            current.Remove("Discovery.PathUnavailable");
            try
            {
                if (LocalizationService.Get("Discovery.PathUnavailable") != english["Discovery.PathUnavailable"].ToString())
                    throw new InvalidOperationException("Missing translation did not fall back to English.");
            }
            finally { current["Discovery.PathUnavailable"] = saved; }
        }
        finally { LocalizationService.ApplyLanguage(language); }
    }

    private static async Task TestImportAndLocalizedPreviewAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "JavMetaLite-performance-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        try
        {
            var paths = CreatePerformanceFixtures(root, 2, artwork: true);
            var plan = await MovieQueueImport.PrepareAsync(paths.Concat(paths).ToArray(), paths, CancellationToken.None);
            if (plan.FileSets.Count != 2 || plan.DuplicatePathCount != 2) throw new InvalidOperationException("Duplicate import plan failed.");
            var multipart = Path.Combine(root, "MULTI-001-CD1.mp4");
            File.WriteAllBytes(multipart, []);
            File.WriteAllBytes(Path.Combine(root, "MULTI-001-CD2.mp4"), []);
            var partsPlan = await MovieQueueImport.PrepareAsync([multipart], [], CancellationToken.None);
            if (partsPlan.FileSets.Count != 1 || partsPlan.FileSets[0].Parts.Count != 2)
                throw new InvalidOperationException("Background planner lost multipart discovery.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await MovieQueueImport.LoadNewAsync(plan.FileSets[0], cancellation.Token); throw new InvalidOperationException("Canceled import loaded a job."); }
            catch (OperationCanceledException) { }
            var coverPath = Path.ChangeExtension(paths[0], null) + "-fanart.jpg";
            var preview = await ArtworkPreviewLoader.LoadLocalAsync(coverPath, CancellationToken.None);
            if (preview.Width != 1600 || preview.Height != 1000 || preview.Bitmap.PixelWidth != 640 || !preview.Bitmap.IsFrozen)
                throw new InvalidOperationException("Display decode changed original dimensions or thread safety.");
            var window = new MainWindow(); window.Show();
            try
            {
                await (Task)typeof(MainWindow).GetMethod("AddMovieFilesAsync", PrivateInstance)!.Invoke(window, [paths])!;
                var jobs = ((IEnumerable)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!).Cast<MovieJob>().ToArray();
                var activate = typeof(MainWindow).GetMethod("ActivateMovieJobAsync", PrivateInstance)!;
                var languages = (ComboBox)window.FindName("LanguageComboBox");
                foreach (var code in new[] { "zh-Hans", "en", "ja", "zh-Hant" })
                {
                    languages.SelectedItem = languages.Items.OfType<ComboBoxItem>().Single(i => i.Tag?.ToString() == code);
                    foreach (var job in jobs)
                    {
                        await (Task)activate.Invoke(window, [job, true])!;
                        if (((TextBlock)window.FindName("FanartHintText")).Text != LocalizationService.Get("Artwork.Dimensions", 1600, 1000))
                            throw new InvalidOperationException("Cached dimensions retained the previous UI language.");
                    }
                }
                TestViewerLoadsFullImage(window);
            }
            finally { window.Close(); }

            // All samples are still checked; a corrupt one is excluded and diagnosed, never accepted for speed.
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(paths[0])!, "extrafanart", "fanart2.jpg"), "corrupt-image");
            var loaded = await MovieQueueImport.LoadNewAsync(plan.FileSets[0], CancellationToken.None);
            using (loaded.Job)
                if (loaded.Job.LocalExtrafanartPaths.Count != 3 || loaded.Result.ArtworkDiscovery?.Diagnostics.Count != 1)
                    throw new InvalidOperationException("Background import weakened artwork validation.");
            using var midBatchCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
            try
            {
                var batch = await MovieQueueImport.LoadBatchAsync(Enumerable.Repeat(plan.FileSets[0], 16).ToArray(), midBatchCancellation.Token);
                foreach (var item in batch) item.Job?.Dispose();
                throw new InvalidOperationException("Mid-batch cancellation was not observed.");
            }
            catch (OperationCanceledException) when (midBatchCancellation.IsCancellationRequested) { }
        }
        finally
        {
            LocalizationService.ApplyLanguage(originalLanguage);
            // Exact, test-owned generated directory; never accepts caller paths.
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestViewerLoadsFullImage(MainWindow main)
    {
        Exception? error = null;
        var checkedImage = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var viewer = Application.Current.Windows.OfType<ArtworkViewerWindow>().FirstOrDefault();
            if (viewer is null) return;
            try
            {
                var items = (IEnumerable<ArtworkViewerItem>)typeof(ArtworkViewerWindow).GetField("_items", PrivateInstance)!.GetValue(viewer)!;
                var fanart = items.First(i => i.Kind == ArtworkViewerItemKind.Fanart);
                if (fanart.Image is null && DateTime.UtcNow < deadline) return;
                if (fanart.PixelWidth != 1600 || fanart.PixelHeight != 1000 || fanart.Image is not BitmapSource { PixelWidth: 1600 })
                    throw new InvalidOperationException("Viewer reused reduced main-panel preview as a full image.");
                checkedImage = true;
            }
            catch (Exception exception) { error = exception; }
            finally
            {
                if (checkedImage || error is not null) { timer.Stop(); viewer.Close(); }
            }
        };
        timer.Start();
        try { typeof(MainWindow).GetMethod("OpenArtworkViewer", PrivateInstance)!.Invoke(main, [ArtworkViewerItemKind.Fanart]); }
        finally { timer.Stop(); }
        if (error is not null) throw error;
        if (!checkedImage) throw new InvalidOperationException("Full-image viewer check did not run.");
    }
}
