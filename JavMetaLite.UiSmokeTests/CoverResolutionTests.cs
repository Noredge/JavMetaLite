using System.Collections;
using System.ComponentModel;
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
    private static int RunCoverResolutionTests(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/cover-resolution");
        AppLog.ConfigureDirectory(Path.Combine(root, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        RunStabilityTask(TestCoverResolutionAsync(root));
        return 0;
    }

    private static void CoverAssert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Cover check: " + message);
    }

    private static MovieJob CoverJob(string id, string location = "https://cover.invalid/low.png")
    {
        var job = new MovieJob();
        job.ResetForVideo($@"C:\Synthetic\{id}.mp4", id);
        var dmm = new MovieMetadata { Id = id, SourceName = "libredmm", CoverUrl = location,
            ScreenshotUrls = ["https://cover.invalid/sample.png"] };
        var r18 = new MovieMetadata { Id = id, SourceName = "r18dev", CoverUrl = "https://cover.invalid/normal.png" };
        job.ApplyOnlineSources(dmm, [dmm, r18]);
        return job;
    }

    private static async Task TestCoverResolutionAsync(string root)
    {
        Directory.CreateDirectory(root);
        await TestCoverResolutionPolicyAsync();
        await TestCoverMonitorBoundsAsync();
        await TestCoverMonitorStaleAsync();
        await TestCoverResolutionUiAsync(root);
        await TestTinyLocalCoverResolutionAsync(Path.Combine(root, "tiny-local"));
        await TestDeferredPreviewAsync(Path.Combine(root, "deferred-preview"));
        Console.WriteLine("UI PASS coverResolutionThresholds=True actualFallback=True selectedSourceOnly=True " +
            "coverCheckConcurrency=2 coverCheckPauseCancel=True staleCoverIgnored=True unknownNotLow=True " +
            "coverDimensionsCache=True coverIssueFilter=True coverWarningAllowsSave=True coverFourLanguages=True");
    }

    private static async Task TestCoverResolutionPolicyAsync()
    {
        foreach (var sample in new[] { (400, 269, true), (599, 400, true), (600, 400, false),
                     (800, 539, false), (0, 0, false), (400, 539, false), (400, 400, false),
                     (147, 200, true), (200, 147, true), (200, 200, true), (399, 399, true),
                     (200, 399, true), (200, 400, false), (0, 200, false) })
            CoverAssert(new CoverResolutionCheck("fixture", sample.Item1, sample.Item2).IsLowResolution == sample.Item3,
                "Threshold/orientation boundary changed.");
        var seen = new List<string>();
        var result = await CoverResolutionCheck.MeasureAsync(["missing", "low", "unused"], (location, _) =>
        {
            seen.Add(location);
            return location == "missing" ? Task.FromException<ArtworkPixelSize>(new HttpRequestException("404"))
                : Task.FromResult(new ArtworkPixelSize(400, 269));
        }, default);
        CoverAssert(result.Location == "low" && result.IsLowResolution && seen.SequenceEqual(["missing", "low"]),
            "Must measure the first valid fallback, not probe every provider or candidate.");
        var unknown = await CoverResolutionCheck.MeasureAsync(["bad"], (_, _) =>
            Task.FromException<ArtworkPixelSize>(new InvalidDataException("invalid image")), default);
        CoverAssert(!unknown.IsMeasured && !unknown.IsLowResolution, "Failed probe became a low-quality verdict.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await CoverResolutionCheck.MeasureAsync(["unused"], (_, _) => throw new Exception("Must not probe"), canceled.Token);
            throw new Exception("Cancellation was swallowed");
        }
        catch (OperationCanceledException) { }
        var sidecar = ArtworkCoverCandidate.CreateSidecarPair(new("local", "Local", ""),
            @"C:\Synthetic\poster.jpg", @"C:\Synthetic\fanart.jpg");
        CoverAssert(CoverResolutionCheck.GetLocations(sidecar).SequenceEqual([sidecar.LocalFanartPath]),
            "Cropped local poster must not be measured as a complete cover.");
        CoverAssert(CoverResolutionCheck.GetLocations(sidecar with { LocalFanartPath = "" }).Count == 0,
            "Missing fanart must not fall back to the sidecar poster.");
        using var job = CoverJob("LOW-001");
        var review = job.ReviewRevision;
        var search = job.SearchState;
        job.InitializeSaveConfiguration(new(new SaveOptions(true, true, true, true, true, true), new OrganizationOptions(false, false)));
        CoverAssert(job.TrySetCoverResolution(job.CoverResolutionRevision, result), "Measurement rejected.");
        CoverAssert(job.ReviewRevision == review && job.SearchState == search && job.IsSelectedForBatch,
            "Advisory measurement mutated review/search/selection state.");
        job.BeginSave(review);
        CoverAssert(job.SaveState == MovieSaveState.Saving, "Low-resolution warning blocked saving.");
        job.MarkSaveCompleted();
        var revision = job.CoverResolutionRevision;
        job.SelectArtworkSource("r18dev");
        CoverAssert(job.CoverResolution is null && !job.TrySetCoverResolution(revision, result), "Old-source result survived selection.");
        job.TrySetCoverResolution(job.CoverResolutionRevision, result);
        job.BeginSearch();
        CoverAssert(!job.HasLowResolutionCover, "Re-search retained a stale warning.");
        job.MarkSearchCanceled();
        job.TrySetCoverResolution(job.CoverResolutionRevision, result);
        job.Metadata.Id = "LOW-002";
        CoverAssert(job.CoverResolution is null, "ID change retained a stale warning.");
        revision = job.CoverResolutionRevision;
        job.Dispose();
        CoverAssert(!job.TrySetCoverResolution(revision, result), "Disposed job accepted a late result.");
    }

    private static async Task TestCoverMonitorBoundsAsync()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var offUi = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var monitor = new CoverResolutionMonitor(dispatcher, async (locations, token) =>
        {
            if (dispatcher.CheckAccess()) offUi = false;
            Interlocked.Increment(ref calls);
            await gate.Task.WaitAsync(token);
            return new(locations[0], 400, 269);
        }, () => CoverAssert(dispatcher.CheckAccess(), "UI callback ran on worker."));
        var jobs = Enumerable.Range(0, 40).Select(i => CoverJob($"BOUND-{i:000}")).ToArray();
        monitor.Pause(true);
        foreach (var job in jobs) { monitor.Track(job); monitor.Track(job); }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        CoverAssert(calls == 0 && monitor.PendingCount == 40, "Paused/deduplicated queue started work.");
        monitor.Pause(false);
        await UntilStableAsync(() => calls == 2, "Two workers did not start.");
        CoverAssert(monitor.RunningCount == 2 && offUi, "Unbounded or UI-thread probing.");
        monitor.Pause(true);
        gate.SetResult();
        await UntilStableAsync(() => monitor.RunningCount == 0, "Paused in-flight checks did not drain.");
        CoverAssert(calls == 2 && monitor.PendingCount == 38, "Paused checks started another movie.");
        monitor.CancelAll();
        monitor.Pause(false);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        CoverAssert(calls == 2 && monitor.PendingCount == 0 && jobs.Count(j => j.HasLowResolutionCover) == 2,
            "Cancellation restarted checks or discarded completed findings.");
        foreach (var job in jobs) job.Dispose();
    }

    private static async Task TestCoverMonitorStaleAsync()
    {
        var oldGate = new TaskCompletionSource<CoverResolutionCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        using var monitor = new CoverResolutionMonitor(Dispatcher.CurrentDispatcher, (locations, _) =>
        {
            Interlocked.Increment(ref started);
            // Deliberately ignore cancellation: a late result must still be rejected.
            return locations[0].EndsWith("low.png") ? oldGate.Task
                : Task.FromResult(new CoverResolutionCheck(locations[0], 800, 539));
        }, () => { });
        using var job = CoverJob("STALE-001");
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MovieJob.CoverResolutionRevision) or nameof(MovieJob.SearchState)) monitor.Refresh(job);
        };
        monitor.Track(job);
        await UntilStableAsync(() => started == 1, "Old source did not start.");
        job.SelectArtworkSource("r18dev");
        await UntilStableAsync(() => job.CoverResolution?.Width == 800, "New source was not rechecked.");
        oldGate.SetResult(new("old", 400, 269));
        await UntilStableAsync(() => monitor.RunningCount == 0, "Old source did not drain.");
        CoverAssert(!job.HasLowResolutionCover && job.CoverResolution?.Width == 800, "Late old-source result overwrote current result.");

        var active = 0;
        using var canceledMonitor = new CoverResolutionMonitor(Dispatcher.CurrentDispatcher, async (_, token) =>
        {
            Interlocked.Increment(ref active);
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Interlocked.Decrement(ref active); }
            return new("never", 400, 269);
        }, () => { });
        using var canceledJob = CoverJob("CANCEL-001");
        canceledMonitor.Track(canceledJob);
        await UntilStableAsync(() => active == 1, "Cancelable probe did not start.");
        canceledMonitor.Forget(canceledJob);
        canceledJob.Dispose();
        await UntilStableAsync(() => active == 0 && canceledMonitor.RunningCount == 0, "Removal did not cancel/drain probe.");
        CoverAssert(canceledJob.CoverResolution is null, "Removed movie received a warning.");
        using var idJob = CoverJob("ID-001");
        idJob.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MovieJob.CoverResolutionRevision) or nameof(MovieJob.SearchState)) canceledMonitor.Refresh(idJob);
        };
        canceledMonitor.Track(idJob);
        await UntilStableAsync(() => active == 1, "ID probe did not start.");
        idJob.Metadata.Id = "ID-002";
        await UntilStableAsync(() => canceledMonitor.PendingCount == 0 && active == 0, "ID change kept checking old artwork.");
        using var closeJob = CoverJob("CLOSE-001");
        canceledMonitor.Track(closeJob);
        await UntilStableAsync(() => active == 1, "Close probe did not start.");
        canceledMonitor.Dispose();
        await UntilStableAsync(() => active == 0, "Closing did not cancel probe.");
    }

    private static async Task TestCoverResolutionUiAsync(string root)
    {
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
            using var job = CoverJob("LOW-053", "https://cover.invalid/missing.png");
            job.Metadata.FallbackCoverUrl = "https://cover.invalid/low.png";
            // Rebuild the selected candidate from the actual provider results, as a real search does.
            var dmm = job.SourceResults[0];
            dmm.FallbackCoverUrl = job.Metadata.FallbackCoverUrl;
            job.ApplyOnlineSources(dmm, job.SourceResults);
            typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!.Invoke(window, [job]);
            queue.Add(job);
            await (Task)typeof(MainWindow).GetMethod("ActivateMovieJobAsync", PrivateInstance)!.Invoke(window, [job, false])!;
            var monitor = (CoverResolutionMonitor)typeof(MainWindow).GetField("_coverResolutionMonitor", PrivateInstance)!.GetValue(window)!;
            var result = new BatchMetadataSearchResult([new(job, BatchMetadataSearchItemStatus.Succeeded, [], null)]);
            typeof(MainWindow).GetMethod("ShowBatchSearchSummary", PrivateInstance)!.Invoke(window, [result, "cover-test"]);
            await UntilStableAsync(() => monitor.PendingCount == 0, "Batch cover checks did not complete.");
            CoverAssert(job.HasLowResolutionCover && job.CoverResolution?.Dimensions == "400 × 269", "Fallback dimensions missing from queue.");
            CoverAssert(handler.Calls == 2, "Checked another provider or sample without selection.");
            var save = (Button)window.FindName("SaveSelectedButton");
            CoverAssert(save.IsEnabled, "Advisory disabled batch saving.");
            var filter = (ComboBox)window.FindName("QueueFilterComboBox");
            filter.SelectedItem = filter.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "issues");
            var list = (ListBox)window.FindName("MovieQueueList");
            window.UpdateLayout();
            CoverAssert(list.Items.Count == 1 && ((TextBlock)window.FindName("QueueCountText")).Text.Contains("1"), "Warning missing from issue filter.");
            foreach (var language in new[] { "zh-Hans", "zh-Hant", "en", "ja" })
            {
                var combo = (ComboBox)window.FindName("LanguageComboBox");
                combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == language);
                window.UpdateLayout();
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(job);
                var badge = FindVisualChildren<Border>(row).Single(b => b.Name == "QueueReviewBadge");
                var label = FindVisualChildren<TextBlock>(badge).Single();
                CoverAssert(badge.IsVisible && label.Text == LocalizationService.Get("Queue.Search.NeedsReview") &&
                    label.Foreground is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 183, 77),
                    "Badge did not localize: " + language);
                CoverAssert(!FindVisualChildren<TextBlock>(row).Any(t => t.Text.Contains("400") || t.Text == LocalizationService.Get("CoverCheck.Low")),
                    "Queue must show only yellow review status, not resolution reasons.");
                CoverAssert(FitsInside(badge, row), "Warning overflows queue row: " + language);
            }
            // Render synthetic metadata only, no real artwork or user media.
            var languageCombo = (ComboBox)window.FindName("LanguageComboBox");
            languageCombo.SelectedItem = languageCombo.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "zh-Hans");
            window.UpdateLayout();
            var visual = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            visual.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(visual));
            using (var stream = File.Create(Path.Combine(root, "cover-warning.png"))) encoder.Save(stream);
            job.SelectArtworkSource("r18dev");
            CoverAssert(!job.HasLowResolutionCover, "Selection did not immediately clear old warning.");
            await UntilStableAsync(() => job.CoverResolution?.Width == 800, "Selected source was not checked.");
            CoverAssert(list.Items.Count == 0 && handler.Calls == 3, "Resolved warning remained in issue filter.");
            var calls = handler.Calls;
            job.SelectArtworkSource("libredmm");
            await UntilStableAsync(() => job.HasLowResolutionCover, "Returning source was not checked.");
            // Failed preferred URL is intentionally still tried; successful fallback uses cached dimensions.
            CoverAssert(handler.Calls == calls + 1, "Known fallback dimensions were downloaded again.");
            job.SelectArtworkSource("r18dev");
            await UntilStableAsync(() => monitor.PendingCount == 0, "Known source check failed.");
            CoverAssert(handler.Calls == calls + 1, "Known full-cover dimensions were downloaded again.");

            handler.Hold = true;
            job.Metadata.CoverUrl = "https://cover.invalid/held.png";
            job.SetManualArtwork(ArtworkCoverCandidate.CreateCompleteCover(MetadataCandidateSource.Manual, job.Metadata.CoverUrl));
            await UntilStableAsync(() => handler.Pending == 1, "Stop-button check did not start.");
            var stop = (Button)window.FindName("CancelOperationButton");
            CoverAssert(stop.IsVisible && stop.IsEnabled && !IsWindowBusy(window), "Background checks made the editor busy or lacked stop control.");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilStableAsync(() => handler.Pending == 0 && monitor.RunningCount == 0, "Stop button failed to drain HTTP.");
            CoverAssert(!stop.IsVisible && job.CoverResolution is null, "Canceled check became a completed verdict.");
        }
        finally { window.Close(); LocalizationService.ApplyLanguage(originalLanguage); }
    }

    private sealed class CoverCheckHandler : HttpMessageHandler
    {
        private readonly byte[] _low = StabilityImage(400, 269);
        private readonly byte[] _normal = StabilityImage(800, 539);
        private int _calls, _pending;
        public int Calls => Volatile.Read(ref _calls);
        public int Pending => Volatile.Read(ref _pending);
        public bool Hold { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "cover.invalid") throw new InvalidOperationException("Live network forbidden in cover tests.");
            Interlocked.Increment(ref _calls);
            if (Hold)
            {
                Interlocked.Increment(ref _pending);
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { Interlocked.Decrement(ref _pending); }
            }
            return request.RequestUri.AbsolutePath == "/missing.png" ? new(HttpStatusCode.NotFound)
                : new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri.AbsolutePath == "/normal.png" ? _normal : _low) };
        }
    }
}
