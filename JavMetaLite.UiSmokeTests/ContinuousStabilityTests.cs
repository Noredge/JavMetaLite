using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    // Optional soak, not a timing gate. One live window for all rounds; only synthetic files/HTTP.
    private static async Task RunContinuousStabilityAsync(string root, int count, int rounds)
    {
        var fixtures = await Task.Run(() => CreatePerformanceFixtures(Path.Combine(root, "synthetic-fixtures"), count, artwork: true));
        using var handler = new StabilitySearchHandler();
        using var http = new HttpClient(handler);
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "continuous-settings")))
        { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_libreDmmClient", new LibreDmmClient(http));
        ReplaceStabilityClient(window, "_r18DevClient", new R18DevClient(http));
        ReplaceStabilityClient(window, "_javLibraryClient", new JavLibraryClient(http));
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(handler, disposeHandler: false));
        window.Show();
        var rows = new List<StabilityRound>();
        var oldJobs = new List<WeakReference<MovieJob>>();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var source = (ComboBox)window.FindName("SourceComboBox");
            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(i => i.Tag?.ToString() == "libredmm");
            for (var round = 1; round <= rounds; round++)
            {
                var clock = Stopwatch.StartNew();
                var result = await RunStabilityRoundAsync(window, handler, fixtures);
                oldJobs.AddRange(result.Jobs);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                clock.Stop();
                // Not part of elapsed time. Weak references test ownership; working set is diagnostic only.
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var alive = oldJobs.Count(IsRetained);
                using var process = Process.GetCurrentProcess();
                var row = new StabilityRound(round, count, Math.Round(clock.Elapsed.TotalMilliseconds, 2),
                    result.MaxCacheCount, result.MaxCacheBytes, alive, handler.InFlight,
                    GC.GetTotalMemory(false), process.WorkingSet64, process.HandleCount, handler.Calls);
                rows.Add(row);
                Console.WriteLine(JsonSerializer.Serialize(row));
                File.WriteAllText(Path.Combine(root, "continuous.json"), JsonSerializer.Serialize(new
                {
                    Version = MainWindow.ApplicationVersion, Runtime = Environment.Version.ToString(),
                    TimestampUtc = DateTime.UtcNow, SameWindow = true,
                    Notes = "Synthetic NFO/JPEG import, duplicate import, 36 real queue selections, full viewer, four languages, canceled DMM search, 10% HTTP500 then failed-source retry, clear queue per round. No live HTTP or media saves. GC after timing; working set/handles are diagnostic, not leak assertions.",
                    Rows = rows
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (alive != 0 || handler.InFlight != 0)
                    throw new InvalidOperationException($"Cleared queue retained jobs/tasks: jobs={alive}, requests={handler.InFlight}.");
            }
        }
        finally { window.Close(); }
        Console.WriteLine($"STABILITY continuous PASS rounds={rounds} count={count} sameWindow=True");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsRetained(WeakReference<MovieJob> reference) => reference.TryGetTarget(out _);

    private sealed record StabilityRound(int Round, int Count, double ElapsedMs, int MaxCacheCount,
        long MaxCacheBytes, int RetainedRemovedJobs, int InFlightRequests, long RetainedManagedBytes,
        long WorkingSetBytes, int Handles, int CumulativeDmmCalls);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<MovieJob>[] Jobs, int MaxCacheCount, long MaxCacheBytes)>
        RunStabilityRoundAsync(MainWindow window, StabilitySearchHandler handler, string[] fixtures)
    {
        var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
        var cache = (MoviePreviewCache)typeof(MainWindow).GetField("_jobPreviews", PrivateInstance)!.GetValue(window)!;
        var loadResults = (IDictionary)typeof(MainWindow).GetField("_jobLoadResults", PrivateInstance)!.GetValue(window)!;
        async Task Invoke(string method, params object[] values)
        {
            var result = typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(window, values);
            if (result is Task task) await task;
        }
        async Task Click(string name)
        {
            var button = (Button)window.FindName(name);
            if (!button.IsEnabled) throw new InvalidOperationException($"Stability action disabled: {name}.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilStableAsync(() => !IsWindowBusy(window), $"Stability action timed out: {name}.", 120);
        }
        await Invoke("AddMovieFilesAsync", (object)fixtures);
        await Invoke("AddMovieFilesAsync", (object)fixtures);
        if (queue.Count != fixtures.Length) throw new InvalidOperationException("Repeated import changed queue count.");
        var weak = queue.Cast<MovieJob>().Select(j => new WeakReference<MovieJob>(j)).ToArray();
        var maxCount = 0;
        long maxBytes = 0;
        var list = (ListBox)window.FindName("MovieQueueList");
        for (var index = 0; index < Math.Min(36, queue.Count); index++)
        {
            list.SelectedItem = queue[index];
            await UntilStableAsync(() => !IsWindowBusy(window), "Queue preview did not settle.");
            if (((Image)window.FindName("PosterImage")).Source is null ||
                ((TextBlock)window.FindName("FanartHintText")).Text != LocalizationService.Get("Artwork.Dimensions", 1600, 1000))
                throw new InvalidOperationException("Queue preview has missing artwork or wrong dimensions.");
            maxCount = Math.Max(maxCount, cache.Count);
            maxBytes = Math.Max(maxBytes, cache.DecodedBytes);
            if (cache.Count > 12 || cache.DecodedBytes > 32 * 1024 * 1024)
                throw new InvalidOperationException("Preview cache exceeded its bounds.");
        }
        TestViewerLoadsFullImage(window);
        var language = (ComboBox)window.FindName("LanguageComboBox");
        foreach (var code in UiLanguageCodes.Supported)
        {
            language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(i => i.Tag?.ToString() == code);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (((TextBlock)window.FindName("FanartHintText")).Text != LocalizationService.Get("Artwork.Dimensions", 1600, 1000))
                throw new InvalidOperationException("Language switch lost original artwork dimensions.");
        }

        handler.Block = true;
        var search = (Button)window.FindName("SearchQueueButton");
        if (!search.IsEnabled) throw new InvalidOperationException("Imported queue cannot be searched.");
        search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilStableAsync(() => handler.InFlight > 0 && IsWindowBusy(window), "Canceled search never started.");
        ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilStableAsync(() => handler.InFlight == 0 && !IsWindowBusy(window), "Canceled search did not settle.");
        if (queue.Cast<MovieJob>().Any(j => j.SearchState == MovieSearchState.Searching) || handler.Canceled == 0)
            throw new InvalidOperationException("Canceled search left a searching job.");
        handler.Block = false;
        handler.Fail = true;
        var beforeSearch = handler.Calls;
        await Click("SearchQueueButton");
        var failed = queue.Cast<MovieJob>().Count(j => j.HasFailedSources);
        if (handler.Calls - beforeSearch != fixtures.Length || failed != fixtures.Length / 10 ||
            queue.Cast<MovieJob>().Any(j => j.SearchState is not (MovieSearchState.SearchFailed or MovieSearchState.NeedsReview)))
            throw new InvalidOperationException("Mixed search produced incorrect requests or states.");
        handler.Fail = false;
        var beforeRetry = handler.Calls;
        await Click("RetryFailedSourcesButton");
        if (handler.Calls - beforeRetry != failed || queue.Cast<MovieJob>().Any(j => j.HasFailedSources || j.SearchState != MovieSearchState.NeedsReview))
            throw new InvalidOperationException("Failed-source retry changed successful jobs or left failures.");
        await Click("ClearQueueButton");
        if (queue.Count != 0 || cache.Count != 0 || loadResults.Count != 0 ||
            ((Image)window.FindName("PosterImage")).Source is not null ||
            ((Image)window.FindName("FanartImage")).Source is not null)
            throw new InvalidOperationException("Clear queue left preview or load-state references.");
        return (weak, maxCount, maxBytes);
    }

    private sealed class StabilitySearchHandler : HttpMessageHandler
    {
        private int _calls, _inFlight, _canceled;
        public int Calls => Volatile.Read(ref _calls);
        public int InFlight => Volatile.Read(ref _inFlight);
        public int Canceled => Volatile.Read(ref _canceled);
        public bool Block { get; set; }
        public bool Fail { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "www.libredmm.com" || !uri.AbsolutePath.EndsWith(".json", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected stability network request: {uri}.");
            Interlocked.Increment(ref _calls);
            Interlocked.Increment(ref _inFlight);
            try
            {
                await Task.Delay(Block ? Timeout.Infinite : 2, token);
                var id = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                var failed = Fail && int.Parse(id.Split('-')[1]) % 10 == 0;
                return new HttpResponseMessage(failed ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { normalized_id = id, title = $"Synthetic {id}" }))
                };
            }
            catch (OperationCanceledException) { Interlocked.Increment(ref _canceled); throw; }
            finally { Interlocked.Decrement(ref _inFlight); }
        }
    }
}
