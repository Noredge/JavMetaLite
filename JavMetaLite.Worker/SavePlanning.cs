using System.Security.Cryptography;
using System.Text.Json;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private long _queueVersion;

    private Task<object> PreviewSaveAsync(JsonElement p)
    {
        // The native client uses an operation so hashing large moving videos is cancelable.
        if (Bool(p, "async", false))
            return Task.FromResult(StartOperation(null, op => BuildPreviewAsync(p, op.Cancellation.Token, op)));
        return BuildPreviewAsync(p, CancellationToken.None, null);
    }

    // At most one filesystem preparation may outlive cancellation. It owns only
    // copied review inputs, never the live jobs or a publishable plan token.
    private Task? _previewReadTask;
    private async Task<T> ReadPreviewAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Task<T> task;
        lock (_gate)
        {
            if (_previewReadTask is { IsCompleted: false })
                throw new RpcError(-32010, "JOB_BUSY", "A previous read-only filesystem check is still returning. Other review actions remain available; retry the preview after it finishes.");
            task = Task.Run(read);
            _previewReadTask = task;
        }
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception; // Observe a canceled caller's late read failure.
            lock (_gate) if (ReferenceEquals(_previewReadTask, completed)) _previewReadTask = null;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await task.WaitAsync(token);
    }

    private async Task<object> BuildPreviewAsync(JsonElement p, CancellationToken token, Operation? operation)
    {
        bool defaultIncludeId;
        lock (_gate)
        {
            ValidatePreviewAdmission(p);
            defaultIncludeId = _preferences.Preferences.IncludeIdInTitle;
        }
        var options = p.TryGetProperty("options", out var o) ? o : JsonDefaults.Empty;
        var config = await ReadPreviewAsync(() => Task.FromResult(ParseConfiguration(options, defaultIncludeId)), token);
        Entry[] entries;
        PreviewEntry[] captured, selected;
        long queueVersion;
        var confirmedParts = Bool(p, "confirmedParts", false);
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            entries = ValidatePreviewAdmission(p);
            foreach (var entry in entries)
            {
                if (entry.Job.LocalNfoSaveBlocked) throw new RpcError(-32010, "SOURCE_CHANGED", "The original NFO could not be read safely. Correct it and reimport the movie.");
            }
            captured = _jobs.Values.Select(entry => CapturePreviewEntry(entry, entries.Contains(entry))).ToArray();
            // Preserve the user's explicit multipart order, not dictionary order.
            selected = entries.Select(entry => captured.Single(item => ReferenceEquals(item.Entry, entry))).ToArray();
            queueVersion = _queueVersion;
            foreach (var entry in entries) entry.Busy = true;
        }
        try
        {
            if (operation is not null) Progress(operation, "preparingPreview", new { totalJobs = entries.Length });
            var items = await ReadPreviewAsync(async () =>
            {
                await _beforePreviewRead(token);
                token.ThrowIfCancellationRequested();
                return await PreparePreviewItemsAsync(selected, captured, config, confirmedParts, token);
            }, token);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (_saveActive || queueVersion != _queueVersion || items.Any(i => i.SourceEntries.Any(e =>
                    !_jobs.TryGetValue(e.Id, out var current) || !ReferenceEquals(current, e) || e.Job.ReviewRevision != i.SourceRevisions[e.Id])))
                    throw new RpcError(-32010, "PLAN_STALE", "The queue or review changed while preparing this preview.");
                // Validation/cancellation must not mutate the review. Bind the
                // successful plan to the configured revisions only at publication.
                foreach (var entry in entries)
                {
                    entry.Job.UpdateSaveConfiguration(config);
                    InvalidatePlans(entry.Id);
                }
                foreach (var item in items)
                    foreach (var source in item.SourceEntries) item.SourceRevisions[source.Id] = source.Job.ReviewRevision;
                var preview = new Preview(items, queueVersion, confirmedParts);
                var result = PreviewSnapshot(preview);
                preview.RetainedBytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonDefaults.Options).LongLength;
                if (preview.RetainedBytes > 7 * 1024 * 1024)
                    throw new RpcError(-32010, "WORKSPACE_CAPACITY", "The preview is too large. Select a smaller batch.");
                _plans.Add(preview.Id, preview);
                PruneRetainedState();
                return result;
            }
        }
        finally { lock (_gate) foreach (var entry in entries) entry.Busy = false; }
    }

    private Entry[] ValidatePreviewAdmission(JsonElement p)
    {
        if (_saveActive) throw new RpcError(-32010, "JOB_BUSY", "Wait for the active save before preparing another preview.");
        if (_recoveryWarnings.Count > 0) throw new RpcError(-32010, "RECOVERY_REQUIRED", "Inspect unresolved transaction records before saving.", _recoveryWarnings.ToArray());
        return RequestedEntries(p, requireRevisions: true);
    }

    private static PreviewEntry CapturePreviewEntry(Entry entry, bool selected) => new(entry,
        entry.Job.ReviewRevision, entry.Job.VideoPaths.ToArray(), entry.Job.CreateLocalSaveContext() with
        { LocalExtrafanartPaths = entry.Job.LocalExtrafanartPaths.ToArray() }, selected
            ? JsonSerializer.Deserialize<MovieMetadata>(JsonSerializer.SerializeToUtf8Bytes(entry.Job.Metadata, JsonDefaults.Options), JsonDefaults.Options)!
            : null);

    private static async Task<PreviewItem[]> PreparePreviewItemsAsync(PreviewEntry[] selected, PreviewEntry[] all,
        MovieSaveConfiguration config, bool confirmedParts, CancellationToken token)
    {
        if (confirmedParts) ValidateConfirmedParts(selected);
        var inputs = new Dictionary<PreviewEntry, string[]>();
        foreach (var captured in all)
        {
            token.ThrowIfCancellationRequested();
            var sidecars = captured.Metadata is null ? LocateSidecars(captured.VideoPaths)
                : ValidateNfoBaseline(captured.VideoPaths, captured.Context.MetadataBundle?.Sidecars.NfoPath);
            inputs[captured] = captured.VideoPaths.Concat(captured.Context.LocalExtrafanartPaths)
                .Concat(new[] { sidecars.NfoPath, sidecars.PosterPath, sidecars.FanartPath }.OfType<string>())
                .Concat(sidecars.ExtrafanartPaths).Distinct(FileSystemPathSemantics.PathComparer).ToArray();
            if (captured.Metadata is not null)
                ValidateArtworkPermissions(captured.Metadata.ScreenshotUrls.Concat(captured.Context.SelectedArtwork?.Urls ?? []), inputs[captured]);
        }
        var protectedItems = all.Where(captured => !selected.Contains(captured))
            .Select(captured => ProtectedPreviewItem(captured, config, inputs[captured])).ToList();
        token.ThrowIfCancellationRequested();
        var items = confirmedParts
            ? new[] { CreatePreviewItem(selected[0], selected, FileOrganizationService.BuildConfirmedMultipartPlan(
                selected.Select(e => e.VideoPaths.Single()), selected[0].Metadata!, config.SaveOptions, config.OrganizationOptions, selected[0].Context)) }
            : selected.Select(e =>
            {
                token.ThrowIfCancellationRequested();
                return CreatePreviewItem(e, [e], FileOrganizationService.BuildPlan(e.VideoPaths, e.Metadata!, config.SaveOptions, config.OrganizationOptions, e.Context));
            }).ToArray();
        if (confirmedParts)
        {
            var primaryInputs = inputs[selected[0]].ToHashSet(FileSystemPathSemantics.PathComparer);
            foreach (var secondary in selected.Skip(1))
            {
                var preserved = inputs[secondary].Where(path => !secondary.VideoPaths.Contains(path) && !primaryInputs.Contains(path)).ToArray();
                protectedItems.Add(ProtectedPreviewItem(secondary, config, preserved));
                items[0].ExtraKeptPaths.AddRange(preserved);
                if (preserved.Length > 0) items[0].Warnings.Add("Unique secondary sidecars remain at their original paths; only the first movie's reviewed metadata is used.");
            }
        }
        var conflicts = BatchSavePathConflicts.Find(items.Select(i => new BatchSavePlanItem(i.Entry.Job, i.Plan, i.Revision)).ToArray(), protectedItems);
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (conflicts.TryGetValue(item.Entry.Job, out var paths)) item.Blockers.AddRange(paths.Select(path => "Cross-job input/output conflict: " + path));
            item.Plan = item.Plan with { Changes = item.Plan.Changes.Concat(item.Plan.VideoTransfers.Where(t => !t.WillMove)
                .Select(t => new PlannedFileChange(PlannedChangeKind.KeepFile, "Original video remains unchanged", t.SourcePath, t.SourcePath)))
                .Concat(item.ExtraKeptPaths.Select(path => new PlannedFileChange(PlannedChangeKind.KeepFile, "Secondary sidecar remains unchanged", path, path))).Distinct().ToArray() };
            foreach (var expected in item.Plan.SourceFileExpectations)
            {
                var stamp = await CaptureAsync(expected.Path, true, token);
                if (!stamp.Exists || !string.Equals(stamp.Hash, expected.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new RpcError(-32010, "SOURCE_CHANGED", "A loaded source changed. Reimport it before generating a preview.");
            }
            var videoPaths = item.Plan.VideoTransfers.Select(t => t.SourcePath).ToHashSet(FileSystemPathSemantics.PathComparer);
            var writes = WriteTargets(item.Plan).ToArray();
            var capturePaths = writes.Concat(item.Plan.Changes.Select(c => c.SourcePath).OfType<string>().Where(Path.IsPathFullyQualified))
                .Concat(item.Plan.SourceFileExpectations.Select(x => x.Path)).Concat(videoPaths).Concat(item.Plan.SidecarTransfers.Select(t => t.SourcePath))
                .Concat(item.Plan.SourcePathsToRetire).Distinct(FileSystemPathSemantics.PathComparer).ToArray();
            var stamps = new List<FileStamp>();
            foreach (var path in capturePaths)
            {
                token.ThrowIfCancellationRequested();
                stamps.Add(await CaptureAsync(path, !videoPaths.Contains(path) || item.Plan.VideoWillMove, token));
            }
            item.Stamps = stamps.ToArray();
            if (item.Plan.OutputGenerationOptions.WriteNfo && item.Plan.LocalContext?.MetadataBundle is null)
            {
                var outputNames = item.Plan.OutputFileNames!;
                var nfoTarget = outputNames.Resolve(item.Plan.TargetDirectory, outputNames.Nfo);
                if (stamps.Any(stamp => stamp.Exists && FileSystemPathSemantics.PathsEqual(stamp.Path, nfoTarget)))
                    throw new RpcError(-32010, "SOURCE_CHANGED", "An existing NFO has not been loaded for round-trip editing. Reimport its movie before replacing it.");
            }
            item.Plan = item.Plan with { PreviewTargetExpectations = writes.Select(path => stamps.Single(s => FileSystemPathSemantics.PathsEqual(s.Path, path)))
                .Select(stamp => new TargetFileExpectation(stamp.Path, stamp.Exists, stamp.Hash)).ToArray() };
        }
        return items;
    }

    private static BatchSavePlanItem ProtectedPreviewItem(PreviewEntry captured, MovieSaveConfiguration config, IEnumerable<string> paths)
    {
        var primary = captured.VideoPaths[0];
        var plan = new SavePlan(primary, primary, Path.GetDirectoryName(primary)!, Path.GetFileNameWithoutExtension(primary), config.SaveOptions, config.OrganizationOptions,
            paths.Select(path => new PlannedFileChange(PlannedChangeKind.KeepFile, "Protect queued input", path, path)).ToArray(), [], []) { VideoTransfers = [] };
        return new(captured.Entry.Job, plan, captured.Revision);
    }
    private sealed record PreviewEntry(Entry Entry, long Revision, string[] VideoPaths, LocalSaveContext Context, MovieMetadata? Metadata);

    private Entry[] RequestedEntries(JsonElement p, bool requireRevisions)
    {
        Entry[] entries;
        if (p.TryGetProperty("jobIds", out var ids))
        {
            if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is 0 or > 1000) throw new RpcError(-32602, "INVALID_PARAMS", "Choose 1 to 1000 unique jobs.");
            var values = ids.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToArray();
            if (values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw new RpcError(-32602, "INVALID_PARAMS", "Job IDs must be unique.");
            entries = values.Select(id => _jobs.GetValueOrDefault(id) ?? throw new RpcError(-32602, "JOB_NOT_FOUND", "A selected job is no longer in the workspace.")).ToArray();
            foreach (var entry in entries)
            {
                EnsureIdle(entry);
                if (requireRevisions && (!p.TryGetProperty("expectedReviewRevisions", out var revisions) || revisions.ValueKind != JsonValueKind.Object || !revisions.TryGetProperty(entry.Id, out var revision) || !revision.TryGetInt64(out var value) || value != entry.Job.ReviewRevision))
                    throw new RpcError(-32010, "REVISION_CONFLICT", "Refresh every selected job before generating a batch preview.", Snapshot(entry));
            }
        }
        else { var entry = GetJob(p); EnsureIdle(entry); if (requireRevisions) RequireRevision(entry, p); entries = [entry]; }
        return entries;
    }

    private MovieSaveConfiguration ParseConfiguration(JsonElement options, bool? defaultIncludeId = null)
    {
        if (options.ValueKind != JsonValueKind.Object) throw new RpcError(-32602, "INVALID_PARAMS", "Options must be an object.");
        foreach (var option in options.EnumerateObject())
            if (option.Name is not ("writeNfo" or "downloadPoster" or "downloadFanart" or "downloadExtrafanart" or "replaceLocalExtrafanart" or "overwriteExisting" or "includeIdInTitle" or "targetMode" or "renameVideo" or "customRootDirectory"))
                throw new RpcError(-32602, "INVALID_PARAMS", "Unknown save option: " + option.Name);
        var mode = options.TryGetProperty("targetMode", out _) ? Text(options, "targetMode") : "VideoDirectory";
        if (!Enum.TryParse<OrganizationTargetMode>(mode, false, out var target) || !Enum.IsDefined(target)) throw new RpcError(-32602, "INVALID_PARAMS", "Unknown organization mode.");
        var root = options.TryGetProperty("customRootDirectory", out var rootValue) && rootValue.ValueKind != JsonValueKind.Null ? Text(options, "customRootDirectory") : null;
        if (target == OrganizationTargetMode.CustomRootNumberFolder && (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !Directory.Exists(root)))
            throw new RpcError(-32010, "VOLUME_UNAVAILABLE", "Choose an existing, available custom root directory.");
        // Native forms retain an empty/stale custom-root field when another mode is selected.
        // Only the active custom-root mode gives that field filesystem meaning.
        root = target == OrganizationTargetMode.CustomRootNumberFolder ? FileSystemPathSemantics.CanonicalizePath(root!) : null;
        return new(new SaveOptions(Bool(options, "writeNfo", true), Bool(options, "downloadPoster", false), Bool(options, "downloadFanart", false), Bool(options, "downloadExtrafanart", false), Bool(options, "includeIdInTitle", defaultIncludeId ?? _preferences.Preferences.IncludeIdInTitle), Bool(options, "overwriteExisting", false), Bool(options, "replaceLocalExtrafanart", false)),
            new OrganizationOptions(target, Bool(options, "renameVideo", false), root, CrossVolumeVerificationMode.FullSha256));
    }

    private static PreviewItem CreatePreviewItem(PreviewEntry entry, PreviewEntry[] sources, SavePlan plan)
    {
        var item = new PreviewItem(entry.Entry, sources.Select(source => source.Entry).ToArray(), plan, sources.ToDictionary(source => source.Entry.Id, source => source.Revision));
        item.Blockers.AddRange(plan.BlockingConflicts);
        if (plan.OverwriteConflicts.Count > 0 && !plan.SaveOptions.OverwriteExisting) item.Blockers.Add("Existing sidecar replacement requires explicit confirmation.");
        item.Warnings.AddRange(plan.OverwriteConflicts);
        if (plan.VideoWillMove) item.Warnings.Add("Videos will be copied with full SHA-256 verification, committed, then originals retired. Real device-loss scenarios require separate validation.");
        if (!plan.SaveOptions.WriteNfo) item.Warnings.Add("NFO output is disabled; edited metadata will not be written.");
        return item;
    }
    private static LocalSidecarPaths LocateSidecars(Entry entry) => LocateSidecars(entry.Job.VideoPaths);
    private static LocalSidecarPaths LocateSidecars(IReadOnlyList<string> videoPaths)
    {
        // Imports resolve user-selected aliases once. A registered source must not be
        // silently rebound to a different movie by a later file or ancestor link.
        try
        {
            foreach (var path in videoPaths) FileSystemPathSemantics.ValidateCanonicalPath(path);
        }
        catch (IOException)
        {
            throw new RpcError(-32010, "SOURCE_CHANGED", "A registered video path was redirected. Resolve the link and reimport before saving.");
        }
        var set = MovieFileSet.Create(videoPaths);
        return LocalSidecarLocator.Locate(set.PrimaryPath, set.MovieBaseName, set.UsesMultipartNaming);
    }
    private static LocalSidecarPaths ValidateNfoBaseline(Entry entry) =>
        ValidateNfoBaseline(entry.Job.VideoPaths, entry.Job.LocalMetadataBundle?.Sidecars.NfoPath);
    private static LocalSidecarPaths ValidateNfoBaseline(IReadOnlyList<string> videoPaths, string? baseline)
    {
        var sidecars = LocateSidecars(videoPaths);
        if (sidecars.NfoPath is not null)
        {
            try { FileSystemPathSemantics.ValidateCanonicalPath(sidecars.NfoPath); }
            catch (IOException) { throw new RpcError(-32010, "SOURCE_CHANGED", "The source NFO path was redirected. Resolve the link and reimport before saving."); }
        }
        if ((baseline is null) != (sidecars.NfoPath is null) ||
            baseline is not null && sidecars.NfoPath is not null && !FileSystemPathSemantics.PathsEqual(baseline, sidecars.NfoPath))
            throw new RpcError(-32010, "SOURCE_CHANGED", "The source NFO appeared, disappeared or changed location since import. Reimport the movie to retain its original XML.");
        return sidecars;
    }
    private static IEnumerable<string> Inputs(Entry entry, LocalSidecarPaths? currentSidecars = null)
    {
        var sidecars = currentSidecars ?? LocateSidecars(entry);
        return entry.Job.VideoPaths.Concat(entry.Job.LocalExtrafanartPaths).Concat(new[] { sidecars.NfoPath, sidecars.PosterPath, sidecars.FanartPath }.OfType<string>()).Concat(sidecars.ExtrafanartPaths).Distinct(FileSystemPathSemantics.PathComparer).ToArray();
    }
    private static BatchSavePlanItem ProtectedItem(Entry entry, MovieSaveConfiguration config, IEnumerable<string>? paths = null)
    {
        var primary = entry.Job.VideoPaths[0];
        var plan = new SavePlan(primary, primary, Path.GetDirectoryName(primary)!, Path.GetFileNameWithoutExtension(primary), config.SaveOptions, config.OrganizationOptions,
            (paths ?? Inputs(entry)).Select(path => new PlannedFileChange(PlannedChangeKind.KeepFile, "Protect queued input", path, path)).ToArray(), [], []) { VideoTransfers = [] };
        return new(entry.Job, plan, entry.Job.ReviewRevision);
    }
    private static IEnumerable<string> WriteTargets(SavePlan plan) => plan.Changes.Where(c => c.Kind is not (PlannedChangeKind.KeepFile or PlannedChangeKind.CreateFolder)).Select(c => c.DestinationPath)
        .Concat(plan.SidecarTransfers.Where(t => !FileSystemPathSemantics.PathsEqual(t.SourcePath, t.DestinationPath)).Select(t => t.DestinationPath)).Distinct(FileSystemPathSemantics.PathComparer);
    private static void ValidateConfirmedParts(PreviewEntry[] entries)
    {
        if (entries.Length < 2 || entries.Any(e => e.VideoPaths.Length != 1)) throw new RpcError(-32602, "INVALID_PARTS", "Choose at least two single-file movies in explicit part order.");
        var id = MovieIdParser.Normalize(entries[0].Metadata!.Id);
        var directory = Path.GetDirectoryName(entries[0].VideoPaths[0])!;
        if (string.IsNullOrWhiteSpace(id) || entries.Any(e => MovieIdParser.Normalize(e.Metadata!.Id) != id || !FileSystemPathSemantics.PathsEqual(Path.GetDirectoryName(e.VideoPaths[0])!, directory)))
            throw new RpcError(-32602, "INVALID_PARTS", "Confirmed parts must share an ID and source directory. The first job supplies reviewed metadata.");
        _ = MovieFileSet.CreateConfirmedParts(entries.Select(e => e.VideoPaths[0]), id);
    }
    private static object PreviewSnapshot(Preview preview)
    {
        var jobs = preview.Items.Select(i => new { jobId = i.Entry.Id, reviewRevision = i.Revision, sourceJobIds = i.SourceEntries.Select(e => e.Id).ToArray(),
            changes = i.Plan.Changes.Select(c => new { operation = c.Kind.ToString(), source = c.SourcePath, target = c.DestinationPath, reason = c.Description, conflict = c.IsBlocking || c.RequiresOverwrite && !i.Plan.SaveOptions.OverwriteExisting }).ToArray(), warnings = i.Warnings, blockingConflicts = i.Blockers }).ToArray();
        return new { planId = preview.Id, jobId = jobs.Length == 1 ? jobs[0].jobId : null, reviewRevision = jobs.Length == 1 ? jobs[0].reviewRevision : 0,
            sourceJobIds = preview.Entries.Select(e => e.Id).ToArray(), confirmedParts = preview.ConfirmedParts, canExecute = preview.Items.All(i => i.Blockers.Count == 0), jobs,
            changes = jobs.Length == 1 ? jobs[0].changes : [], warnings = preview.Items.SelectMany(i => i.Warnings).ToArray(), blockingConflicts = preview.Items.SelectMany(i => i.Blockers).ToArray() };
    }
    private static async Task<FileStamp> CaptureAsync(string path, bool hash, CancellationToken token)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || Directory.Exists(path)) throw new RpcError(-32010, "PATH_CONFLICT", "The preview path is linked, ambiguous or occupied by a directory.");
        var exists = info.Exists;
        var length = exists ? info.Length : 0;
        var ticks = exists ? info.LastWriteTimeUtc.Ticks : 0;
        string? digest = null;
        if (exists && hash)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            info.Refresh();
            if (!info.Exists || info.Length != length || info.LastWriteTimeUtc.Ticks != ticks) throw new RpcError(-32010, "SOURCE_CHANGED", "A file changed while the preview was being prepared.");
        }
        return new(path, exists, length, ticks, digest);
    }
    private static async Task ValidateStampsAsync(PreviewItem item, CancellationToken token)
    {
        foreach (var entry in item.SourceEntries) ValidateNfoBaseline(entry);
        foreach (var stamp in item.Stamps)
            if (await CaptureAsync(stamp.Path, stamp.Hash is not null, token) != stamp)
                throw new RpcError(-32010, "SOURCE_CHANGED", "A file changed since preview. Reimport externally edited NFO files and generate a new preview.");
    }
    private void QueueChanged() { _queueVersion++; foreach (var plan in _plans.Values.Where(p => p.OperationId is null)) plan.Stale = true; }
    private sealed class Preview(PreviewItem[] items, long queueVersion, bool confirmedParts)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N"); public PreviewItem[] Items { get; } = items;
        public Entry[] Entries => Items.SelectMany(i => i.SourceEntries).Distinct().ToArray();
        public long QueueVersion { get; } = queueVersion; public bool ConfirmedParts { get; } = confirmedParts;
        public bool Stale; public string? OperationId;
        public long CreatedTimestamp { get; } = System.Diagnostics.Stopwatch.GetTimestamp();
        public long RetainedBytes;
    }
    private sealed class PreviewItem(Entry entry, Entry[] sources, SavePlan plan, Dictionary<string, long> sourceRevisions)
    {
        public Entry Entry { get; } = entry; public Entry[] SourceEntries { get; } = sources; public SavePlan Plan { get; set; } = plan;
        public long Revision => SourceRevisions[Entry.Id];
        public Dictionary<string, long> SourceRevisions { get; } = sourceRevisions;
        public FileStamp[] Stamps = []; public List<string> Blockers { get; } = []; public List<string> Warnings { get; } = []; public List<string> ExtraKeptPaths { get; } = [];
    }
    private sealed record FileStamp(string Path, bool Exists, long Length, long WriteTicks, string? Hash);
}
