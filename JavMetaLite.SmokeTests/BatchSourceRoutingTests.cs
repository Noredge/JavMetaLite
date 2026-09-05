using System.IO;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

internal static class BatchSourceRoutingTests
{
    public static async Task DefaultSource()
    {
        using var libre = new Provider("libredmm");
        using var r18 = new Provider("r18dev", (_, _) => throw new Exception("Default search must never request R18"));
        var jobs = Enumerable.Range(1, 71).Select(Job).ToArray();
        try
        {
            var result = await BatchMetadataSearchCoordinator.SearchAsync(jobs, libre, r18);
            Check(result.SucceededCount == 71 && libre.Calls == 71 && r18.Calls == 0,
                "Omitted source mode must default to LibreDMM only");
            Check(AppPreferences.CreateSafeDefaults().SearchSourceMode == "libredmm" &&
                MetadataSearchSourceModes.Normalize(" AUTO ") == "libredmm" &&
                MetadataSearchSourceModes.Normalize(null) == "libredmm",
                "Fresh, legacy Auto and missing source settings default to LibreDMM");
        }
        finally { foreach (var job in jobs) job.Dispose(); }
    }

    public static async Task SourceMatrix()
    {
        foreach (var mode in MetadataSearchSourceModes.Supported.Concat(new[] { "javlibrary", "auto", "invalid" }))
        {
            using var libre = new Provider("libredmm");
            using var r18 = new Provider("r18dev");
            var jobs = Enumerable.Range(1, 71).Select(Job).ToArray();
            try
            {
                var sourceNames = MetadataSearchSourceModes.AutomaticSources(mode);
                var expectedSources = mode switch
                {
                    "custom" => new[] { "libredmm", "r18dev" },
                    "r18dev" => new[] { "r18dev" },
                    "manual" or "javlibrary" => Array.Empty<string>(),
                    _ => new[] { "libredmm" }
                };
                Check(sourceNames.SequenceEqual(expectedSources), $"{mode}: independent source policy expectation");
                var result = await BatchMetadataSearchCoordinator.SearchAsync(jobs, libre, r18,
                    sourceMode: mode,
                    sourceProfile: new MetadataSourcePreferenceProfile { TitleSource = "r18dev" });
                Check(libre.Calls == (sourceNames.Contains("libredmm") ? 71 : 0), $"{mode}: Libre request count");
                Check(r18.Calls == (sourceNames.Contains("r18dev") ? 71 : 0), $"{mode}: R18 request count");
                if (sourceNames.Count == 0)
                {
                    Check(result.Items.Count == 0 && jobs.All(job => job.SearchState == MovieSearchState.Searchable), "Manual/legacy JAVLibrary must leave queue untouched");
                    continue;
                }
                Check(result.SucceededCount == 71 && result.PartialCount == 0 && result.FailedCount == 0, $"{mode}: complete success is relative to requested sources");
                Check(jobs.All(job => job.LastSearchAttempts.Count == sourceNames.Count &&
                    job.LastSearchAttempts.All(attempt => sourceNames.Contains(attempt.SourceName))), $"{mode}: no unrequested attempts");
                var expectedTitleSource = mode is "custom" or "r18dev" ? "r18dev" : "libredmm";
                Check(jobs.All(job => job.Metadata.Title.StartsWith(expectedTitleSource)), $"{mode}: custom preferences apply only in Custom mode");
            }
            finally { foreach (var job in jobs) job.Dispose(); }
        }
    }

    public static async Task SingleSourceFailureAndRetry()
    {
        var fail = true;
        using var libre = new Provider("libredmm", (id, _) => fail && id == "IPX-002"
            ? Task.FromException<MovieMetadata>(new IOException("synthetic failure"))
            : Task.FromResult(Metadata(id, "libredmm")));
        using var r18 = new Provider("r18dev", (_, _) => throw new Exception("Unselected R18 must never run"));
        var jobs = Enumerable.Range(1, 3).Select(Job).ToArray();
        try
        {
            var result = await BatchMetadataSearchCoordinator.SearchAsync(jobs, libre, r18, sourceMode: "libredmm");
            Check(result.SucceededCount == 2 && result.FailedCount == 1 && result.PartialCount == 0, "single-source failure is not partial multi-source success");
            Check(jobs[1].LastSearchAttempts is [{ SourceName: "libredmm", Success: false }], "single-source failure retains retryable attempt");
            fail = false;
            var retry = await BatchMetadataSearchCoordinator.SearchAsync(jobs, libre, r18,
                sourceMode: "libredmm", retryFailedSourcesOnly: true);
            Check(retry.SucceededCount == 1 && libre.Calls == 4 && r18.Calls == 0, "retry only failed single-source job");
        }
        finally { foreach (var job in jobs) job.Dispose(); }
    }

    public static async Task ScopedRetryAndManualImport()
    {
        using var job = Job(81);
        var libreMetadata = Metadata(job.Metadata.Id, "libredmm");
        var libreAttempt = new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, libreMetadata, null, 1);
        var failedR18 = new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null, new IOException("offline"), 0);
        var failedJav = new MetadataSourceSearchAttempt("javlibrary", "JAVLibrary", TimeSpan.Zero, null, new IOException("legacy attempt"), 0);
        job.ApplyOnlineSources(libreMetadata, [libreMetadata], [libreAttempt, failedR18, failedJav]);
        job.ApplyManualWebSource(new MovieMetadata { Id = job.Metadata.Id, Rating = "8.5", SourceName = "javlibrary", SourceDisplayName = "JAVLibrary" });
        using var libre = new Provider("libredmm");
        using var r18 = new Provider("r18dev");
        var skipped = await BatchMetadataSearchCoordinator.SearchAsync([job], libre, r18,
            sourceMode: "libredmm", retryFailedSourcesOnly: true);
        Check(skipped.Items.Count == 0 && libre.Calls == 0 && r18.Calls == 0, "DMM-only retry cannot contact old failed R18 or JAVLibrary");
        var recovered = await BatchMetadataSearchCoordinator.SearchAsync([job], libre, r18,
            sourceMode: "r18dev", retryFailedSourcesOnly: true);
        Check(r18.Calls == 1 && libre.Calls == 0 && recovered.PartialCount == 1, "R18 retry leaves excluded legacy failure as history");
        Check(ReferenceEquals(job.LastSearchAttempts[0], libreAttempt) && ReferenceEquals(job.LastSearchAttempts[2], failedJav), "unrequested attempts are not mutated");
        Check(job.Metadata.Rating == "8.5" && job.MetadataReview.GetSelectedCandidate(MetadataField.Rating)?.Source.Name == "javlibrary", "manual JAVLibrary data and provenance survive retry");
        var noAutomaticFailures = await BatchMetadataSearchCoordinator.SearchAsync([job], libre, r18, retryFailedSourcesOnly: true, sourceMode: "custom");
        Check(noAutomaticFailures.Items.Count == 0 && r18.Calls == 1, "legacy JAVLibrary failure is never an automatic retry candidate");
    }

    public static async Task SingleSourceCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var libre = new Provider("libredmm", async (id, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Metadata(id, "libredmm");
        });
        using var r18 = new Provider("r18dev");
        using var cancel = new CancellationTokenSource();
        var jobs = Enumerable.Range(1, 4).Select(Job).ToArray();
        try
        {
            var task = BatchMetadataSearchCoordinator.SearchAsync(jobs, libre, r18, cancel.Token,
                sourceMode: "libredmm", maxConcurrentJobs: 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancel.Cancel();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(result.CanceledCount == 1 && result.NotStartedCount == 3 && libre.Calls == 1 && r18.Calls == 0, "single-source cancellation must stop remaining jobs without fallback");
        }
        finally { foreach (var job in jobs) job.Dispose(); }
    }

    private static MovieJob Job(int index)
    {
        var job = new MovieJob();
        job.ResetForVideo($@"C:\Synthetic\IPX-{index:D3}.mp4", $"IPX-{index:D3}");
        return job;
    }

    private static MovieMetadata Metadata(string id, string source) => new()
    { Id = id, Title = source + " title", SourceName = source, SourceDisplayName = source };

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Provider(string name, Func<string, CancellationToken, Task<MovieMetadata>>? search = null) : IMetadataProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Name => name;
        public string DisplayName => name;
        public Task<MovieMetadata> SearchAsync(string id, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return search?.Invoke(id, cancellationToken) ?? Task.FromResult(Metadata(id, name));
        }
        public void Dispose() { }
    }
}
