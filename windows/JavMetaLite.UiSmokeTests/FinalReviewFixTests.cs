using BatchSavePathConflicts = JavMetaLite.App.BatchSavePathConflicts;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static int RunFinalReviewFixTests(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/final-review-fixes");
        AppLog.ConfigureDirectory(Path.Combine(root, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        RunStabilityTask(TestFinalReviewFixesAsync(root));
        TestReadabilityCloseout();
        return 0;
    }

    private static async Task TestFinalReviewFixesAsync(string root)
    {
        Directory.CreateDirectory(root);
        await TestArtworkDownloadDeadlineAsync(Path.Combine(root, "preview-deadline"));
        await TestViewerRestartsFaultedImageAsync();
        await TestViewerApplyPreviewLifecycleAsync(Path.Combine(root, "viewer"));
        await TestRetryComparisonCancellationAsync(Path.Combine(root, "retry"));
        await TestBatchPathConflictPreviewAsync(Path.Combine(root, "batch"));
        await TestSkippedBatchInputConflictAsync(Path.Combine(root, "batch-unavailable-target"));
        await TestSavePreflightCancellationAsync(Path.Combine(root, "save-preflight"));
        Console.WriteLine("UI PASS viewerApplyBusy=True lateMainPreviewRejected=True retryComparisonCancelRecovers=True batchPathConflictsBlocked=True sharedExtrafanartNamespace=True");
    }

    private static async Task TestSkippedBatchInputConflictAsync(string root)
    {
        foreach (var locked in new[] { false, true })
        {
            var source = Directory.CreateDirectory(Path.Combine(root, locked ? "locked" : "directory")).FullName;
            var sharedArtwork = Path.Combine(Directory.CreateDirectory(Path.Combine(source, "extrafanart")).FullName, "local.jpg");
            File.WriteAllText(sharedArtwork, "shared fixture artwork");
            var window = new MainWindow(new AppPreferencesStore(Path.Combine(source, "settings")))
                { ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var organization = new OrganizationOptions(OrganizationTargetMode.VideoDirectory, false);
                var metadataOnly = new SaveOptions(true, false, false, false, false, false);
                MovieJob Add(string directory, string id, SaveOptions options, int samples = 0)
                {
                    var path = Path.Combine(directory, id + ".mp4");
                    File.WriteAllText(path, id);
                    var job = FinalReviewAddJob(window, id, path, FinalReviewMetadata(id, "libredmm", "blue", samples));
                    job.InitializeSaveConfiguration(new(options, organization));
                    job.IsSelectedForBatch = true;
                    return job;
                }

                var writer = Add(source, "ONE-011", metadataOnly with
                {
                    DownloadExtrafanart = true, ReplaceLocalExtrafanart = true
                }, samples: 1);
                var skipped = Add(source, "TWO-012", metadataOnly);
                var independent = Add(Directory.CreateDirectory(Path.Combine(source, "separate")).FullName,
                    "THR-013", metadataOnly);
                writer.SetLocalExtrafanart([sharedArtwork]);
                skipped.SetLocalExtrafanart([sharedArtwork]);
                await FinalReviewActivate(window, writer, false);

                var badTarget = Path.ChangeExtension(skipped.VideoPath!, ".nfo");
                if (locked) File.WriteAllText(badTarget, "locked NFO");
                else Directory.CreateDirectory(badTarget);
                using var targetLock = locked ? File.Open(badTarget, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;

                _ = window.Dispatcher.BeginInvoke(() => ((Button)window.FindName("SaveSelectedButton"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                await UntilStableAsync(() => Application.Current.Windows.OfType<BatchSavePreviewWindow>().Any(),
                    "Unavailable target did not produce a batch preview.");
                var preview = Application.Current.Windows.OfType<BatchSavePreviewWindow>().Single();
                CoverAssert(preview.Items.Count == 1 && preview.Items[0].Job == independent,
                    "A failed preflight exposed shared artwork to another save or blocked an independent save.");
                CoverAssert(preview.Issues.Single(issue => issue.Job == skipped).Message.Contains(badTarget) &&
                    preview.Issues.Single(issue => issue.Job == writer).Message.Contains(sharedArtwork),
                    "Preview did not explain both the preflight and shared input conflicts.");
                preview.DialogResult = false;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                CoverAssert(File.ReadAllText(sharedArtwork) == "shared fixture artwork",
                    "Batch preview changed shared artwork.");
            }
            finally
            {
                foreach (var dialog in Application.Current.Windows.OfType<BatchSavePreviewWindow>()
                    .Where(dialog => dialog.Owner == window).ToArray())
                    dialog.Close();
                window.Close();
            }
        }
    }

    private static async Task TestSavePreflightCancellationAsync(string root)
    {
        var window = new MainWindow(new AppPreferencesStore(root)) { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var preflight = window.RunSavePreflightAsync(async token =>
            {
                started.SetResult(window.Dispatcher.CheckAccess());
                await Task.Delay(Timeout.Infinite, token);
                return "unexpected preview";
            });

            CoverAssert(!await started.Task.WaitAsync(TimeSpan.FromSeconds(10)), "Save preflight ran on the dispatcher.");
            var cancel = (Button)window.FindName("CancelOperationButton");
            CoverAssert(IsWindowBusy(window) && cancel.IsVisible && cancel.IsEnabled, "Preflight cannot be canceled.");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CoverAssert(await preflight.WaitAsync(TimeSpan.FromSeconds(10)) is null && !IsWindowBusy(window),
                "Canceled preflight returned a preview or kept the window busy.");
            CoverAssert(await window.RunSavePreflightAsync(_ => Task.FromResult("retry")) == "retry",
                "Save preflight could not be retried.");
        }
        finally { window.Close(); }
    }

    private static object FinalReviewField(object target, string name) =>
        target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static object? FinalReviewInvoke(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethods(PrivateInstance).Single(method => method.Name == name &&
            method.GetParameters().Length == arguments.Length && method.GetParameters().Zip(arguments)
                .All(pair => pair.Second is null || pair.First.ParameterType.IsInstanceOfType(pair.Second)))
            .Invoke(target, arguments);

    private static MovieMetadata FinalReviewMetadata(string id, string source, string marker, int samples = 0) => new()
    {
        Id = id, Title = id, SourceName = source, SourceDisplayName = source,
        CoverUrl = $"https://final-review.invalid/{id}-{source}-{marker}.png",
        ScreenshotUrls = Enumerable.Range(1, samples).Select(i => $"https://final-review.invalid/{id}-{i}.png").ToArray()
    };

    private static MovieJob FinalReviewAddJob(MainWindow window, string id, string path, params MovieMetadata[] sources)
    {
        var job = new MovieJob();
        job.ResetForVideo(path, id);
        job.ApplyOnlineSources(sources[0], sources);
        FinalReviewInvoke(window, "AttachMovieJob", job);
        ((IList)FinalReviewField(window, "_movieQueue")).Add(job);
        return job;
    }

    private static Task FinalReviewActivate(MainWindow window, MovieJob job, bool load) =>
        (Task)FinalReviewInvoke(window, "ActivateMovieJobAsync", job, load)!;

    private static void FinalReviewSelectSource(MainWindow window, string source)
    {
        var combo = (ComboBox)window.FindName("SourceComboBox");
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == source);
    }

    private static byte[] FinalReviewColorImage(Color color)
    {
        var pixels = new byte[800 * 540 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(800, 540, 96, 96, PixelFormats.Bgra32, null, pixels, 800 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    private static bool FinalReviewPreviewIsBlue(MainWindow window, string name)
    {
        if (((Image)window.FindName(name)).Source is not BitmapSource bitmap) return false;
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(converted.PixelWidth / 2, converted.PixelHeight / 2, 1, 1), pixel, 4, 0);
        return pixel[0] > 200 && pixel[2] < 20;
    }

    private static async Task TestArtworkDownloadDeadlineAsync(string root)
    {
        using var client = new HttpClient(new FinalReviewHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StalledPreviewContent() })))
        {
            Timeout = TimeSpan.FromMilliseconds(150)
        };
        var window = new MainWindow(new AppPreferencesStore(root));
        ReplaceStabilityClient(window, "_previewHttpClient", client);
        using var cleanup = new CancellationTokenSource();
        try
        {
            var download = (Task<byte[]>)FinalReviewInvoke(window, "DownloadPreviewImageAsync",
                "https://preview.invalid/cover.jpg", cleanup.Token)!;
            try
            {
                await download.WaitAsync(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("A stalled preview body returned successfully.");
            }
            catch (InvalidDataException error)
            {
                CoverAssert(error.InnerException is IOException { InnerException: OperationCanceledException },
                    "The preview deadline did not cancel the response body.");
            }
        }
        finally { cleanup.Cancel(); window.Close(); }
    }

    private static async Task TestViewerRestartsFaultedImageAsync()
    {
        var other = new ArtworkViewerItem("Initial fixture",
            BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4));
        var item = new ArtworkViewerItem("Fixture", null, "https://preview.invalid/retry.jpg");
        var calls = 0;
        Func<ArtworkViewerItem, CancellationToken, Task<ArtworkViewerLoadedImage>> loader = (_, _) =>
        {
            if (++calls == 1) throw new IOException("Synthetic synchronous image failure.");
            return Task.FromResult(new ArtworkViewerLoadedImage(
                BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4), 1, 1));
        };
        // The constructor's preload encounters the synchronous failure.
        var viewer = new ArtworkViewerWindow([other, item], 0, loader);
        try
        {
            CoverAssert(calls == 1 && item.Image is null && item.LoadTask is null,
                "The initial image preload did not clear its failed load.");
            var retry = (Task<ArtworkViewerLoadedImage>)FinalReviewInvoke(viewer, "EnsureImageLoadedAsync", item)!;
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
            CoverAssert(calls == 2 && item.Image is not null, "Revisiting the image did not retry its failed load.");
        }
        finally { viewer.Close(); }
    }

    private static async Task TestViewerApplyPreviewLifecycleAsync(string root)
    {
        var red = FinalReviewColorImage(Colors.Red); var blue = FinalReviewColorImage(Colors.Blue);
        var phase = 0; var blockCanceledViewer = true;
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        using var handler = new FinalReviewHandler(async (request, token) =>
        {
            if (phase == 0) await Task.Delay(Timeout.Infinite, token); // Viewer-only preloads must not warm the cache.
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("A-001-r18dev") || url.Contains("LATE-003"))
            { entered.TrySetResult(); await release.Task.WaitAsync(token); }
            if (url.Contains("CANCEL-004") && blockCanceledViewer)
            { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(url.Contains("B-002") ? blue : red) };
        });
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "settings")))
            { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(handler));
        window.Show();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var a = FinalReviewAddJob(window, "A-001", @"C:\Synthetic\A-001.mp4",
                FinalReviewMetadata("A-001", "libredmm", "red"), FinalReviewMetadata("A-001", "r18dev", "red"));
            var b = FinalReviewAddJob(window, "B-002", @"C:\Synthetic\B-002.mp4", FinalReviewMetadata("B-002", "libredmm", "blue"));
            await FinalReviewActivate(window, a, false);
            _ = window.Dispatcher.BeginInvoke(() =>
                ((Button)window.FindName("ArtworkViewerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await UntilStableAsync(() => Application.Current.Windows.OfType<ArtworkViewerWindow>().Any(), "Viewer did not open.");
            var viewer = Application.Current.Windows.OfType<ArtworkViewerWindow>().Single();
            var option = ((IEnumerable)FinalReviewField(viewer, "_artworkSourceOptions")).Cast<object>()
                .Single(item => item.GetType().GetProperty("Name")!.GetValue(item)?.ToString() == "r18dev");
            FinalReviewInvoke(viewer, "SelectArtworkSource", option);
            phase = 1;
            ((Button)viewer.FindName("ApplySelectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            CoverAssert(IsWindowBusy(window) && !((ListBox)window.FindName("MovieQueueList")).IsEnabled,
                "Viewer source application did not own the normal busy lifecycle.");
            FinalReviewInvoke(window, "NavigateQueue", 1);
            CoverAssert(ReferenceEquals(FinalReviewField(window, "_activeJob"), a), "Busy viewer reload allowed movie navigation.");
            release.TrySetResult();
            await UntilStableAsync(() => !IsWindowBusy(window), "Viewer application did not finish.");
            ((ListBox)window.FindName("MovieQueueList")).SelectedItem = b;
            await UntilStableAsync(() => !IsWindowBusy(window) && ReferenceEquals(FinalReviewField(window, "_activeJob"), b), "Queue did not resume.");
            CoverAssert(FinalReviewPreviewIsBlue(window, "PosterImage") && FinalReviewPreviewIsBlue(window, "FanartImage"), "Viewer reload contaminated another movie.");

            // Deliberately bypass UI serialization to exercise the late-result guard itself.
            entered = new TaskCompletionSource(); release = new TaskCompletionSource();
            var late = FinalReviewAddJob(window, "LATE-003", @"C:\Synthetic\LATE-003.mp4", FinalReviewMetadata("LATE-003", "libredmm", "red"));
            await FinalReviewActivate(window, late, false);
            var pending = (Task)FinalReviewInvoke(window, "LoadSelectedArtworkPreviewAsync")!;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await FinalReviewActivate(window, b, true);
            release.TrySetResult(); await pending;
            FinalReviewInvoke(window, "CacheActivePreview");
            CoverAssert(FinalReviewPreviewIsBlue(window, "PosterImage") && FinalReviewPreviewIsBlue(window, "FanartImage"), "Late result overwrote the active movie.");
            await FinalReviewActivate(window, a, false); await FinalReviewActivate(window, b, false);
            CoverAssert(FinalReviewPreviewIsBlue(window, "PosterImage"), "A stale preview entered the next movie's cache.");

            phase = 0; entered = new TaskCompletionSource();
            var canceled = FinalReviewAddJob(window, "CANCEL-004", @"C:\Synthetic\CANCEL-004.mp4",
                FinalReviewMetadata("CANCEL-004", "libredmm", "red"), FinalReviewMetadata("CANCEL-004", "r18dev", "red"));
            await FinalReviewActivate(window, canceled, false);
            _ = window.Dispatcher.BeginInvoke(() => ((Button)window.FindName("ArtworkViewerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await UntilStableAsync(() => Application.Current.Windows.OfType<ArtworkViewerWindow>().Any(), "Cancellation viewer did not open.");
            viewer = Application.Current.Windows.OfType<ArtworkViewerWindow>().Single();
            option = ((IEnumerable)FinalReviewField(viewer, "_artworkSourceOptions")).Cast<object>()
                .Single(item => item.GetType().GetProperty("Name")!.GetValue(item)?.ToString() == "r18dev");
            FinalReviewInvoke(viewer, "SelectArtworkSource", option);
            phase = 1;
            ((Button)viewer.FindName("ApplySelectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilStableAsync(() => !IsWindowBusy(window), "Viewer application cancellation did not release the window.");
            var cache = (MoviePreviewCache)FinalReviewField(window, "_jobPreviews");
            CoverAssert(!cache.Contains(canceled) && canceled.ArtworkReview.SelectedCandidate?.Source.Name == "r18dev",
                "Canceled viewer load cached a blank result or reverted the user's applied source.");
            blockCanceledViewer = false;
            await FinalReviewActivate(window, b, true); await FinalReviewActivate(window, canceled, true);
            CoverAssert(((Image)window.FindName("PosterImage")).Source is not null && cache.Contains(canceled),
                "Viewer preview could not load after cancellation and revisit.");
        }
        finally { release.TrySetResult(); window.Close(); }
    }

    private static async Task TestRetryComparisonCancellationAsync(string root)
    {
        var entered = new TaskCompletionSource(); var blockImages = true;
        var image = FinalReviewColorImage(Colors.Blue); var requests = 0;
        using var api = new HttpClient(new FinalReviewHandler((request, token) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"dvd_id\":\"IPX-081\",\"title_en\":\"R18 fixture\",\"jacket_full_url\":\"https://final-review.invalid/recovered.png\"}") });
        }));
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "settings")))
            { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_r18DevClient", new R18DevClient(api, new R18RequestScheduler()));
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(new FinalReviewHandler(async (request, token) =>
        {
            if (blockImages) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) };
        })));
        window.Show();
        try
        {
            var dmm = FinalReviewMetadata("IPX-081", "libredmm", "blue");
            var job = FinalReviewAddJob(window, "IPX-081", @"C:\Synthetic\IPX-081.mp4", dmm);
            job.ApplyOnlineSources(dmm, [dmm], [
                new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, dmm, null, 1),
                new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null, new IOException("fixture failure"), 0)]);
            await FinalReviewActivate(window, job, false);
            typeof(MainWindow).GetField("_customSourceProfile", PrivateInstance)!.SetValue(window,
                new MetadataSourcePreferenceProfile { ArtworkSource = MetadataSourcePreferenceProfile.BestArtworkResolution });
            FinalReviewSelectSource(window, "custom");
            var retry = (Button)window.FindName("RetryFailedSourcesButton");
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilStableAsync(() => !IsWindowBusy(window), "Comparison cancellation did not finish.");
            CoverAssert(job.SearchState == MovieSearchState.SearchCanceled && job.CanBatchSearch && job.HasPartialSourceFailure,
                "Canceled source comparison remained Searching or discarded prior sources.");
            blockImages = false;
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilStableAsync(() => !IsWindowBusy(window) && job.SearchState == MovieSearchState.NeedsReview, "Retry could not recover after comparison cancellation.");
            CoverAssert(!job.HasFailedSources && requests == 2, "Successful provider was repeated or failed-source recovery did not finish.");
        }
        finally { window.Close(); }
    }

    private static async Task TestBatchPathConflictPreviewAsync(string root)
    {
        Directory.CreateDirectory(root);
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "settings")))
            { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        var language = LocalizationService.CurrentLanguageCode;
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var options = new SaveOptions(false, false, false, true, false, true, true);
            var organization = new OrganizationOptions(OrganizationTargetMode.VideoDirectory, false);
            MovieJob Add(string id, int samples)
            {
                var path = Path.Combine(root, id + ".mp4"); File.WriteAllText(path, id + " fixture movie");
                var job = FinalReviewAddJob(window, id, path, FinalReviewMetadata(id, "libredmm", "blue", samples));
                job.InitializeSaveConfiguration(new MovieSaveConfiguration(options, organization));
                return job;
            }
            var a = Add("ONE-001", 1); var b = Add("TWO-002", 3);
            SavePlan Plan(MovieJob job, OrganizationOptions? target = null) => FileOrganizationService.BuildPlan(
                job.VideoPaths, job.Metadata, options, target ?? organization, job.CreateLocalSaveContext());
            BatchSavePreviewItem Item(MovieJob job, SavePlan plan) => new(job, plan, job.ReviewRevision);
            var aPlan = Plan(a); var bPlan = Plan(b);
            CoverAssert(!aPlan.HasBlockingConflicts && !bPlan.HasBlockingConflicts, "Fixture plans should be safe individually.");
            CoverAssert(BatchSavePathConflicts.Find([Item(a, aPlan), Item(b, bPlan)]).Count == 2,
                "Different sample counts did not block the shared extrafanart namespace.");
            var retired = Path.Combine(root, "extrafanart", "fanart99.jpg");
            var retirementOnly = aPlan with { Changes = [], SourcePathsToRetire = [retired] };
            CoverAssert(BatchSavePathConflicts.Find([Item(a, retirementOnly), Item(b, bPlan)]).Count == 2,
                "Retirement of a disjoint sample in a shared directory was missed.");
            var preserved = bPlan with { Changes = [new(PlannedChangeKind.KeepFile, "keep", retired)], SourcePathsToRetire = [] };
            CoverAssert(BatchSavePathConflicts.Find([Item(a, retirementOnly), Item(b, preserved)]).Count == 2,
                "A source retirement could invalidate another movie's retained file.");
            var readOnly = retirementOnly with { SourcePathsToRetire = [], SourceFileExpectations = [new(retired, "fixture", "read")] };
            CoverAssert(BatchSavePathConflicts.Find([Item(a, readOnly), Item(b, preserved)]).Count == 0,
                "Read-only sharing was incorrectly blocked.");
            var separate = new OrganizationOptions(OrganizationTargetMode.SourceNumberFolder, false);
            CoverAssert(BatchSavePathConflicts.Find([Item(a, Plan(a, separate)), Item(b, Plan(b, separate))]).Count == 0,
                "Independent ID folders were incorrectly blocked.");
            using var multipart = new MovieJob();
            var part1 = Path.Combine(root, "PART-003-cd1.mp4"); var part2 = Path.Combine(root, "PART-003-cd2.mp4");
            File.WriteAllText(part1, "part1 fixture"); File.WriteAllText(part2, "part2 fixture");
            multipart.ResetForVideos([part1, part2], "PART-003");
            var multiMetadata = FinalReviewMetadata("PART-003", "libredmm", "blue", 2);
            multipart.ApplyOnlineSources(multiMetadata, [multiMetadata]);
            CoverAssert(BatchSavePathConflicts.Find([Item(multipart, Plan(multipart, separate)), Item(a, Plan(a, separate))]).Count == 0,
                "Legitimate multipart naming was incorrectly blocked.");

            // An ordinary metadata collision is blocked even with blanket overwrite permission.
            var nfoPath = Path.Combine(root, "shared.nfo");
            var nfoA = aPlan with { Changes = [new(PlannedChangeKind.OverwriteFile, "NFO", nfoPath)] };
            var nfoB = bPlan with { Changes = [new(PlannedChangeKind.OverwriteFile, "NFO", nfoPath.ToUpperInvariant())] };
            CoverAssert(BatchSavePathConflicts.Find([Item(a, nfoA), Item(b, nfoB)]).Count == 2,
                "Case-insensitive metadata output collision bypassed the batch check.");

            await FinalReviewActivate(window, a, false);
            ((CheckBox)window.FindName("SkipSavePreviewCheckBox")).IsChecked = true;
            foreach (var code in UiLanguageCodes.Supported)
            {
                LocalizationService.ApplyLanguage(code);
                var message = LocalizationService.Get("Error.BatchOutputConflict", "fixture-path");
                CoverAssert(message.Contains("fixture-path") && !message.Contains("Error.BatchOutputConflict"), "Batch conflict translation missing.");
                _ = window.Dispatcher.BeginInvoke(() => ((Button)window.FindName("SaveSelectedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                await UntilStableAsync(() => Application.Current.Windows.OfType<BatchSavePreviewWindow>().Any(), "Skip preview bypassed batch path conflicts.");
                var preview = Application.Current.Windows.OfType<BatchSavePreviewWindow>().Single();
                CoverAssert(preview.Items.Count == 0 && preview.Issues.Count == 2 && !((Button)preview.FindName("ConfirmButton")).IsEnabled,
                    "Overlapping plans remained executable after preflight.");
                preview.DialogResult = false;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }

            // Reproduce the original risk: the user could approve each movie's existing
            // NFO overwrite while two apparently new sample outputs silently overlap.
            File.WriteAllText(Path.Combine(root, "ONE-001.nfo"), "existing A NFO");
            File.WriteAllText(Path.Combine(root, "TWO-002.nfo"), "existing B NFO");
            var overwritePromptOptions = options with { WriteNfo = true, OverwriteExisting = false };
            foreach (var job in new[] { a, b })
                job.UpdateSaveConfiguration(new MovieSaveConfiguration(overwritePromptOptions, organization));
            FinalReviewInvoke(window, "ApplySaveConfiguration", a.SaveConfiguration);
            _ = window.Dispatcher.BeginInvoke(() => ((Button)window.FindName("SaveSelectedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await UntilStableAsync(() => Application.Current.Windows.OfType<BatchSavePreviewWindow>().Any(), "Own-NFO overwrite fixture did not preview.");
            var ownNfoPreview = Application.Current.Windows.OfType<BatchSavePreviewWindow>().Single();
            ((CheckBox)ownNfoPreview.FindName("OverwriteConfirmCheckBox")).IsChecked = true;
            CoverAssert(ownNfoPreview.Items.Count == 0 && ownNfoPreview.Issues.Count == 2 &&
                !((Button)ownNfoPreview.FindName("ConfirmButton")).IsEnabled,
                "Approving existing NFO overwrite made cross-movie sample conflicts executable.");
            ownNfoPreview.DialogResult = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CoverAssert(!Directory.Exists(Path.Combine(root, "extrafanart")) &&
                File.ReadAllText(a.VideoPath!) == "ONE-001 fixture movie" && File.ReadAllText(b.VideoPath!) == "TWO-002 fixture movie",
                "Batch conflict preview changed fixture media or created artwork.");
            CoverAssert(File.ReadAllText(Path.Combine(root, "ONE-001.nfo")) == "existing A NFO" &&
                File.ReadAllText(Path.Combine(root, "TWO-002.nfo")) == "existing B NFO", "Conflict preview changed existing metadata.");
        }
        finally { LocalizationService.ApplyLanguage(language); window.Close(); }
    }

    private sealed class FinalReviewHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }

    private sealed class StalledPreviewContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
