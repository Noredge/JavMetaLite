using System.Text.Json;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private const int MaximumDiscoveries = 4;
    private const int MaximumDiscoveryItems = 10000;
    private const int MaximumDiscoveryBytes = 6 * 1024 * 1024;
    private static readonly TimeSpan DiscoveryLifetime = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, Discovery> _discoveries = [];
    private bool _discoveryActive;
    private bool _confirmedImportActive;

    private static string[] DiscoveryInputPaths(JsonElement p)
    {
        if (!p.TryGetProperty("paths", out var values) || values.ValueKind != JsonValueKind.Array)
            throw new RpcError(-32602, "INVALID_PARAMS", "Paths must be an array.");
        var paths = values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToArray();
        if (paths.Length is 0 or > MaximumDiscoveryItems || paths.Any(v => string.IsNullOrWhiteSpace(v) || !Path.IsPathFullyQualified(v)))
            throw new RpcError(-32602, "INVALID_PARAMS", "Choose between 1 and 10000 absolute file or folder paths.");
        return paths;
    }

    private void EnsureDiscoveryAvailable()
    {
        EnsureNoTrash();
        if (_saveActive || _discoveryActive || _importsActive > 0)
            throw new RpcError(-32010, "JOB_BUSY", "Wait for the current save, import or discovery to finish.");
    }

    private object DiscoverInputs(JsonElement p)
    {
        var paths = DiscoveryInputPaths(p);
        var recursive = Bool(p, "recursive", true);
        lock (_gate)
        {
            PruneDiscoveries();
            EnsureDiscoveryAvailable();
            if (_discoveries.Count >= MaximumDiscoveries)
                throw new RpcError(-32010, "DISCOVERY_CAPACITY", "Close an existing discovery before starting another.");
            return StartOperation(null, async op =>
            {
                try
                {
                    Progress(op, "discovering");
                    var found = await _inputDiscovery(paths, recursive, op.Cancellation.Token);
                    if (found.MovieFileSets.Count > MaximumDiscoveryItems)
                        throw new RpcError(-32010, "DISCOVERY_CAPACITY", "Discovery contains more than 10000 movies; choose smaller folders.");
                    var diagnostics = found.Diagnostics.ToList();
                    var items = new List<DiscoveryItem>();
                    foreach (var set in found.MovieFileSets)
                    {
                        op.Cancellation.Token.ThrowIfCancellationRequested();
                        DiscoveryStamp[]? stamps = null;
                        string? unavailable = null;
                        try { stamps = set.VideoPaths.Select(CaptureDiscoveryStamp).ToArray(); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or RpcError)
                        {
                            unavailable = error is RpcError { Reason: "IDENTITY_UNAVAILABLE" } ? "IDENTITY_UNAVAILABLE" : "SOURCE_UNAVAILABLE";
                            diagnostics.Add(MovieFileDiscoveryDiagnostic.FromException(set.PrimaryPath, error));
                        }
                        items.Add(new(Guid.NewGuid().ToString("N"), set, MovieIdParser.TryExtractFromBaseName(set.MovieBaseName) ?? "", stamps, unavailable));
                    }
                    lock (_gate)
                    {
                        op.Cancellation.Token.ThrowIfCancellationRequested();
                        var discovery = new Discovery(Guid.NewGuid().ToString("N"), op.Id, _utcNow() + DiscoveryLifetime,
                            items.ToArray(), diagnostics.ToArray(), found.IgnoredFileCount, found.SkippedDirectoryCount);
                        var payload = DiscoverySnapshot(discovery);
                        if (JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options).Length > MaximumDiscoveryBytes)
                            throw new RpcError(-32010, "DISCOVERY_CAPACITY", "Discovery exceeds the safe response size; choose smaller folders.");
                        _discoveries.Add(discovery.Id, discovery);
                        return payload;
                    }
                }
                finally { lock (_gate) _discoveryActive = false; }
            }, beforeStart: _ => _discoveryActive = true, readOnly: true);
        }
    }

    private object DiscoverySnapshot(Discovery discovery)
    {
        var existingJobs = DiscoveryQueueIndex();
        return new {
        discoveryId = discovery.Id, expiresAt = discovery.ExpiresAt,
        items = discovery.Items.Select(item =>
        {
            var existing = ExistingDiscoveryJob(item, existingJobs);
            return new { itemId = item.Id, movieId = item.MovieId, primaryPath = item.FileSet.PrimaryPath,
                videoPaths = item.FileSet.VideoPaths, alreadyImported = existing is not null,
                canImport = existing is null && item.Stamps is not null,
                existingJobId = existing?.Id, unavailableReason = existing is not null ? "ALREADY_IMPORTED" : item.UnavailableReason };
        }).ToArray(),
        warnings = discovery.Diagnostics.Select(x => $"{x.Path}: {x.Message}").ToArray(),
        diagnostics = discovery.Diagnostics.Select(x => new { path = x.Path, message = x.Message, kind = x.Kind.ToString() }).ToArray(),
        ignoredFileCount = discovery.IgnoredFileCount, skippedDirectoryCount = discovery.SkippedDirectoryCount
        };
    }

    private object ImportDiscovery(JsonElement p)
    {
        var id = Text(p, "discoveryId");
        if (!p.TryGetProperty("selectedItemIds", out var values) || values.ValueKind != JsonValueKind.Array)
            throw new RpcError(-32602, "INVALID_PARAMS", "Selected item IDs must be an array.");
        var selected = values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToArray();
        if (selected.Length is 0 or > MaximumDiscoveryItems || selected.Any(string.IsNullOrWhiteSpace) || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new RpcError(-32602, "INVALID_PARAMS", "Choose one or more distinct discovered item IDs.");
        lock (_gate)
        {
            PruneDiscoveries();
            EnsureDiscoveryAvailable();
            if (!_discoveries.TryGetValue(id, out var discovery)) throw StaleDiscovery();
            var items = selected.Select(itemId => discovery.Items.FirstOrDefault(item => item.Id == itemId)
                ?? throw new RpcError(-32602, "INVALID_PARAMS", "An item does not belong to this discovery.")).ToArray();
            if (items.Any(item => item.Stamps is null))
                throw new RpcError(-32010, "SOURCE_CHANGED", "A selected input could not be verified; discover it again.");
            var existingJobs = DiscoveryQueueIndex();
            // The result is a single-use confirmation. Existing drafts are not touched until
            // every selected entry is loaded, revalidated and admitted in one gate below.
            return StartOperation(null, async op =>
            {
                var staged = new List<(DiscoveryItem Item, Entry Entry)>();
                var duplicates = new HashSet<string>(StringComparer.Ordinal);
                var skipped = new List<DiscoveryItem>();
                var committed = false;
                var inventory = new LocalSidecarInventoryScope((directory, token) =>
                    _afterSidecarInventoryCaptured?.Invoke("importDiscovery", directory, token));
                try
                {
                    Progress(op, "validatingDiscovery");
                    foreach (var item in items) ValidateDiscoveredItem(item, op.Cancellation.Token);
                    foreach (var item in items)
                    {
                        op.Cancellation.Token.ThrowIfCancellationRequested();
                        lock (_gate)
                        {
                            if (ExistingDiscoveryJob(item, existingJobs) is { } existing) { duplicates.Add(existing.Id); skipped.Add(item); continue; }
                        }
                        var entry = await LoadDiscoveredEntry(item.FileSet, op.Cancellation.Token, inventory);
                        staged.Add((item, entry));
                        Progress(op, "loadingLocal", new { loaded = staged.Count, total = items.Length });
                    }
                    foreach (var item in items) ValidateDiscoveredItem(item, op.Cancellation.Token);
                    try { inventory.ValidateReusedDirectories(op.Cancellation.Token); }
                    catch (SidecarInventoryChangedException) { throw new RpcError(-32010, "SOURCE_CHANGED", "A sidecar directory changed during import. Discover inputs again."); }
                    lock (_gate)
                    {
                        op.Cancellation.Token.ThrowIfCancellationRequested();
                        EnsureNoTrash();
                        if (_saveActive) throw new RpcError(-32010, "JOB_BUSY", "A save is active; discover inputs again after it finishes.");
                        var currentJobs = DiscoveryQueueIndex();
                        if (skipped.Any(item => ExistingDiscoveryJob(item, currentJobs) is null))
                            throw new RpcError(-32010, "SOURCE_CHANGED", "An existing queue input changed during confirmation; discover inputs again.");
                        var admitted = new List<Entry>();
                        var admittedIdentities = new HashSet<MacFileIdentity>();
                        foreach (var (item, entry) in staged)
                        {
                            // Also guard aliasing within the selected batch, without changing
                            // the separate Core policy used to authorize destructive saves.
                            if (item.Stamps!.Any(stamp => admittedIdentities.Contains(stamp.Identity)))
                                continue;
                            foreach (var stamp in item.Stamps!) admittedIdentities.Add(stamp.Identity);
                            admitted.Add(entry);
                        }
                        EnsureSnapshotCapacity(additionalBytes: admitted.Sum(entry => (long)JsonSerializer.SerializeToUtf8Bytes(Snapshot(entry), JsonDefaults.Options).Length));
                        foreach (var entry in admitted) _jobs.Add(entry.Id, entry);
                        if (admitted.Count > 0) QueueChanged();
                        foreach (var (_, entry) in staged.Where(x => !admitted.Contains(x.Entry))) entry.Job.Dispose();
                        committed = true;
                        return new { discoveryId = discovery.Id, importedJobIds = admitted.Select(e => e.Id).ToArray(), duplicateJobIds = duplicates.ToArray(),
                            warnings = discovery.Diagnostics.Select(x => $"{x.Path}: {x.Message}").ToArray(),
                            diagnostics = discovery.Diagnostics.Select(x => new { path = x.Path, message = x.Message, kind = x.Kind.ToString() }).ToArray(),
                            ignoredFileCount = discovery.IgnoredFileCount, skippedDirectoryCount = discovery.SkippedDirectoryCount };
                    }
                }
                finally
                {
                    if (!committed) foreach (var (_, entry) in staged) entry.Job.Dispose();
                    lock (_gate) { _importsActive--; _confirmedImportActive = false; }
                }
            }, beforeStart: _ => { ReleaseDiscovery(discovery, "consumed"); _importsActive++; _confirmedImportActive = true; });
        }
    }

    private object DiscardDiscovery(JsonElement p)
    {
        lock (_gate)
        {
            PruneDiscoveries();
            var id = Text(p, "discoveryId");
            if (_discoveries.TryGetValue(id, out var discovery)) ReleaseDiscovery(discovery, "discarded");
            // Idempotent close: an expired or consumed token can also be safely dismissed.
            return new { discarded = true };
        }
    }

    private void PruneDiscoveries()
    {
        foreach (var discovery in _discoveries.Values.Where(x => x.ExpiresAt <= _utcNow()).ToArray())
            ReleaseDiscovery(discovery, "expired");
    }

    private void ReleaseDiscovery(Discovery discovery, string state)
    {
        _discoveries.Remove(discovery.Id);
        // Retain only a small receipt in operation history, not every expired path list.
        if (_operations.TryGetValue(discovery.OperationId, out var op))
        {
            op.Result = new { discoveryId = discovery.Id, discoveryState = state, requiresRediscovery = true };
            op.RetainedBytes = JsonSerializer.SerializeToUtf8Bytes(OperationSnapshot(op), JsonDefaults.Options).LongLength;
        }
    }

    private static RpcError StaleDiscovery() => new(-32010, "DISCOVERY_STALE", "This discovery has expired or was already used. Discover inputs again.");

    private DiscoveryQueueLookup DiscoveryQueueIndex()
    {
        var paths = new Dictionary<string, Entry>(FileSystemPathSemantics.PathComparer);
        var identities = new Dictionary<MacFileIdentity, Entry>();
        foreach (var entry in _jobs.Values)
        foreach (var path in entry.Job.VideoPaths)
        {
            paths.TryAdd(path, entry);
            try
            {
                FileSystemPathSemantics.ValidateCanonicalPath(path);
                if (MacFileIdentity.TryRead(path, out var identity, out _)) identities.TryAdd(identity, entry);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return new(paths, identities);
    }

    private static Entry? ExistingDiscoveryJob(DiscoveryItem item, DiscoveryQueueLookup index)
    {
        var exact = item.FileSet.VideoPaths.Select(path => index.Paths.GetValueOrDefault(path)).FirstOrDefault(e => e is not null);
        if (exact is not null) return exact;
        foreach (var stamp in item.Stamps ?? [])
        {
            if (!index.Identities.TryGetValue(stamp.Identity, out var candidate)) continue;
            // An operation-local index is only a hint. External replacement must not
            // turn an old inode match into a reason to omit a now-distinct input.
            foreach (var path in candidate.Job.VideoPaths)
            {
                try
                {
                    FileSystemPathSemantics.ValidateCanonicalPath(path);
                    if (MacFileIdentity.TryRead(path, out var identity, out _) && identity == stamp.Identity) return candidate;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        return null;
    }

    private static DiscoveryStamp CaptureDiscoveryStamp(string path)
    {
        // Discovery already resolved deliberately chosen input links. Do not resolve a
        // registered canonical path again after an external ancestor/file replacement.
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        var before = new FileInfo(path);
        if (!before.Exists) throw new FileNotFoundException("Discovered video is unavailable.", path);
        if (!MacFileIdentity.TryRead(path, out var identity, out var error))
        {
            if (error is not null) throw error;
            throw new RpcError(-32010, "IDENTITY_UNAVAILABLE", "File identity cannot be verified on this platform.");
        }
        var stamp = new DiscoveryStamp(path, identity, before.Length, before.CreationTimeUtc.Ticks, before.LastWriteTimeUtc.Ticks);
        before.Refresh();
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        if (!before.Exists || before.Length != stamp.Length || before.CreationTimeUtc.Ticks != stamp.Created || before.LastWriteTimeUtc.Ticks != stamp.Modified)
            throw new IOException("Discovered video changed while its identity was being read.");
        return stamp;
    }

    private static void ValidateDiscoveredItem(DiscoveryItem item, CancellationToken token)
    {
        foreach (var expected in item.Stamps!)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (CaptureDiscoveryStamp(expected.Path) != expected)
                    throw new IOException("Discovered video identity or attributes changed.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or RpcError)
            {
                throw new RpcError(-32010, "SOURCE_CHANGED", "A selected video changed since discovery. Discover inputs again.", new { path = expected.Path });
            }
        }
    }

    private sealed record DiscoveryStamp(string Path, MacFileIdentity Identity, long Length, long Created, long Modified);
    private sealed record DiscoveryQueueLookup(Dictionary<string, Entry> Paths, Dictionary<MacFileIdentity, Entry> Identities);
    private sealed record DiscoveryItem(string Id, MovieFileSet FileSet, string MovieId, DiscoveryStamp[]? Stamps, string? UnavailableReason);
    private sealed record Discovery(string Id, string OperationId, DateTimeOffset ExpiresAt, DiscoveryItem[] Items,
        MovieFileDiscoveryDiagnostic[] Diagnostics, int IgnoredFileCount, int SkippedDirectoryCount);
}
