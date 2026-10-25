using System.Text.Json;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private object ExecuteSave(JsonElement p)
    {
        lock (_gate)
        {
            PruneRetainedState();
            if (!_plans.TryGetValue(Text(p, "planId"), out var preview)) throw new RpcError(-32010, "PLAN_STALE", "Unknown preview. Generate a new preview.");
            if (preview.OperationId is not null) return new { operationId = preview.OperationId };
            if (preview.Stale || preview.QueueVersion != _queueVersion || preview.Items.Any(i => i.SourceEntries.Any(e => !_jobs.ContainsKey(e.Id) || e.Job.ReviewRevision != i.SourceRevisions[e.Id])))
                throw new RpcError(-32010, "PLAN_STALE", "The queue or review changed. Generate a new preview.");
            var blockers = preview.Items.SelectMany(i => i.Blockers).ToArray();
            if (blockers.Length > 0) throw new RpcError(-32010, "PATH_CONFLICT", "Resolve all preview conflicts first.", blockers);
            if (_recoveryWarnings.Count > 0) throw new RpcError(-32010, "RECOVERY_REQUIRED", "Inspect unresolved transactions first.", _recoveryWarnings.ToArray());
            if (_importsActive > 0 || _discoveryActive) throw new RpcError(-32010, "JOB_BUSY", "Wait for discovery/import to finish, then regenerate the preview before saving.");
            if (_saveActive) throw new RpcError(-32010, "JOB_BUSY", "Another save is still committing or recovering.");
            foreach (var entry in preview.Entries) EnsureIdle(entry);
            foreach (var entry in preview.Entries) entry.Busy = true;
            _saveActive = true;
            return StartOperation(preview.Items.Length == 1 ? preview.Items[0].Entry.Id : null, async op =>
            {
                try
                {
                    // Recheck the ENTIRE batch before its first filesystem mutation.
                    foreach (var item in preview.Items)
                    {
                        try
                        {
                            Progress(op, "validatingPreview", new { jobId = item.Entry.Id, totalJobs = preview.Items.Length });
                            await ValidateStampsAsync(item, op.Cancellation.Token);
                        }
                        catch (Exception error)
                        {
                            lock (_gate)
                            {
                                if (error is OperationCanceledException) item.Entry.Job.MarkSaveCanceled();
                                else item.Entry.Job.MarkSaveFailed(error, true);
                            }
                            return PreflightFailure(preview, item, error);
                        }
                    }
                    var prepared = preview.Items.Select(i => new PreparedMovieSave(i.Entry.Job, i.Plan, i.Revision, i.Plan.SaveOptions.OverwriteExisting)).ToArray();
                    var result = await BatchSaveCoordinator.ExecuteAsync(prepared,
                        (item, token) => ExecuteTransactionAsync(preview, preview.Items.Single(i => ReferenceEquals(i.Entry.Job, item.Job)), op, token),
                        op.Cancellation.Token,
                        item =>
                        {
                            lock (_gate)
                            {
                                var current = preview.Items.Single(i => ReferenceEquals(i.Entry.Job, item.Item.Job));
                                if (item.Status == BatchSaveItemStatus.Completed)
                                {
                                    if (current.Plan.SaveOptions.WriteNfo) current.Entry.SavedRevision = current.Entry.Job.ReviewRevision;
                                    foreach (var secondary in current.SourceEntries.Skip(1))
                                    {
                                        _jobs.Remove(secondary.Id); secondary.Job.Dispose();
                                    }
                                    // Output paths and sidecar ownership have changed; all other plans expire.
                                    QueueChanged();
                                }
                            }
                            return Task.CompletedTask;
                        });
                    lock (_gate)
                    {
                        var rows = result.Items.Select(item =>
                        {
                            var current = preview.Items.Single(i => ReferenceEquals(i.Entry.Job, item.Item.Job));
                            return new SaveItemReport(current.Entry.Id, current.SourceEntries.Select(e => e.Id).ToArray(), item.Status.ToString(),
                                item.SaveResult is null ? [] : ResultOutputs(item.SaveResult, current.Plan), item.Error is null ? null : RpcError.From(item.Error).Wire);
                        }).ToArray();
                        var cleanup = result.Items.SelectMany(i => i.SaveResult?.CleanupIssues ?? [])
                            .Concat(result.Items.SelectMany(i => i.Error is TemporaryCleanupException failure ? failure.Issues : [])).ToArray();
                        var outcome = result.FailedCount > 0 ? (_recoveryWarnings.Count > 0 ? "recoveryRequired" : "partialFailure")
                            : cleanup.Length > 0 ? "cleanupRequired" : result.CanceledCount > 0 || result.NotStartedCount > 0 ? "canceled" : "completed";
                        return Report(rows, outcome, cleanup);
                    }
                }
                finally { lock (_gate) { foreach (var entry in preview.Entries) entry.Busy = false; _saveActive = false; } }
            }, op => preview.OperationId = op.Id);
        }
    }

    private async Task<OrganizedSaveResult> ExecuteTransactionAsync(Preview preview, PreviewItem item, Operation op, CancellationToken token)
    {
        var entry = item.Entry;
        var journal = new Journal(op.Id + "-" + entry.Id, preview.Id, item.Plan.VideoTransfers.Select(t => t.SourcePath).ToArray(), item.Plan.Changes.ToArray());
        var journalPath = Path.Combine(_recoveryRoot, journal.TransactionId + ".json");
        OrganizedSaveResult? committedResult = null;
        try
        {
            WriteJournal(journalPath, journal);
            await ValidateStampsAsync(item, token);
            using var output = new OutputService(_images);
            var service = _organizationFactory(output);
            var progress = new SynchronousProgress<FileTransactionProgress>(stage =>
            {
                journal.Phase = stage.Stage.ToString();
                if (stage.TemporaryPath is { } temp && !journal.TemporaryPaths.Contains(temp)) journal.TemporaryPaths.Add(temp);
                WriteJournal(journalPath, journal);
                Progress(op, stage.Stage.ToString(), new { jobId = entry.Id, currentIndex = Array.IndexOf(preview.Items, item) + 1,
                    totalJobs = preview.Items.Length, completedBytes = stage.BytesProcessed, totalBytes = stage.TotalBytes });
            });
            committedResult = await service.ExecuteAsync(item.Plan, entry.Job.Metadata, item.Plan.SaveOptions.OverwriteExisting, token, progress);
            journal.Status = committedResult.CleanupIssues.Count == 0 ? "completed" : "cleanupRequired";
            journal.CleanupIssues.AddRange(committedResult.CleanupIssues);
            journal.TemporaryPaths.AddRange(committedResult.CleanupIssues.Select(x => x.Path));
            WriteJournal(journalPath, journal);
            await ReloadAfterSaveAsync(entry, committedResult, item.Plan);
            lock (_gate)
            {
                if (committedResult.CleanupIssues.Count > 0)
                { entry.Warnings.Add("Files were saved; temporary files require inspection. Later batch items were not started."); _recoveryWarnings.Add(journalPath); }
            }
            return committedResult;
        }
        catch (Exception error)
        {
            journal.Status = committedResult is not null ? "cleanupRequired" : error is FileRecoveryException or TemporaryCleanupException ? "recoveryRequired" : error is OperationCanceledException ? "canceledRestored" : "failedRestored";
            journal.ErrorReason = RpcError.From(error).Reason;
            if (error is FileRecoveryException recovery) journal.TemporaryPaths.AddRange(recovery.RecoveryPaths);
            if (error is TemporaryCleanupException cleanup)
            { journal.TemporaryPaths.AddRange(cleanup.Issues.Select(x => x.Path)); journal.CleanupIssues.AddRange(cleanup.Issues); }
            try { WriteJournal(journalPath, journal); } catch { journal.Status = "recoveryRequired"; }
            lock (_gate)
            {
                if (journal.Status is "recoveryRequired" or "cleanupRequired") _recoveryWarnings.Add(journalPath);
                if (committedResult is not null)
                {
                    entry.Job.UpdateVideoPaths(committedResult.VideoPaths);
                    entry.Job.BeginLocalNfoRead(); // Block subsequent writes until an explicit reimport refreshes the baseline.
                    entry.Warnings.Add("Files were saved, but post-save verification or journaling needs inspection. Remove and reimport after resolving the recovery record.");
                }
            }
            if (committedResult is not null)
                return committedResult.WithAdditionalCleanupIssues([new(journalPath, error.Message)]);
            throw;
        }
    }

    private async Task ReloadAfterSaveAsync(Entry entry, OrganizedSaveResult result, SavePlan plan)
    {
        var fresh = new MovieJob();
        try
        {
            var loaded = await MovieJobLoader.LoadAsync(fresh, result.VideoPaths, _images, CancellationToken.None);
            if (loaded.MetadataStatus is LocalMetadataLoadStatus.NfoReadFailed or LocalMetadataLoadStatus.SidecarInspectionFailed || plan.SaveOptions.WriteNfo && loaded.MetadataStatus != LocalMetadataLoadStatus.Loaded)
                throw new IOException("Saved files could not be reloaded safely.", loaded.MetadataError);
            lock (_gate)
            {
                var previousSource = entry.Job.ArtworkReview.SelectedCandidate?.Source.Name;
                var previousSamples = entry.Job.Metadata.ScreenshotUrls.ToArray();
                entry.Job.UpdateVideoPaths(result.VideoPaths);
                if (plan.SaveOptions.WriteNfo && fresh.LocalMetadataBundle is { } bundle)
                    entry.Job.ApplyLocalMetadata(bundle, LocalMetadataReviewComposer.CreateLocal(bundle.Metadata));
                else
                    entry.Job.RefreshLocalMetadataBaseline(fresh.LocalMetadataBundle);
                entry.Job.SetLocalArtwork(fresh.LocalArtworkCandidate);
                entry.Job.SetLocalExtrafanart(fresh.LocalExtrafanartPaths);
                if (!plan.SaveOptions.WriteNfo)
                {
                    if (previousSource is not null) entry.Job.SelectArtworkSource(previousSource);
                    // Retain explicit selections, remapping only local files actually moved by this transaction.
                    var samples = new List<string>();
                    foreach (var location in previousSamples)
                    {
                        if (!ArtworkLocationHelper.TryGetLocalPath(location, out var local)) { samples.Add(location); continue; }
                        var transfer = plan.SidecarTransfers.FirstOrDefault(t => FileSystemPathSemantics.PathsEqual(t.SourcePath, local));
                        if (transfer is not null) samples.Add(transfer.DestinationPath);
                        else if (!plan.SourcePathsToRetire.Any(path => FileSystemPathSemantics.PathsEqual(path, local))) samples.Add(location);
                    }
                    // Regenerated local samples can change names; retired source URLs must not survive.
                    samples.AddRange(result.Outputs.ExtrafanartPaths);
                    entry.Job.Metadata.ScreenshotUrls = samples.Distinct(StringComparer.Ordinal).ToArray();
                }
                // Rebuilding the reviewed artwork invalidates the old quality revision.
                // Reuse only the freshly measured, currently selected local full cover;
                // never attach local dimensions to a retained online source or poster.
                if (fresh.CoverResolution is { IsMeasured: true } resolution &&
                    entry.Job.ArtworkReview.SelectedCandidate is { IsSidecarPair: true } selected &&
                    !string.IsNullOrWhiteSpace(selected.LocalFanartPath) &&
                    FileSystemPathSemantics.PathsEqual(selected.LocalFanartPath, resolution.Location))
                    entry.Job.TrySetCoverResolution(entry.Job.CoverResolutionRevision, resolution);
                entry.Warnings.AddRange(loaded.ArtworkDiscovery?.Diagnostics ?? []);
            }
        }
        finally { fresh.Dispose(); }
    }

    private static string[] ResultOutputs(OrganizedSaveResult result, SavePlan plan) => result.VideoPaths
        .Concat(new[] { result.Outputs.NfoPath, result.Outputs.PosterPath, result.Outputs.FanartPath }.OfType<string>())
        .Concat(result.Outputs.ExtrafanartPaths).Concat(plan.SidecarTransfers.Select(t => t.DestinationPath))
        .Distinct(FileSystemPathSemantics.PathComparer).ToArray();
    private static BatchReport PreflightFailure(Preview preview, PreviewItem failed, Exception error)
    {
        var rows = preview.Items.Select(i => new SaveItemReport(i.Entry.Id, i.SourceEntries.Select(e => e.Id).ToArray(),
            ReferenceEquals(i, failed) ? error is OperationCanceledException ? "Canceled" : "Failed" : "NotStarted", [], ReferenceEquals(i, failed) ? RpcError.From(error).Wire : null)).ToArray();
        return Report(rows, error is OperationCanceledException ? "canceled" : "partialFailure", []);
    }
    private static BatchReport Report(SaveItemReport[] rows, string outcome, TemporaryCleanupIssue[] cleanup) => new(outcome,
        rows.Where(r => r.Status == "Completed").SelectMany(r => r.SourceJobIds).ToArray(), rows.Where(r => r.Status == "NotStarted").SelectMany(r => r.SourceJobIds).ToArray(),
        rows.FirstOrDefault(r => r.Status == "Failed")?.JobId, cleanup.Select(x => x.Path).ToArray(), cleanup.Select(x => new CleanupIssueReport(x.Path, x.Error)).ToArray(),
        rows.SelectMany(r => r.Outputs).Distinct(FileSystemPathSemantics.PathComparer).ToArray(), rows);
    private sealed record SaveItemReport(string JobId, string[] SourceJobIds, string Status, string[] Outputs, object? Error);
    private sealed record BatchReport(string Outcome, string[] SavedJobIds, string[] NotStartedJobIds, string? FailedJobId, string[] CleanupWarnings,
        CleanupIssueReport[] CleanupIssues, string[] Outputs, SaveItemReport[] JobResults);
    private sealed record CleanupIssueReport(string Path, string Message);
    private static void WriteJournal(string path, Journal journal)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, journal, JsonDefaults.Options); stream.Flush(true); }
        File.Move(temp, path, true);
    }
    private sealed class Journal(string transactionId, string planId, string[] sourcePaths, PlannedFileChange[] changes)
    { public int ProtocolVersion { get; } = 1; public string TransactionId { get; } = transactionId; public string PlanId { get; } = planId; public string[] SourcePaths { get; } = sourcePaths; public PlannedFileChange[] Changes { get; } = changes; public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow; public string Status { get; set; } = "inProgress"; public string Phase { get; set; } = "preparing"; public List<string> TemporaryPaths { get; } = []; public List<TemporaryCleanupIssue> CleanupIssues { get; } = []; public string? ErrorReason { get; set; } }
    private sealed class SynchronousProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
