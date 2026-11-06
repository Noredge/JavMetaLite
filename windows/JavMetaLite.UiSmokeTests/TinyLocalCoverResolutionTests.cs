using System.Collections;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static async Task TestTinyLocalCoverResolutionAsync(string root)
    {
        Directory.CreateDirectory(root);
        var examples = new[]
        {
            (Id: "FNS-121", Width: 147, Height: 200, Kind: "valid", Low: true),
            (Id: "TNY-002", Width: 800, Height: 539, Kind: "valid", Low: false),
            (Id: "TNY-003", Width: 200, Height: 200, Kind: "valid", Low: true),
            (Id: "TNY-004", Width: 400, Height: 539, Kind: "valid", Low: false),
            (Id: "TNY-005", Width: 147, Height: 200, Kind: "poster-only", Low: false),
            (Id: "TNY-006", Width: 147, Height: 200, Kind: "corrupt", Low: false)
        };
        var paths = new List<string>();
        var unchanged = new Dictionary<string, byte[]>();
        foreach (var example in examples)
        {
            var directory = Path.Combine(root, example.Id);
            Directory.CreateDirectory(directory);
            var video = Path.Combine(directory, example.Id + ".mp4");
            var nfo = Path.ChangeExtension(video, ".nfo");
            await File.WriteAllTextAsync(video, "synthetic movie, not playable");
            await File.WriteAllTextAsync(nfo, $"<movie><id>{example.Id}</id><title>Synthetic cover test</title></movie>");
            unchanged[video] = SHA256.HashData(await File.ReadAllBytesAsync(video));
            unchanged[nfo] = SHA256.HashData(await File.ReadAllBytesAsync(nfo));
            await File.WriteAllBytesAsync(Path.Combine(directory, example.Id + "-poster.jpg"), StabilityImage(147, 200));
            if (example.Kind == "corrupt")
                await File.WriteAllTextAsync(Path.Combine(directory, example.Id + "-fanart.jpg"), "not an image");
            else if (example.Kind == "valid")
                await File.WriteAllBytesAsync(Path.Combine(directory, example.Id + "-fanart.jpg"), StabilityImage(example.Width, example.Height));
            paths.Add(video);
        }

        var loaded = await MovieQueueImport.LoadBatchAsync(MovieFileSet.Group(paths).ToArray(), default);
        CoverAssert(loaded.Count == examples.Length && loaded.All(item => item.Error is null), "Local queue import failed.");
        foreach (var example in examples)
        {
            var item = loaded.Single(item => item.Job!.Metadata.Id == example.Id);
            var job = item.Job!;
            CoverAssert(job.HasLowResolutionCover == example.Low, "Unopened local movie was not classified: " + example.Id);
            CoverAssert(job.SearchState == MovieSearchState.Searchable && job.CanBatchSearch,
                "Local quality hint changed search eligibility.");
            if (example.Kind == "valid")
                CoverAssert(ReferenceEquals(job.CoverResolution, item.Result!.ArtworkDiscovery!.FanartResolution) &&
                    job.CoverResolution?.Width == example.Width && job.CoverResolution.Height == example.Height,
                    "Import must reuse the exact measurement from sidecar validation.");
            else
                CoverAssert(job.CoverResolution is null, "Missing/corrupt fanart fell back to cropped poster or became measured.");
            var reviewRevision = job.ReviewRevision;
            job.InitializeSaveConfiguration(new(new SaveOptions(true, true, true, true, true, true), new OrganizationOptions(false, false)));
            job.BeginSave(reviewRevision);
            job.MarkSaveCanceled();
        }

        var language = LocalizationService.CurrentLanguageCode;
        using var handler = new NoLocalCoverNetworkHandler();
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "settings")))
            { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(handler));
        window.Show();
        var first = loaded.Single(item => item.Job!.Metadata.Id == "FNS-121").Job!;
        var normal = loaded.Single(item => item.Job!.Metadata.Id == "TNY-002").Job!;
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            foreach (var item in loaded)
            {
                typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!.Invoke(window, [item.Job]);
                queue.Add(item.Job);
            }
            async Task Activate(MovieJob job) => await (Task)typeof(MainWindow)
                .GetMethod("ActivateMovieJobAsync", PrivateInstance)!.Invoke(window, [job, true])!;
            await Activate(first);
            var monitor = (CoverResolutionMonitor)typeof(MainWindow).GetField("_coverResolutionMonitor", PrivateInstance)!.GetValue(window)!;
            CoverAssert(handler.Calls == 0 && monitor.PendingCount == 0, "Local load/preview started online checks.");
            var hint = (TextBlock)window.FindName("FanartHintText");
            CoverAssert(hint.Text.Contains("147 × 200") && first.HasLowResolutionCover,
                "147x200 local preview did not update the advisory.");
            var filter = (ComboBox)window.FindName("QueueFilterComboBox");
            filter.SelectedItem = filter.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "issues");
            var list = (ListBox)window.FindName("MovieQueueList");
            window.UpdateLayout();
            CoverAssert(list.Items.Count == 2, "Local tiny covers are absent from the issue filter.");
            foreach (var code in new[] { "zh-Hans", "zh-Hant", "en", "ja" })
            {
                var languageBox = (ComboBox)window.FindName("LanguageComboBox");
                languageBox.SelectedItem = languageBox.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == code);
                window.UpdateLayout();
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(first);
                var badge = FindVisualChildren<Border>(row).Single(b => b.Name == "QueueReviewBadge");
                var text = FindVisualChildren<TextBlock>(badge).Single();
                CoverAssert(text.Text == LocalizationService.Get("Queue.Search.NeedsReview") &&
                    text.Foreground is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 183, 77),
                    "Local searchable movie must display yellow review status: " + code);
                CoverAssert(!FindVisualChildren<TextBlock>(row).Any(t => t.Text.Contains("147")) && FitsInside(badge, row),
                    "Queue leaks dimensions or clips the review label: " + code);
            }
            var languageCombo = (ComboBox)window.FindName("LanguageComboBox");
            languageCombo.SelectedItem = languageCombo.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "zh-Hans");
            window.UpdateLayout();
            var visual = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,
                (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            visual.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(visual));
            using (var stream = File.Create(Path.Combine(root, "tiny-local-warning.png"))) encoder.Save(stream);

            filter.SelectedItem = filter.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == "all");
            await Activate(normal);
            var fanart = first.LocalArtworkCandidate!.LocalFanartPath;
            using (var locked = new FileStream(fanart, FileMode.Open, FileAccess.Read, FileShare.None))
                await Activate(first); // Cached preview: opening the file again would fail.
            CoverAssert(first.HasLowResolutionCover && hint.Text.Contains("147 × 200"), "Cached revisit lost local quality state.");

            // Same-path local replacement: a fresh preview must use the newly read dimensions,
            // not a remote-style URL dimension cache or an earlier discovery result.
            await File.WriteAllBytesAsync(fanart, StabilityImage(800, 539));
            await (Task)typeof(MainWindow).GetMethod("LoadSelectedArtworkPreviewAsync", PrivateInstance)!.Invoke(window, null)!;
            CoverAssert(!first.HasLowResolutionCover && first.CoverResolution?.Width == 800, "Fresh local preview retained stale low dimensions.");
            await File.WriteAllBytesAsync(fanart, StabilityImage(147, 200));
            first.SetLocalArtwork(first.LocalArtworkCandidate);
            CoverAssert(first.CoverResolution is null, "Rebuilt source retained old dimensions.");
            await (Task)typeof(MainWindow).GetMethod("LoadSelectedArtworkPreviewAsync", PrivateInstance)!.Invoke(window, null)!;
            CoverAssert(first.HasLowResolutionCover, "Local source revisit was not checked without an online search.");
            await File.WriteAllTextAsync(fanart, "now corrupt");
            await (Task)typeof(MainWindow).GetMethod("LoadSelectedArtworkPreviewAsync", PrivateInstance)!.Invoke(window, null)!;
            CoverAssert(first.CoverResolution?.IsMeasured == false && !first.HasLowResolutionCover,
                "Failed fresh preview reused a stale low-resolution verdict.");
            foreach (var file in unchanged)
                CoverAssert(SHA256.HashData(await File.ReadAllBytesAsync(file.Key)).SequenceEqual(file.Value), "Quality checking modified NFO/video.");
            CoverAssert(handler.Calls == 0 && monitor.PendingCount == 0, "Local checking performed HTTP or queued a second read.");
        }
        finally
        {
            window.Close();
            foreach (var item in loaded) item.Job?.Dispose();
            LocalizationService.ApplyLanguage(language);
        }
        await TestCoverPreviewObservationAsync();
        Console.WriteLine("UI PASS tiny147x200=True tinySquare=True localImportReusesValidation=True unopenedLocalQueueFlags=True " +
            "localPreviewUpdates=True localCacheRevisit=True localCoverHttp=0 localFilesPreserved=True localYellowReviewFourLanguages=True");
    }

    private static async Task TestCoverPreviewObservationAsync()
    {
        using var job = CoverJob("OBS-001");
        var gate = new TaskCompletionSource<CoverResolutionCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        using var monitor = new CoverResolutionMonitor(Dispatcher.CurrentDispatcher, (_, _) =>
        {
            started = true;
            return gate.Task; // Ignores cancellation to reproduce a late background reply.
        }, () => { });
        monitor.Track(job);
        await UntilStableAsync(() => started, "Background observation test did not start.");
        var revision = job.CoverResolutionRevision;
        var review = job.ReviewRevision;
        monitor.Observe(job, revision, new("preview", 147, 200));
        CoverAssert(job.HasLowResolutionCover && monitor.PendingCount == 0 && review == job.ReviewRevision,
            "Known preview measurement started another read or changed the review revision.");
        gate.SetResult(new("old", 800, 539));
        await UntilStableAsync(() => monitor.RunningCount == 0, "Background measurement did not drain.");
        CoverAssert(job.CoverResolution?.Width == 147, "Late background result replaced current preview dimensions.");
        job.SelectArtworkSource("r18dev");
        monitor.Observe(job, revision, new("stale-preview", 147, 200));
        CoverAssert(job.CoverResolution is null, "Old-source preview measurement crossed a source change.");
    }

    private sealed class NoLocalCoverNetworkHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Local artwork checks must not access the network.");
        }
    }
}
