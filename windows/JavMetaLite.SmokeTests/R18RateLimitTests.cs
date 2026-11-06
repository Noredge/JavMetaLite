using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

internal static class R18RateLimitTests
{
    public static async Task LargeQueueScheduling()
    {
        var clock = new TestClock();
        var scheduler = clock.Scheduler();
        var starts = new List<DateTimeOffset>();
        var urls = new List<string>();
        var active = 0;
        var peak = 0;
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            peak = Math.Max(peak, ++active);
            starts.Add(clock.Now);
            urls.Add(request.RequestUri!.AbsoluteUri);
            await Task.Yield();
            active--;
            return starts.Count == 5 ? Limited(TimeSpan.FromSeconds(7)) : Success(request);
        }));
        using var r18 = new R18DevClient(http, scheduler);
        using var primary = new Provider();
        var jobs = Jobs(71);
        try
        {
            var result = await BatchMetadataSearchCoordinator.SearchAsync(jobs, primary, r18,
                sourceProfile: Profile(), sourceMode: MetadataSearchSourceModes.Custom);
            Check(result.SucceededCount == 71 && result.PartialCount == 0, "71 movies recover after a transient 429");
            Check(starts.Count == 72 && primary.Calls == 71, "only the throttled request is retried");
            Check(peak == 1, "R18 HTTP requests are single flight");
            Check(starts.Zip(starts.Skip(1)).All(pair => pair.Second - pair.First >= TimeSpan.FromSeconds(1)), "every request is spaced");
            var retried = urls.FindIndex(5, url => url == urls[4]);
            Check(retried >= 5 && starts[retried] - starts[4] >= TimeSpan.FromSeconds(7), "Retry-After is honored for the same URL");
            Check(jobs.All(job => job.Metadata.Title.StartsWith("R18 ") && !job.HasFailedSources), "preferred recovered source used");
        }
        finally { foreach (var job in jobs) job.Dispose(); }
    }

    public static async Task RateLimitClassificationAndBudget()
    {
        var clock = new TestClock();
        var urls = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Limited()); // HTML, deliberately no Retry-After.
        }));
        using var r18 = new R18DevClient(http, clock.Scheduler());
        var attempt = await MetadataSearchCoordinator.SearchAttemptAsync("IPX-081", r18);
        Check(attempt.Error is MetadataSourceRateLimitException, "HTML 429 is not NotFound or a generic parse error");
        Check(urls.Count == 3 && urls.Distinct().Count() == 1, "two retries then stop, no fallback URL burst");
        Check(clock.Delays.SequenceEqual(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }), "exponential backoff without a header");

        var dateClock = new TestClock();
        var calls = 0;
        var shared = dateClock.Scheduler();
        using var dateHttp = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = Limited();
            // Server/client clocks differ. Use the response Date to interpret Retry-After.
            response.Headers.Date = dateClock.Now.AddHours(-2);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(dateClock.Now.AddHours(-2).AddMinutes(20));
            return Task.FromResult(response);
        }));
        using var first = new R18DevClient(dateHttp, shared);
        using var second = new R18DevClient(dateHttp, shared);
        var failed = await MetadataSearchCoordinator.SearchAttemptAsync("IPX-081", first);
        var deferred = await MetadataSearchCoordinator.SearchAttemptAsync("IPX-082", second);
        Check(failed.Error is MetadataSourceRateLimitException { RetryAt: var until } && until == dateClock.Now.AddMinutes(20), "HTTP-date supports server clock skew");
        Check(deferred.Error is MetadataSourceRateLimitException && calls == 1, "long cooldown is shared across clients without sending another request");
        Check(dateClock.Delays.Count == 0, "long waits are deferred rather than blocking the queue");
        try
        {
            await second.ImportDetailPageAsync(R18DevClient.BuildDetailPageUrl("IPX-083"));
            throw new Exception("Expected browser import to share the cooldown");
        }
        catch (MetadataSourceRateLimitException) { }
        Check(calls == 1, "manual API import shares the same limiter");
    }

    public static async Task PartialQueueRetryAndReview()
    {
        var clock = new TestClock();
        var calls = 0;
        var recovered = false;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            return Task.FromResult(recovered ? Success(request) : Limited(TimeSpan.FromMinutes(20)));
        }));
        using var r18 = new R18DevClient(http, clock.Scheduler());
        using var primary = new Provider();
        var jobs = Jobs(71);
        try
        {
            jobs[0].SetLocalExtrafanart([@"C:\Synthetic\local.jpg"]);
            var result = await BatchMetadataSearchCoordinator.SearchAsync(jobs, primary, r18,
                sourceProfile: Profile(), sourceMode: MetadataSearchSourceModes.Custom);
            Check(result.SucceededCount == 0 && result.PartialCount == 71 && result.FailedCount == 0, "partial is not complete or all-failed");
            Check(calls == 1 && jobs.All(job => job.HasPartialSourceFailure && job.FailedSourceNames == "R18.dev"), "all pending R18 attempts retain a source warning");
            var retainedPrimary = jobs[0].LastSearchAttempts[0];
            jobs[1].MetadataReview.SetManualValue(MetadataField.Title, "Reviewed title");
            jobs[1].MetadataReview.SetManualValue(MetadataField.Director, "");
            jobs[1].MetadataReview.SelectCandidate(MetadataField.Maker, "libredmm");
            jobs[1].SelectArtworkSource("libredmm");
            jobs[1].Metadata.ScreenshotUrls = [];
            foreach (var job in jobs.Skip(3)) job.IsSelectedForBatch = false;
            recovered = true;
            clock.Now += TimeSpan.FromMinutes(20);
            var retry = await BatchMetadataSearchCoordinator.SearchAsync(jobs, primary, r18,
                sourceProfile: Profile(), retryFailedSourcesOnly: true, sourceMode: MetadataSearchSourceModes.Custom);
            Check(retry.Items.Count == 3 && retry.SucceededCount == 3 && retry.PartialCount == 0, "retry only selected affected movies");
            Check(primary.Calls == 71 && calls == 4 && ReferenceEquals(retainedPrimary, jobs[0].LastSearchAttempts[0]), "successful provider attempts are reused exactly");
            Check(jobs[0].Metadata.Title == "R18 IPX-001" && jobs[0].Metadata.Maker == "R18 maker", "automatic fallback adopts recovered preferred fields");
            Check(jobs[0].ArtworkReview.SelectedCandidate?.Source.Name == "r18dev" &&
                jobs[0].Metadata.ScreenshotUrls.SequenceEqual(new[] { @"C:\Synthetic\local.jpg" }),
                "loaded sidecars do not pin automatic artwork; no mixed online samples after recovery");
            Check(jobs[1].Metadata.Title == "Reviewed title" && jobs[1].Metadata.Director == "", "manual values and intentional blank survive");
            Check(jobs[1].Metadata.Maker == "Libre maker" && jobs[1].MetadataReview.GetSelectedCandidate(MetadataField.Maker)?.Source.Name == "libredmm", "explicit source choice survives");
            Check(jobs[1].ArtworkReview.SelectedCandidate?.Source.Name == "libredmm" && jobs[1].Metadata.ScreenshotUrls.Count == 0, "reviewed artwork and deselected samples survive");
            Check(jobs.Skip(3).All(job => job.HasPartialSourceFailure), "unselected jobs remain untouched");
            var again = await BatchMetadataSearchCoordinator.SearchAsync(jobs, primary, r18, retryFailedSourcesOnly: true, sourceMode: MetadataSearchSourceModes.Custom);
            Check(again.Items.Count == 0 && calls == 4, "recovered jobs are excluded from failed-source retry");
        }
        finally { foreach (var job in jobs) job.Dispose(); }
    }

    public static async Task WaitingTimeoutAndCancellation()
    {
        var now = DateTimeOffset.UtcNow;
        var waits = 0;
        var scheduler = new R18RequestScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), () => now,
            async (delay, ct) => { waits++; await Task.Delay(100, ct); now += delay; });
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Success(request))));
        using var r18 = new R18DevClient(http, scheduler);
        await MetadataSearchCoordinator.SearchSingleAsync("IPX-081", r18, providerTimeout: TimeSpan.FromMilliseconds(30));
        var result = await MetadataSearchCoordinator.SearchSingleAsync("IPX-082", r18, providerTimeout: TimeSpan.FromMilliseconds(30));
        Check(result.Success && waits == 1, "100ms scheduler wait is excluded from the 30ms HTTP timeout");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blockedHttp = new HttpClient(new Handler(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new Exception("unreachable");
        }));
        using var blocked = new R18DevClient(blockedHttp, new TestClock().Scheduler());
        using var firstCancel = new CancellationTokenSource();
        using var queuedCancel = new CancellationTokenSource();
        var first = MetadataSearchCoordinator.SearchAttemptAsync("IPX-081", blocked, firstCancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = MetadataSearchCoordinator.SearchAttemptAsync("IPX-082", blocked, queuedCancel.Token);
        queuedCancel.Cancel();
        await ExpectCanceled(queued);
        firstCancel.Cancel();
        await ExpectCanceled(first);
        var timedOut = await MetadataSearchCoordinator.SearchAttemptAsync("IPX-083", blocked,
            providerTimeout: TimeSpan.FromMilliseconds(30));
        Check(timedOut.Error is MetadataSourceTimeoutException, "actual HTTP still has a bounded timeout and gate is released");

        var cooling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var limitedHttp = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Limited()); }));
        using var limited = new R18DevClient(limitedHttp, new R18RequestScheduler());
        limited.CoolingDown += _ => cooling.TrySetResult();
        using var cancel = new CancellationTokenSource();
        var waiting = MetadataSearchCoordinator.SearchAttemptAsync("IPX-081", limited, cancel.Token);
        await cooling.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancel.Cancel();
        await ExpectCanceled(waiting);
        Check(calls == 1, "cancel during cooldown prevents retry requests");
    }

    public static async Task PartialRetryCancellation()
    {
        using var job = Jobs(1)[0];
        var source = new MovieMetadata { Id = job.Metadata.Id, Title = "Keep", SourceName = "libredmm", SourceDisplayName = "LibreDMM" };
        var attempts = new[]
        {
            new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, source, null, 1),
            new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null, new IOException("offline"), 0)
        };
        job.ApplyOnlineSources(source, [source], attempts);
        using var primary = new Provider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new Exception("unreachable");
        }));
        using var secondary = new R18DevClient(http, new TestClock().Scheduler());
        using var cancel = new CancellationTokenSource();
        var task = BatchMetadataSearchCoordinator.SearchAsync([job], primary, secondary, cancel.Token, retryFailedSourcesOnly: true, sourceMode: MetadataSearchSourceModes.Custom);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancel.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(result.CanceledCount == 1 && primary.Calls == 0, "canceling retry never re-fetches successful provider");
        Check(job.HasFailedSources && job.LastSearchAttempts.Count == 2 && job.Metadata.Title == "Keep", "cancel retains metadata and retry eligibility");
    }

    private static MetadataSourcePreferenceProfile Profile() => new()
    { TitleSource = "r18dev", MakerSource = "r18dev", DirectorSource = "r18dev", ArtworkSource = "r18dev" };

    private static MovieJob[] Jobs(int count) => Enumerable.Range(1, count).Select(index =>
    {
        var job = new MovieJob();
        job.ResetForVideo($@"C:\Synthetic\IPX-{index:D3}.mp4", $"IPX-{index:D3}");
        return job;
    }).ToArray();

    private static HttpResponseMessage Success(HttpRequestMessage request)
    {
        var number = int.Parse(Regex.Match(request.RequestUri!.AbsoluteUri, @"ipx(\d+)").Groups[1].Value);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                dvd_id = $"IPX-{number:D3}", title_en = $"R18 IPX-{number:D3}", maker_name_en = "R18 maker",
                director = "R18 director", jacket_full_url = "https://images.example.test/r18.jpg"
            }), System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage Limited(TimeSpan? wait = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("<html>Too many requests</html>", System.Text.Encoding.UTF8, "text/html") };
        if (wait.HasValue) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait.Value);
        return response;
    }

    private static async Task ExpectCanceled(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Expected prompt cancellation");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class TestClock
    {
        public DateTimeOffset Now = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public R18RequestScheduler Scheduler() => new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), () => Now,
            (delay, ct) => { ct.ThrowIfCancellationRequested(); Delays.Add(delay); Now += delay; return Task.CompletedTask; });
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class Provider : IMetadataProvider
    {
        public string Name => "libredmm";
        public string DisplayName => "LibreDMM";
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<MovieMetadata> SearchAsync(string id, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new MovieMetadata
            {
                Id = id, Title = "Libre " + id, Maker = "Libre maker", SourceName = Name, SourceDisplayName = DisplayName,
                CoverUrl = "https://images.example.test/libre.jpg", ScreenshotUrls = ["https://images.example.test/libre-sample.jpg"]
            });
        }
        public void Dispose() { }
    }
}
