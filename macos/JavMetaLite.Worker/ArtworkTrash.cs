using System.Text.Json;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private TrashLease? _trashLease;
    private object? TrashSnapshot() => _trashLease is { } lease ? new
    { trashToken = lease.Token, phase = lease.Phase, paths = lease.Paths, affectedJobIds = lease.Entries.Select(e => e.Id).ToArray() } : null;

    private void EnsureNoTrash()
    {
        if (_recoveryActive) throw new RpcError(-32010, "JOB_BUSY", "Wait for transaction recovery verification to finish.");
        if (_trashLease is not null)
            throw new RpcError(-32010, "TRASH_PENDING", "Finish or cancel the local image Trash operation before changing this workspace.", TrashSnapshot());
    }

    private async Task<object> PrepareTrash(JsonElement p)
    {
        TrashLease lease;
        long revision;
        lock (_gate)
        {
            EnsureNoTrash();
            if (_importsActive > 0 || _saveActive || _jobs.Values.Any(e => e.Busy) || _operations.Values.Any(o => o.Status == "running"))
                throw new RpcError(-32010, "JOB_BUSY", "Wait for imports, searches and saves before moving an image to Trash.");
            var entry = GetJob(p); RequireRevision(entry, p);
            var source = ArtworkSources(entry).FirstOrDefault(a => a.ArtworkId == Text(p, "artworkId") && a.IsLocal)
                ?? throw new RpcError(-32602, "ARTWORK_NOT_LOCAL", "Only a registered local image can be moved to Trash.");
            FileSystemPathSemantics.ValidateCanonicalPath(source.Location);
            var path = CanonicalInput(source.Location);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 100L * 1024 * 1024)
                throw new RpcError(-32010, "ARTWORK_INVALID", "The registered image is missing or exceeds the local image limit.");
            var affected = _jobs.Values.Where(e => Inputs(e).Any(input => SameInput(input, path)) || ArtworkSources(e).Any(a => a.IsLocal && SameInput(a.Location, path))).ToArray();
            lease = new TrashLease([path], affected);
            _trashLease = lease;
            revision = entry.Job.ReviewRevision;
            foreach (var e in affected) { e.Busy = true; InvalidatePlans(e.Id); }
        }
        try
        {
            var stamps = new List<FileStamp>();
            foreach (var path in lease.Paths)
            {
                var stamp = await CaptureAsync(path, true, CancellationToken.None);
                if (!stamp.Exists) throw new RpcError(-32010, "SOURCE_CHANGED", "The registered image disappeared during Trash preflight.");
                await ArtworkLocationHelper.ReadLocalImageWithDimensionsAsync(path, _images);
                if (await CaptureAsync(path, true, CancellationToken.None) != stamp)
                    throw new RpcError(-32010, "SOURCE_CHANGED", "The registered image changed during Trash preflight.");
                stamps.Add(stamp);
            }
            lock (_gate)
            {
                lease.Stamps = stamps.ToArray(); lease.Phase = "prepared";
                return new { trashToken = lease.Token, paths = lease.Paths, reviewRevision = revision, affectedJobIds = lease.Entries.Select(e => e.Id).ToArray() };
            }
        }
        catch { lock (_gate) ReleaseTrash(lease); throw; }
    }

    private async Task<object> ValidateTrash(JsonElement p)
    {
        TrashLease lease;
        lock (_gate)
        {
            lease = GetTrash(p);
            if (lease.Phase != "prepared") throw new RpcError(-32010, "TRASH_PHASE", "Only a prepared Trash operation can be authorized once.");
            lease.Phase = "validating";
        }
        try
        {
            await CheckTrashUnchanged(lease);
            lock (_gate)
            {
                lease.Phase = "authorized";
                // No timeout: the native recycle callback must finish before completeTrash releases this reservation.
                return new { trashToken = lease.Token, paths = lease.Paths, authorized = true };
            }
        }
        catch { lock (_gate) lease.Phase = "prepared"; throw; }
    }

    private async Task<object> AbortTrash(JsonElement p)
    {
        TrashLease lease;
        lock (_gate)
        {
            lease = GetTrash(p);
            if (lease.Phase != "prepared") throw new RpcError(-32010, "TRASH_PHASE", "An authorized recycle must finish before completeTrash; it cannot be aborted or timed out.");
            lease.Phase = "validating";
        }
        try
        {
            await CheckTrashUnchanged(lease);
            lock (_gate) { ReleaseTrash(lease); return new { aborted = true }; }
        }
        catch { lock (_gate) lease.Phase = "prepared"; throw; }
    }

    private async Task CheckTrashUnchanged(TrashLease lease)
    {
        foreach (var stamp in lease.Stamps)
            if (!stamp.Exists || await CaptureAsync(stamp.Path, true, CancellationToken.None) != stamp)
                throw new RpcError(-32010, "SOURCE_CHANGED", "The registered image changed. Do not recycle it; complete the operation to refresh the workspace.");
        lock (_gate)
            if (lease.Entries.Any(e => !_jobs.ContainsKey(e.Id) || e.Job.ReviewRevision != lease.Revisions[e.Id]))
                throw new RpcError(-32010, "REVISION_CONFLICT", "A shared image review changed. Complete the operation to refresh the workspace.");
    }

    private async Task<object> CompleteTrash(JsonElement p)
    {
        TrashLease lease;
        lock (_gate)
        {
            lease = GetTrash(p);
            if (lease.Phase is not ("prepared" or "authorized")) throw new RpcError(-32010, "TRASH_PHASE", "Wait for Trash preflight or refresh to finish.");
            lease.Phase = "refreshing";
        }
        var warnings = new List<string>(); var removed = new List<string>();
        try
        {
            foreach (var stamp in lease.Stamps)
            {
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(stamp.Path)))
                        throw new DirectoryNotFoundException("The image parent folder or volume is unavailable; absence is not proof of a completed recycle.");
                    var now = await CaptureAsync(stamp.Path, true, CancellationToken.None);
                    if (!now.Exists) removed.Add(stamp.Path);
                    else if (now != stamp) warnings.Add("The image path now contains changed data; it was refreshed without deleting it: " + stamp.Path);
                    else warnings.Add("The original image is still present; it was not moved to Trash: " + stamp.Path);
                }
                catch (Exception error) { warnings.Add("The image outcome could not be verified: " + stamp.Path + "; " + error.Message); }
            }
            foreach (var entry in lease.Entries)
            {
                LocalArtworkDiscoveryResult? found = null;
                try
                {
                    var set = MovieFileSet.Create(entry.Job.VideoPaths);
                    var sidecars = LocalSidecarLocator.Locate(set.PrimaryPath, set.MovieBaseName, set.UsesMultipartNaming);
                    found = await LocalArtworkDiscovery.DiscoverAsync(sidecars, _images);
                    warnings.AddRange(found.Diagnostics);
                }
                catch (Exception error) { warnings.Add("Local artwork could not be safely reloaded for " + entry.Id + ": " + error.Message); }
                lock (_gate)
                {
                    var previousSource = entry.Job.ArtworkReview.SelectedCandidate?.Source.Name;
                    var selectedSamples = entry.Job.Metadata.ScreenshotUrls.ToArray();
                    var previousLocal = ArtworkSources(entry).Where(a => a.IsLocal).Select(a => a.Location).ToArray();
                    entry.Job.RemoveLocalArtworkLocations(previousLocal);
                    entry.Job.SetLocalArtwork(found?.Candidate);
                    entry.Job.SetLocalExtrafanart(found?.ExtrafanartPaths ?? []);
                    if (previousSource is not null) entry.Job.SelectArtworkSource(previousSource);
                    var validLocal = (found?.ExtrafanartPaths ?? []).ToHashSet(FileSystemPathSemantics.PathComparer);
                    entry.Job.Metadata.ScreenshotUrls = selectedSamples.Where(s => ArtworkLocationHelper.TryGetRemoteUri(s, out _) || validLocal.Contains(s)).ToArray();
                    InvalidatePlans(entry.Id);
                }
            }
            lock (_gate)
            {
                foreach (var entry in lease.Entries) entry.Warnings.AddRange(warnings);
                ReleaseTrash(lease);
                return new { jobs = lease.Entries.Select(Snapshot).ToArray(), removedPaths = removed.ToArray(), warnings = warnings.ToArray() };
            }
        }
        catch
        {
            // Native recycle has finished, but leave an explicit recoverable lease when refresh fails.
            lock (_gate) lease.Phase = "prepared";
            throw;
        }
    }

    private TrashLease GetTrash(JsonElement p) => _trashLease is { } lease && lease.Token == Text(p, "trashToken")
        ? lease : throw new RpcError(-32602, "TRASH_NOT_FOUND", "This Trash token is not active in the current worker session.");
    private void ReleaseTrash(TrashLease lease)
    {
        foreach (var entry in lease.Entries) entry.Busy = false;
        if (ReferenceEquals(_trashLease, lease)) _trashLease = null;
    }
    private sealed class TrashLease(string[] paths, Entry[] entries)
    {
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public string[] Paths { get; } = paths;
        public Entry[] Entries { get; } = entries;
        public Dictionary<string, long> Revisions { get; } = entries.ToDictionary(e => e.Id, e => e.Job.ReviewRevision);
        public FileStamp[] Stamps = [];
        public string Phase = "preparing";
    }
}
