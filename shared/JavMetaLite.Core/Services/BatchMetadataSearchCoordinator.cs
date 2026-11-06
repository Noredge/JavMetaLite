using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public enum BatchMetadataSearchItemStatus
{
    Succeeded,
    PartiallySucceeded,
    Failed,
    Canceled,
    NotStarted
}

public sealed record BatchMetadataSearchItemResult(
    MovieJob Job,
    BatchMetadataSearchItemStatus Status,
    IReadOnlyList<MetadataSourceSearchAttempt> Attempts,
    Exception? Error);

public sealed record BatchMetadataSearchResult(
    IReadOnlyList<BatchMetadataSearchItemResult> Items)
{
    public int SucceededCount => Items.Count(item => item.Status is BatchMetadataSearchItemStatus.Succeeded);

    public int PartialCount => Items.Count(item => item.Status is BatchMetadataSearchItemStatus.PartiallySucceeded);

    public int FailedCount => Items.Count(item => item.Status is BatchMetadataSearchItemStatus.Failed);

    public int CanceledCount => Items.Count(item => item.Status is BatchMetadataSearchItemStatus.Canceled);

    public int NotStartedCount => Items.Count(item => item.Status is BatchMetadataSearchItemStatus.NotStarted);
}

public static class BatchMetadataSearchCoordinator
{
    public const int DefaultMaxConcurrentJobs = 2;

    public static async Task<BatchMetadataSearchResult> SearchAsync(
        IEnumerable<MovieJob> jobs,
        IMetadataProvider primaryProvider,
        IMetadataProvider secondaryProvider,
        CancellationToken cancellationToken = default,
        int maxConcurrentJobs = DefaultMaxConcurrentJobs,
        TimeSpan? providerTimeout = null,
        MetadataSourcePreferenceProfile? sourceProfile = null,
        Func<IReadOnlyList<MovieMetadata>, CancellationToken, Task<string?>>? bestArtworkSourceResolver = null,
        bool retryFailedSourcesOnly = false,
        string sourceMode = MetadataSearchSourceModes.LibreDmm)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(primaryProvider);
        ArgumentNullException.ThrowIfNull(secondaryProvider);
        if (maxConcurrentJobs is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentJobs),
                "批量搜索并发数必须介于 1 与 8 之间。 ");
        }

        sourceMode = MetadataSearchSourceModes.Normalize(sourceMode);
        var sourceNames = MetadataSearchSourceModes.AutomaticSources(sourceMode);
        if (sourceNames.Count == 0)
        {
            return new BatchMetadataSearchResult([]);
        }
        var selectedProviders = new[] { primaryProvider, secondaryProvider }
            .Where(provider => sourceNames.Contains(provider.Name, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedProviders.Length != sourceNames.Count)
        {
            throw new ArgumentException("缺少所选自动来源的提供器。", nameof(primaryProvider));
        }
        sourceProfile = sourceMode == MetadataSearchSourceModes.Custom
            ? MetadataSourcePreferenceProfile.Normalize(sourceProfile)
            : null;

        var candidates = jobs
            .Where(job => job is not null && (retryFailedSourcesOnly
                ? job.IsSelectedForBatch && job.LastSearchAttempts.Any(attempt => !attempt.Success &&
                    sourceNames.Contains(attempt.SourceName, StringComparer.OrdinalIgnoreCase)) &&
                  job.SearchState != MovieSearchState.Searching &&
                  !string.IsNullOrWhiteSpace(MovieIdParser.Normalize(job.Metadata.Id))
                : job.CanBatchSearch))
            .Distinct()
            .ToArray();
        if (candidates.Length == 0)
        {
            return new BatchMetadataSearchResult([]);
        }

        var results = new BatchMetadataSearchItemResult?[candidates.Length];
        var nextIndex = -1;
        var workerCount = Math.Min(maxConcurrentJobs, candidates.Length);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => RunWorkerAsync())
            .ToArray();
        await Task.WhenAll(workers);

        for (var index = 0; index < candidates.Length; index++)
        {
            results[index] ??= new BatchMetadataSearchItemResult(
                candidates[index],
                BatchMetadataSearchItemStatus.NotStarted,
                [],
                null);
        }

        return new BatchMetadataSearchResult(results.Select(item => item!).ToArray());

        async Task RunWorkerAsync()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= candidates.Length)
                {
                    return;
                }

                var job = candidates[index];
                try
                {
                    var previousAttempts = job.LastSearchAttempts.ToArray();
                    job.BeginSearch(preserveAttempts: retryFailedSourcesOnly);
                    MultiSourceSearchResult searchResult;
                    if (retryFailedSourcesOnly)
                    {
                        searchResult = await MetadataSearchCoordinator.RetryFailedAsync(job.Metadata.Id, previousAttempts,
                            selectedProviders, cancellationToken, providerTimeout);
                    }
                    else if (selectedProviders.Length == 1)
                    {
                        var attempt = await MetadataSearchCoordinator.SearchAttemptAsync(
                            job.Metadata.Id, selectedProviders[0], cancellationToken, providerTimeout);
                        if (!attempt.Success)
                        {
                            job.MarkSearchFailed([attempt], attempt.Error!);
                            results[index] = new BatchMetadataSearchItemResult(job,
                                BatchMetadataSearchItemStatus.Failed, [attempt], attempt.Error);
                            continue;
                        }
                        searchResult = new MultiSourceSearchResult(attempt.Metadata!, [attempt.Metadata!], [attempt]);
                    }
                    else
                    {
                        searchResult = await MetadataSearchCoordinator.SearchAllAsync(
                            job.Metadata.Id,
                            selectedProviders[0],
                            selectedProviders[1],
                            cancellationToken,
                            providerTimeout);
                    }
                    var resolvedArtworkBundleSource =
                        sourceProfile?.ArtworkSource == MetadataSourcePreferenceProfile.BestArtworkResolution &&
                        bestArtworkSourceResolver is not null
                            ? await bestArtworkSourceResolver(searchResult.Sources, cancellationToken)
                            : null;
                    if (retryFailedSourcesOnly)
                    {
                        job.ApplyRetriedOnlineSources(searchResult.Metadata, searchResult.Sources,
                            searchResult.Attempts, sourceProfile, resolvedArtworkBundleSource);
                    }
                    else
                    {
                        job.ApplyOnlineSources(
                            searchResult.Metadata,
                            searchResult.Sources,
                            searchResult.Attempts,
                            sourceProfile,
                            resolvedArtworkBundleSource);
                    }
                    results[index] = new BatchMetadataSearchItemResult(
                        job,
                        job.HasPartialSourceFailure ? BatchMetadataSearchItemStatus.PartiallySucceeded :
                            BatchMetadataSearchItemStatus.Succeeded,
                        searchResult.Attempts,
                        null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    job.MarkSearchCanceled();
                    results[index] = new BatchMetadataSearchItemResult(
                        job,
                        BatchMetadataSearchItemStatus.Canceled,
                        [],
                        null);
                    return;
                }
                catch (Exception exception)
                {
                    var attempts = exception switch
                    {
                        MultiSourceSearchException multiSourceException => multiSourceException.Attempts,
                        MultiSourceMergeException mergeException => mergeException.Attempts,
                        _ => []
                    };
                    job.MarkSearchFailed(attempts, exception);
                    results[index] = new BatchMetadataSearchItemResult(
                        job,
                        BatchMetadataSearchItemStatus.Failed,
                        attempts,
                        exception);
                }
            }
        }
    }
}
