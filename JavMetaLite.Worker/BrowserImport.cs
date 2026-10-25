using System.Text.Json;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private object BrowserTargets(JsonElement p)
    {
        lock (_gate)
        {
            var entry = GetJob(p);
            var targets = BrowserImportRouting.BuildTargets(entry.Job.Metadata.Id, entry.Job.SourceResults);
            return new { targets = targets.Select(t => new { source = t.SourceName, displayName = t.DisplayName, url = t.InitialUrl,
                requiresResolution = BrowserImportRouting.RequiresProviderResolution(t) }).ToArray(), maximumHtmlBytes = BrowserPageImporter.MaximumHtmlBytes };
        }
    }

    // Resolve a browser address only. Never apply provider metadata to the job,
    // invalidate a save plan, or write sidecars as a side effect of navigation.
    private object ResolveBrowserTarget(JsonElement p)
    {
        var source = Text(p, "source");
        if (!BrowserImportRouting.IsSupportedSource(source))
            throw new RpcError(-32602, "INVALID_PARAMS", "Unsupported browser source.");
        lock (_gate)
        {
            var entry = GetJob(p); EnsureIdle(entry); RequireRevision(entry, p);
            var revision = entry.Job.ReviewRevision;
            var target = BrowserImportRouting.BuildTarget(source, entry.Job.Metadata.Id, entry.Job.SourceResults);
            return StartOperation(entry.Id, async op =>
            {
                var resolved = target;
                if (BrowserImportRouting.RequiresProviderResolution(target))
                {
                    await _searchSlots.WaitAsync(op.Cancellation.Token);
                    try
                    {
                        Progress(op, "resolvingBrowserTarget");
                        using var provider = _providerFactory(target.SourceName);
                        var attempt = await MetadataSearchCoordinator.SearchAttemptAsync(target.RequestedMovieId, provider, op.Cancellation.Token, _providerTimeout);
                        if (!attempt.Success) throw attempt.Error!;
                        var metadata = attempt.Metadata!;
                        if (!string.Equals(MovieIdParser.Normalize(metadata.Id), target.RequestedMovieId, StringComparison.OrdinalIgnoreCase))
                            throw new RpcError(-32010, "PAGE_ID_MISMATCH", "The provider returned a different movie.");
                        resolved = BrowserImportRouting.BuildTarget(source, target.RequestedMovieId, [metadata]);
                        if (BrowserImportRouting.RequiresProviderResolution(resolved))
                            throw new RpcError(-32010, "PAGE_NOT_READY", "The provider returned no usable detail address.");
                    }
                    finally { _searchSlots.Release(); }
                }
                lock (_gate)
                {
                    op.Cancellation.Token.ThrowIfCancellationRequested();
                    if (!_jobs.ContainsKey(entry.Id) || entry.Job.ReviewRevision != revision)
                        throw new RpcError(-32010, "REVISION_CONFLICT", "The review changed while resolving the browser address.");
                    return new { source = resolved.SourceName, url = resolved.InitialUrl, jobId = entry.Id, reviewRevision = revision };
                }
            });
        }
    }

    private static object BrowserSupport(JsonElement p)
    {
        var source = Text(p, "source"); var url = Text(p, "url");
        try { BrowserPageImporter.ValidateUrl(source, url); return new { supported = true, reason = (string?)null }; }
        catch (BrowserPageImportException error) { return new { supported = false, reason = error.Reason }; }
    }

    private object ImportBrowserPage(JsonElement p)
    {
        var source = Text(p, "source"); var url = Text(p, "url"); var html = Text(p, "html");
        BrowserPageImporter.ValidatePage(source, url, html);
        lock (_gate)
        {
            var entry = GetJob(p); EnsureIdle(entry); RequireRevision(entry, p);
            var revision = entry.Job.ReviewRevision;
            var movieId = entry.Job.Metadata.Id;
            entry.Busy = true;
            return StartOperation(entry.Id, async op =>
            {
                var acquired = false;
                try
                {
                    Progress(op, "validatingBrowserPage");
                    await _searchSlots.WaitAsync(op.Cancellation.Token); acquired = true;
                    using var libre = new LibreDmmClient(); using var r18 = new R18DevClient(); using var library = new JavLibraryClient();
                    var result = await new BrowserPageImporter(libre, r18, library).ImportAsync(source, url, html, movieId, op.Cancellation.Token);
                    lock (_gate)
                    {
                        op.Cancellation.Token.ThrowIfCancellationRequested();
                        if (!_jobs.ContainsKey(entry.Id) || entry.Job.ReviewRevision != revision)
                            throw new RpcError(-32010, "REVISION_CONFLICT", "The review changed while the page was being imported.", Snapshot(entry));
                        EnsureSnapshotCapacity(additionalBytes: 4L * JsonSerializer.SerializeToUtf8Bytes(result, JsonDefaults.Options).Length);
                        var count = entry.Job.ApplyManualWebSource(result);
                        InvalidatePlans(entry.Id); entry.Busy = false;
                        return new { job = Snapshot(entry), promotedFieldCount = count };
                    }
                }
                finally { if (acquired) _searchSlots.Release(); lock (_gate) entry.Busy = false; }
            });
        }
    }
}
