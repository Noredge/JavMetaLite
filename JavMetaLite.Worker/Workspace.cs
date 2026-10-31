using System.Security.Cryptography;
using System.Text.Json;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using JavMetaLite.Imaging.Skia;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly RpcWriter _wire;
    private readonly Dictionary<string, Entry> _jobs = [];
    private readonly Dictionary<string, Operation> _operations = [];
    private readonly Dictionary<string, Preview> _plans = [];
    private readonly IArtworkImageProcessor _images = new SkiaArtworkImageProcessor();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _searchSlots = new(2, 2);
    private readonly Func<string, IMetadataProvider> _providerFactory;
    private readonly TimeSpan? _providerTimeout;
    private readonly Func<OutputService, FileOrganizationService> _organizationFactory;
    private readonly Func<IEnumerable<string>, bool, CancellationToken, Task<MovieInputDiscoveryResult>> _inputDiscovery;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<CancellationToken, Task> _beforePreviewRead;
    private readonly Action<string, string, CancellationToken>? _afterSidecarInventoryCaptured;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _dataRoot;
    private readonly string _cacheRoot;
    private readonly ArtworkDiskCache _artworkCache;
    private readonly string _recoveryRoot;
    private readonly AppPreferencesStore _preferencesStore;
    private readonly List<string> _recoveryWarnings = [];
    private AppPreferencesLoadResult _preferences;
    private bool _initialized;
    private bool _saveActive;
    private int _importsActive;
    public bool ShuttingDown { get; private set; }

    public Workspace(RpcWriter wire, string? dataRoot,
        Func<string, IMetadataProvider>? providerFactory = null, TimeSpan? providerTimeout = null,
        Func<OutputService, FileOrganizationService>? organizationFactory = null,
        Func<IEnumerable<string>, bool, CancellationToken, Task<MovieInputDiscoveryResult>>? inputDiscovery = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<CancellationToken, Task>? beforePreviewRead = null,
        ArtworkDiskCacheLimits? artworkCacheLimits = null,
        Action<string, string, CancellationToken>? afterSidecarInventoryCaptured = null)
    {
        _wire = wire;
        _providerFactory = providerFactory ?? (source => source switch
        { "libredmm" => new LibreDmmClient(), "r18dev" => new R18DevClient(), _ => throw new ArgumentException("Unknown provider.") });
        _providerTimeout = providerTimeout;
        _organizationFactory = organizationFactory ?? (output => new FileOrganizationService(output));
        _inputDiscovery = inputDiscovery ?? MovieInputDiscovery.DiscoverAsync;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _beforePreviewRead = beforePreviewRead ?? (_ => Task.CompletedTask);
        _afterSidecarInventoryCaptured = afterSidecarInventoryCaptured;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _dataRoot = dataRoot ?? Path.Combine(home, "Library", "Application Support", "JavMetaLite");
        _cacheRoot = FileSystemPathSemantics.CanonicalizePath(Path.Combine(dataRoot is null ? Path.Combine(home, "Library", "Caches", "JavMetaLite") : Path.Combine(dataRoot, "cache"), "artwork", _sessionId));
        var recoveryRoot = Path.Combine(_dataRoot, "recovery");
        if (new DirectoryInfo(recoveryRoot).LinkTarget is not null) throw new IOException("Recovery root must not be a symbolic link.");
        _recoveryRoot = FileSystemPathSemantics.CanonicalizePath(recoveryRoot);
        _artworkCache = new ArtworkDiskCache(_cacheRoot, artworkCacheLimits);
        Directory.CreateDirectory(_recoveryRoot);
        AppLog.ConfigureDirectory(dataRoot is null ? Path.Combine(home, "Library", "Logs", "JavMetaLite") : Path.Combine(dataRoot, "logs"));
        _preferencesStore = new AppPreferencesStore(_dataRoot);
        _preferences = _preferencesStore.Load();
        InitializeRecovery();
    }

    public async Task<object> Dispatch(string method, JsonElement p)
    {
        if (method == "initialize")
        {
            if (!p.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var n) || n != 1)
            { _initialized = false; throw new RpcError(-32010, "PROTOCOL_VERSION_MISMATCH", "Client and bundled engine versions do not match."); }
            _initialized = true;
            return new { protocolVersion = 1, workerVersion = "1.0.0", sessionId = _sessionId, cacheRoot = _cacheRoot,
                capabilities = new { readLocal = true, searchLibreDmm = true, writeNfo = true, searchR18Dev = true, writeArtwork = true, organizeVideo = true, manualBrowserImport = true, localArtworkTrash = true, batchSave = true, confirmParts = true, discoverInputs = true, selectReviewSource = true, artworkPreviewLeases = true, transactionRecovery = true },
                discoveryLimits = new { maximumTokens = MaximumDiscoveries, maximumItems = MaximumDiscoveryItems, maximumResultBytes = MaximumDiscoveryBytes, expiresAfterSeconds = DiscoveryLifetime.TotalSeconds } };
        }
        if (!_initialized) throw new RpcError(-32010, "NOT_INITIALIZED", "Initialize the engine before sending commands.");
        lock (_gate)
            if (_recoveryActive && method is not ("operations.get" or "operations.cancel" or "workspace.getSnapshot" or "artwork.releasePreview" or "shutdown"))
                throw new RpcError(-32010, "JOB_BUSY", "Wait for transaction recovery verification to finish.");
        switch (method)
        {
            case "workspace.getSnapshot": lock (_gate) { PruneDiscoveries(); PruneRetainedState(); return new { protocolVersion = 1, jobs = _jobs.Values.Select(Snapshot).ToArray(), activeOperations = _operations.Values.Where(x => x.Status == "running").Select(OperationSnapshot).ToArray(), recoveryWarnings = _recoveryWarnings.ToArray(), activeTrash = TrashSnapshot(), retention = RetentionSnapshot(), artworkCache = _artworkCache.Snapshot() }; }
            case "workspace.importInputs": return Import(p);
            case "recovery.list": return ListRecovery(p);
            case "recovery.verify": return VerifyRecovery(p);
            case "recovery.archive": return ArchiveRecovery(p);
            case "workspace.discoverInputs": return DiscoverInputs(p);
            case "workspace.importDiscovery": return ImportDiscovery(p);
            case "workspace.discardDiscovery": return DiscardDiscovery(p);
            case "jobs.get": lock (_gate) return Snapshot(GetJob(p));
            case "jobs.applyEdits": return ApplyEdits(p);
            case "jobs.selectCandidate": return SelectCandidate(p);
            case "jobs.selectSource": return SelectReviewSource(p);
            case "jobs.search": return Search(p, false);
            case "jobs.retryFailedSources": return Search(p, true);
            case "jobs.remove": return RemoveJobs(p);
            case "artwork.getPreview": return await GetArtworkPreview(p);
            case "artwork.releasePreview":
                var leaseId = Text(p, "leaseId");
                _artworkCache.Release(leaseId);
                return new { released = true, leaseId };
            case "artwork.select": return SelectArtwork(p);
            case "artwork.selectSource": return SelectArtworkSource(p);
            case "artwork.prepareTrash": return await PrepareTrash(p);
            case "artwork.validateTrash": return await ValidateTrash(p);
            case "artwork.abortTrash": return await AbortTrash(p);
            case "artwork.completeTrash": return await CompleteTrash(p);
            case "browser.getTargets": return BrowserTargets(p);
            case "browser.resolveTarget": return ResolveBrowserTarget(p);
            case "browser.getSupport": return BrowserSupport(p);
            case "browser.importPage": return ImportBrowserPage(p);
            case "save.preview": return await PreviewSaveAsync(p);
            case "save.execute": return ExecuteSave(p);
            case "operations.get": lock (_gate) return OperationSnapshot(GetOperation(p));
            case "operations.cancel": lock (_gate) { var op = GetOperation(p); if (op.Status == "running") op.Cancellation.Cancel(); return new { received = true, operationId = op.Id, status = op.Status }; }
            case "preferences.get": return PreferencesSnapshot();
            case "preferences.update":
                lock (_gate) EnsureNoTrash();
                if (!_preferences.CanOverwrite) throw new RpcError(-32010, "PREFERENCES_VERSION_UNSUPPORTED", "A newer settings schema cannot be overwritten.");
                _preferences = _preferences with { Preferences = _preferences.Preferences with
                { IncludeIdInTitle = Bool(p, "includeIdInTitle", _preferences.Preferences.IncludeIdInTitle), RememberSavePreferences = true,
                  SkipSavePreview = false, SkipBatchSavePreview = false, DownloadPoster = false, DownloadFanart = false, DownloadExtrafanart = false, RenameVideo = false,
                  TargetMode = OrganizationTargetMode.VideoDirectory } };
                _preferencesStore.Save(_preferences.Preferences);
                return PreferencesSnapshot();
            case "shutdown": ShuttingDown = true; await Stop(); return new { stopped = true };
            default: throw new RpcError(-32601, "METHOD_NOT_FOUND", "Unknown method.");
        }
    }

    private object PreferencesSnapshot() => new { includeIdInTitle = _preferences.Preferences.IncludeIdInTitle, overwriteExisting = false,
        searchSource = "libredmm", dataRoot = _dataRoot, cacheRoot = _cacheRoot, logRoot = AppLog.LogDirectory,
        warning = _preferences.Warning, canOverwrite = _preferences.CanOverwrite };

    private object Import(JsonElement p)
    {
        var paths = DiscoveryInputPaths(p);
        var recursive = Bool(p, "recursive", true);
        lock (_gate)
        {
            EnsureNoTrash();
            if (_saveActive) throw new RpcError(-32010, "JOB_BUSY", "Import is unavailable during a save; wait until its protected input set is released.");
            if (_discoveryActive || _confirmedImportActive) throw new RpcError(-32010, "JOB_BUSY", "Wait for the current discovery or confirmed import.");
            _importsActive++;
        }
        return StartOperation(null, async op =>
        {
            try
            {
            Progress(op, "discovering");
            var discovery = await _inputDiscovery(paths, recursive, op.Cancellation.Token);
            var warnings = discovery.Diagnostics.Select(x => $"{x.Path}: {x.Message}").ToList();
            var imported = new List<string>();
            var duplicates = new List<string>();
            foreach (var fileSet in discovery.MovieFileSets)
            {
                op.Cancellation.Token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    var existing = _jobs.Values.FirstOrDefault(e => e.Job.VideoPaths.Any(path => fileSet.VideoPaths.Any(candidate => SameInput(path, candidate))));
                    if (existing is not null) { duplicates.Add(existing.Id); continue; }
                }
                Entry? entry = null;
                try
                {
                    entry = await LoadDiscoveredEntry(fileSet, op.Cancellation.Token);
                    lock (_gate)
                    {
                        var duplicate = _jobs.Values.FirstOrDefault(e => e.Job.VideoPaths.Any(path => fileSet.VideoPaths.Any(candidate => SameInput(path, candidate))));
                        if (duplicate is not null) { duplicates.Add(duplicate.Id); entry.Job.Dispose(); continue; }
                        if (_saveActive) throw new RpcError(-32010, "JOB_BUSY", "A save is active; reimport after it safely stops.");
                        EnsureSnapshotCapacity(entry);
                        _jobs.Add(entry.Id, entry); QueueChanged(); imported.Add(entry.Id);
                    }
                    Progress(op, "loadingLocal", new { discovered = imported.Count });
                }
                catch (OperationCanceledException) { entry?.Job.Dispose(); throw; }
                catch (Exception error) { entry?.Job.Dispose(); warnings.Add($"{fileSet.PrimaryPath}: {error.Message}"); }
            }
            return new { importedJobIds = imported, duplicateJobIds = duplicates, warnings,
                diagnostics = discovery.Diagnostics.Select(x => new { path = x.Path, message = x.Message, kind = x.Kind.ToString() }).ToArray(),
                ignoredFileCount = discovery.IgnoredFileCount, skippedDirectoryCount = discovery.SkippedDirectoryCount };
            }
            finally { lock (_gate) _importsActive--; }
        });
    }

    private async Task<Entry> LoadDiscoveredEntry(MovieFileSet fileSet, CancellationToken token, LocalSidecarInventoryScope? inventory = null)
    {
        var job = new MovieJob();
        try
        {
            // IEnumerable overload preserves the exact reviewed parts; it does not discover new siblings.
            var loaded = await MovieJobLoader.LoadAsync(job, fileSet.VideoPaths, _images, inventory, token);
            var entry = new Entry(job);
            if (loaded.MetadataError is not null) entry.Warnings.Add(loaded.MetadataError.Message);
            if (loaded.ArtworkDiscovery is { } artwork) entry.Warnings.AddRange(artwork.Diagnostics);
            entry.Warnings.AddRange(job.LocalMetadataBundle?.Diagnostics ?? []);
            job.InitializeSaveConfiguration(Configuration(false, _preferences.Preferences.IncludeIdInTitle));
            entry.SavedRevision = job.ReviewRevision;
            return entry;
        }
        catch { job.Dispose(); throw; }
    }

    private object ApplyEdits(JsonElement p)
    {
        lock (_gate)
        {
            var e = GetJob(p); EnsureIdle(e); RequireRevision(e, p);
            var edits = p.GetProperty("edits");
            if (edits.ValueKind != JsonValueKind.Object) throw new RpcError(-32602, "INVALID_PARAMS", "Edits must be an object.");
            // Validate the whole command before changing any field.
            foreach (var edit in edits.EnumerateObject())
                if (edit.Value.ValueKind != JsonValueKind.String || !EditableFields.Contains(edit.Name))
                    throw new RpcError(-32602, "INVALID_PARAMS", "Unknown or non-string editable field: " + edit.Name);
            var editBytes = JsonSerializer.SerializeToUtf8Bytes(edits, JsonDefaults.Options).Length;
            if (editBytes > 1024 * 1024)
                throw new RpcError(-32602, "EDIT_TOO_LARGE", "One edit command must be at most 1 MiB; no fields were changed.");
            // A value appears in metadata and its manual candidate. Reserve space before mutation.
            EnsureSnapshotCapacity(additionalBytes: 3L * editBytes);
            foreach (var edit in edits.EnumerateObject())
            {
                var value = edit.Value.GetString()!;
                if (FieldMap.TryGetValue(edit.Name, out var field)) e.Job.MetadataReview.SetManualValue(field, value);
                else switch (edit.Name)
                {
                    case "id": e.Job.ChangeReviewedId(value); break;
                    case "series": e.Job.Metadata.Series = value; break;
                    case "contentId": e.Job.Metadata.ContentId = value; break;
                    case "sourceUrl": e.Job.Metadata.SourceUrl = value; break;
                }
            }
            InvalidatePlans(e.Id);
            return Snapshot(e);
        }
    }

    private object SelectCandidate(JsonElement p)
    {
        lock (_gate)
        {
            var e = GetJob(p); EnsureIdle(e); RequireRevision(e, p);
            if (!FieldMap.TryGetValue(Text(p, "field"), out var field))
                throw new RpcError(-32602, "INVALID_PARAMS", "Unknown candidate field.");
            var source = Text(p, "source");
            var candidate = e.Job.MetadataReview.GetCandidates(field).FirstOrDefault(item =>
                string.Equals(item.Source.Name, source, StringComparison.OrdinalIgnoreCase))
                ?? throw new RpcError(-32602, "INVALID_PARAMS", "Candidate no longer exists.");
            if (!candidate.Source.IsManual && source != "local-nfo" && !IsCurrentOnlineSource(e, source))
                throw new RpcError(-32602, "INVALID_PARAMS", "This cached candidate belongs to a different movie ID. Query the current movie again.");
            EnsureSnapshotCapacity(additionalBytes: 2L * JsonSerializer.SerializeToUtf8Bytes(candidate.Value, JsonDefaults.Options).Length);
            e.Job.MetadataReview.SelectCandidate(field, source);
            InvalidatePlans(e.Id); return Snapshot(e);
        }
    }

    private object Snapshot(Entry e)
    {
        var job = e.Job;
        return new { jobId = e.Id, reviewRevision = job.ReviewRevision,
            metadata = JsonSerializer.SerializeToElement(job.Metadata, JsonDefaults.Options), videoPaths = job.VideoPaths.ToArray(),
            localNfoPath = job.LocalMetadataBundle?.Sidecars.NfoPath, searchState = job.SearchState.ToString(), saveState = job.SaveState.ToString(),
            hasUnsavedChanges = job.ReviewRevision != e.SavedRevision, localNfoSaveBlocked = job.LocalNfoSaveBlocked,
            hasFailedSources = job.HasFailedSources, hasPartialSourceFailure = job.HasPartialSourceFailure, hasRateLimitedSource = job.HasRateLimitedSource,
            searchAttempts = job.LastSearchAttempts.Select(SearchAttemptSnapshot).ToArray(), coverQuality = CoverQuality(e),
            warnings = e.Warnings.Concat(job.LastSearchError is null ? [] : new[] { job.LastSearchError.Message }).Concat(job.LastSaveError is null ? [] : new[] { job.LastSaveError.Message }).ToArray(),
            reviewSources = AvailableReviewSources(e), reviewSource = CurrentReviewSource(e),
            busy = e.Busy, canReplaceLocalExtrafanart = job.CanReplaceLocalExtrafanart, candidates = FieldMap.SelectMany(pair => job.MetadataReview.GetCandidates(pair.Value).Select(candidate => new
            { field = pair.Key, source = candidate.Source.Name, sourceDisplayName = candidate.Source.DisplayName, value = candidate.Value,
                selected = job.MetadataReview.GetSelectedCandidate(pair.Value) == candidate })).ToArray(),
            artwork = ArtworkSources(e).Select(a => new { a.ArtworkId, a.Kind, a.Source, a.Selected, a.CandidateSource, contentKey = ArtworkContentKey(a) }).ToArray() };
    }

    // Keep mandatory session snapshots representable within the 8 MiB wire frame.
    // This is a capacity rejection, never a truncation or silent loss of imported metadata.
    private void EnsureSnapshotCapacity(Entry? addition = null, long additionalBytes = 0)
    {
        const long maximumSnapshotBytes = 7L * 1024 * 1024;
        long bytes = additionalBytes + 64 * 1024;
        foreach (var entry in _jobs.Values.Concat(addition is null ? [] : new[] { addition }))
        {
            bytes += JsonSerializer.SerializeToUtf8Bytes(Snapshot(entry), JsonDefaults.Options).Length;
            if (bytes > maximumSnapshotBytes)
                throw new RpcError(-32010, "WORKSPACE_CAPACITY", "The workspace metadata exceeds the safe response limit. Remove completed jobs or use smaller metadata values.");
        }
    }

    private object RemoveJobs(JsonElement p)
    {
        lock (_gate)
        {
            var bulk = p.TryGetProperty("jobIds", out var values);
            string[] ids;
            if (bulk)
            {
                if (p.TryGetProperty("jobId", out _) || values.ValueKind != JsonValueKind.Array ||
                    values.GetArrayLength() == 0 || values.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString())))
                    throw new RpcError(-32602, "INVALID_PARAMS", "Provide either one jobId or a nonempty array of unique jobIds.");
                ids = values.EnumerateArray().Select(v => v.GetString()!).ToArray();
                if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                    throw new RpcError(-32602, "INVALID_PARAMS", "Queue removal jobIds must be unique.");
            }
            else ids = [Text(p, "jobId")];
            // Resolve and validate the complete request under the same lock
            // before removing anything. A later unknown/busy job cannot cause
            // a partial clear. This operation never touches media or sidecars.
            var entries = ids.Select(id => _jobs.GetValueOrDefault(id) ?? throw new RpcError(-32602, "JOB_NOT_FOUND", "The job is no longer in this workspace.")).ToArray();
            if (_confirmedImportActive || bulk && (_saveActive || _importsActive > 0 || _discoveryActive || _jobs.Values.Any(e => e.Busy)))
                throw new RpcError(-32010, "JOB_BUSY", "Wait for imports and saves to safely finish before clearing the queue.");
            foreach (var entry in entries) EnsureIdle(entry);
            foreach (var entry in entries) _jobs.Remove(entry.Id);
            QueueChanged();
            foreach (var entry in entries) { _artworkCache.RemoveJob(entry.Id); entry.Job.Dispose(); }
            return new { removed = true, removedIds = ids, removedCount = ids.Length };
        }
    }

    private Entry GetJob(JsonElement p) => _jobs.GetValueOrDefault(Text(p, "jobId")) ?? throw new RpcError(-32602, "JOB_NOT_FOUND", "The job is no longer in this workspace.");
    private Operation GetOperation(JsonElement p) { PruneRetainedState(); return _operations.GetValueOrDefault(Text(p, "operationId")) ?? throw new RpcError(-32602, "OPERATION_NOT_FOUND", "Unknown operation in this session."); }
    private void EnsureIdle(Entry e) { EnsureNoTrash(); if (e.Busy) throw new RpcError(-32010, "JOB_BUSY", "Wait for this job's current operation to finish."); }
    private void RequireRevision(Entry e, JsonElement p)
    { if (!p.TryGetProperty("expectedReviewRevision", out var rev) || !rev.TryGetInt64(out var value) || value != e.Job.ReviewRevision) throw new RpcError(-32010, "REVISION_CONFLICT", "The review changed. Refresh before editing.", Snapshot(e)); }
    private void InvalidatePlans(string jobId) { foreach (var plan in _plans.Values.Where(x => x.Entries.Any(e => e.Id == jobId) && x.OperationId is null)) plan.Stale = true; }
    private static string Text(JsonElement p, string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new RpcError(-32602, "INVALID_PARAMS", "Missing string: " + key);
    private static bool Bool(JsonElement p, string key, bool fallback) => p.TryGetProperty(key, out var value) ? value.GetBoolean() : fallback;
    private static bool SameInput(string a, string b) => string.Equals(CanonicalInput(a), CanonicalInput(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string CanonicalInput(string path) => FileSystemPathSemantics.CanonicalizePath(path);
    private static MovieSaveConfiguration Configuration(bool overwrite, bool includeId) => new(new SaveOptions(true, false, false, false, includeId, overwrite), new OrganizationOptions(OrganizationTargetMode.VideoDirectory, false));
    private static readonly Dictionary<string, MetadataField> FieldMap = new()
    { ["title"] = MetadataField.Title, ["originalTitle"] = MetadataField.OriginalTitle, ["releaseDate"] = MetadataField.ReleaseDate,
      ["runtimeMinutes"] = MetadataField.RuntimeMinutes, ["maker"] = MetadataField.Maker, ["director"] = MetadataField.Director,
      ["label"] = MetadataField.Label, ["actorsText"] = MetadataField.Actors, ["genresText"] = MetadataField.Genres,
      ["plot"] = MetadataField.Plot, ["rating"] = MetadataField.Rating };
    private static readonly HashSet<string> EditableFields = [.. FieldMap.Keys, "id", "sourceUrl"];
    private sealed class Entry(MovieJob job)
    { public string Id { get; } = Guid.NewGuid().ToString("N"); public MovieJob Job { get; } = job; public long SavedRevision; public bool Busy; public List<string> Warnings { get; } = []; }
}
