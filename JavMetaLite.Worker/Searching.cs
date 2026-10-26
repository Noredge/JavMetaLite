using System.Text.Json;
using JavMetaLite.Core.Services;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private object Search(JsonElement p, bool retry)
    {
        var mode = p.TryGetProperty("sourceMode", out _) ? Text(p, "sourceMode") : "libredmm";
        if (mode is not ("libredmm" or "r18dev" or "all" or "custom")) throw new RpcError(-32602, "INVALID_PARAMS", "Choose libredmm, r18dev, all or custom.");
        var profile = mode == "custom" ? ReadSourceProfile(p) : mode == "all"
            ? new MetadataSourcePreferenceProfile { TitleSource = MetadataSourcePreferenceProfile.R18Dev }
            : OnlineProfile(mode);
        Entry[] entries;
        lock (_gate)
        {
            entries = RequestedEntries(p, requireRevisions: false);
            foreach (var entry in entries)
                if (string.IsNullOrWhiteSpace(MovieIdParser.Normalize(entry.Job.Metadata.Id))) throw new RpcError(-32602, "INVALID_PARAMS", "Every selected movie needs an ID before searching.");
            if (retry && entries.Any(entry => !entry.Job.HasFailedSources))
                throw new RpcError(-32602, "INVALID_PARAMS", "Retry only jobs with recorded failed sources; use search to request a new source.");
            foreach (var entry in entries) { entry.Busy = true; InvalidatePlans(entry.Id); }
        }
        return StartOperation(entries.Length == 1 ? entries[0].Id : null, async op =>
        {
            var rows = new List<object>();
            var warnings = new List<string>();
            try
            {
                foreach (var entry in entries)
                {
                    if (op.Cancellation.IsCancellationRequested) { rows.Add(new { jobId = entry.Id, status = "NotStarted" }); continue; }
                    var acquired = false;
                    try
                    {
                        lock (_gate) entry.Job.BeginSearch(retry);
                        Progress(op, "waitingForProviderSlot", new { jobId = entry.Id });
                        await _searchSlots.WaitAsync(op.Cancellation.Token); acquired = true;
                        Progress(op, mode == "libredmm" ? "queryingLibreDmm" : mode == "r18dev" ? "queryingR18Dev" : "queryingSources", new { jobId = entry.Id });
                        using var first = _providerFactory(mode == "r18dev" ? "r18dev" : "libredmm");
                        using var second = mode is "all" or "custom" ? _providerFactory("r18dev") : null;
                        IMetadataProvider[] providers = second is null ? [first] : [first, second];
                        MultiSourceSearchResult result;
                        var previous = entry.Job.LastSearchAttempts;
                        if (retry && previous.Count > 0)
                        {
                            var failed = providers.Where(provider => previous.Any(a => !a.Success && a.SourceName == provider.Name)).ToArray();
                            result = await MetadataSearchCoordinator.RetryFailedAsync(entry.Job.Metadata.Id, previous, failed, op.Cancellation.Token, _providerTimeout);
                        }
                        else if (mode is "all" or "custom") result = await MetadataSearchCoordinator.SearchAllAsync(entry.Job.Metadata.Id, first, second!, op.Cancellation.Token, _providerTimeout);
                        else
                        {
                            var attempt = await MetadataSearchCoordinator.SearchAttemptAsync(entry.Job.Metadata.Id, providers[0], op.Cancellation.Token, _providerTimeout);
                            if (!attempt.Success) { lock (_gate) entry.Job.MarkSearchFailed([attempt], attempt.Error!); throw attempt.Error!; }
                            result = new MultiSourceSearchResult(attempt.Metadata!, [attempt.Metadata!], [attempt]);
                        }
                        lock (_gate)
                        {
                            EnsureSnapshotCapacity(additionalBytes: 4L * JsonSerializer.SerializeToUtf8Bytes(result.Sources, JsonDefaults.Options).Length);
                            // Explicit new queries display their fresh configured
                            // sources. Manual values remain candidates; retries
                            // retain the user's current reviewed selections.
                            if (retry) entry.Job.ApplyRetriedOnlineSources(result.Metadata, result.Sources, result.Attempts, profile);
                            else
                            {
                                // A new query replaces the sources it actually
                                // requested; unrelated earlier sources remain
                                // candidates without displacing fresh selections.
                                var freshSources = result.Sources.Concat(entry.Job.SourceResults.Where(source =>
                                        HasCurrentMovieId(entry, source) && !providers.Any(provider => provider.Name == source.SourceName)))
                                    .DistinctBy(source => MetadataCandidateSource.FromMetadata(source).Name, StringComparer.OrdinalIgnoreCase)
                                    .ToArray();
                                entry.Job.ApplyOnlineSources(result.Metadata, freshSources, result.Attempts, profile);
                            }
                            var partial = result.Attempts.Where(a => !a.Success).Select(a => a.SourceDisplayName + ": " + a.Error?.Message).ToArray();
                            warnings.AddRange(partial);
                            rows.Add(new { jobId = entry.Id, status = "Completed", outcome = partial.Length > 0 ? "partialFailure" : "completed",
                                attempts = result.Attempts.Select(SearchAttemptSnapshot).ToArray(), warnings = partial });
                        }
                    }
                    catch (OperationCanceledException) { lock (_gate) entry.Job.MarkSearchCanceled(); rows.Add(new { jobId = entry.Id, status = "Canceled" }); }
                    catch (Exception error)
                    {
                        lock (_gate)
                        {
                            if (entry.Job.SearchState == MovieSearchState.Searching)
                            {
                                var attempts = error is MultiSourceSearchException search ? search.Attempts : error is MultiSourceMergeException merge ? merge.Attempts : entry.Job.LastSearchAttempts;
                                entry.Job.MarkSearchFailed(attempts, error);
                            }
                        }
                        warnings.Add(entry.Job.Metadata.Id + ": " + error.Message);
                        rows.Add(new { jobId = entry.Id, status = "Failed", attempts = entry.Job.LastSearchAttempts.Select(SearchAttemptSnapshot).ToArray(), error = RpcError.From(error).Wire });
                    }
                    finally { if (acquired) _searchSlots.Release(); }
                }
                return new { sourceMode = mode, outcome = op.Cancellation.IsCancellationRequested ? "canceled" : warnings.Count > 0 ? "partialFailure" : "completed", jobResults = rows, warnings };
            }
            finally { lock (_gate) foreach (var entry in entries) entry.Busy = false; }
        });
    }

    private static readonly string[] SourceProfileKeys = ["titleSource", "originalTitleSource", "releaseDateSource", "runtimeSource", "makerSource", "directorSource", "labelSource", "actorsSource", "genresSource", "plotSource", "ratingSource", "artworkSource"];
    private static MetadataSourcePreferenceProfile ReadSourceProfile(JsonElement p)
    {
        if (!p.TryGetProperty("sourceProfile", out var value) || value.ValueKind != JsonValueKind.Object)
            throw new RpcError(-32602, "INVALID_PARAMS", "Custom search requires a sourceProfile object.");
        foreach (var field in value.EnumerateObject())
            if (!SourceProfileKeys.Contains(field.Name) || field.Value.ValueKind != JsonValueKind.String || !MetadataSourcePreferenceProfile.IsSupportedSource(field.Value.GetString()))
                throw new RpcError(-32602, "INVALID_PARAMS", "Each custom field source must be libredmm or r18dev.");
        return MetadataSourcePreferenceProfile.Normalize(value.Deserialize<MetadataSourcePreferenceProfile>(JsonDefaults.Options));
    }
    private static MetadataSourcePreferenceProfile OnlineProfile(string source) => new()
    {
        TitleSource = source, OriginalTitleSource = source, ReleaseDateSource = source,
        RuntimeSource = source, MakerSource = source, DirectorSource = source,
        LabelSource = source, ActorsSource = source, GenresSource = source,
        PlotSource = source, RatingSource = source, ArtworkSource = source
    };

    private static object SearchAttemptSnapshot(MetadataSourceSearchAttempt attempt) => new
    {
        source = attempt.SourceName, displayName = attempt.SourceDisplayName, success = attempt.Success,
        elapsedMilliseconds = attempt.Elapsed.TotalMilliseconds, error = attempt.Error is null ? null : SearchErrorSnapshot(attempt.Error)
    };

    private static object SearchErrorSnapshot(Exception error)
    {
        // Providers can retain a transport error as InnerException when all fallback URLs fail.
        var cause = error;
        while (cause.InnerException is { } inner && cause is not (MetadataSourceRateLimitException or MetadataSourceTimeoutException)) cause = inner;
        var reason = cause switch
        {
            MetadataSourceRateLimitException => "PROVIDER_RATE_LIMITED",
            MetadataSourceTimeoutException => "PROVIDER_TIMEOUT",
            MetadataNotFoundException => "PROVIDER_NOT_FOUND",
            HttpRequestException => "PROVIDER_HTTP_ERROR",
            OperationCanceledException => "CANCELED",
            _ => "PROVIDER_FAILED"
        };
        return new { reason, message = error.Message, retryAt = cause is MetadataSourceRateLimitException rate ? (DateTimeOffset?)rate.RetryAt : null };
    }
}
