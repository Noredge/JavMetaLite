using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed record ArtworkDiskCacheLimits(long MaximumBytes = 256L * 1024 * 1024,
    int MaximumFiles = 1024, int MaximumLeases = 4096);

// Generated artwork only. Explicit client leases protect URL readers independently
// of jobs and operation receipts; an in-use file is never a pressure-eviction victim.
internal sealed class ArtworkDiskCache : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _publication = new(1, 1);
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedFile> _files = new(StringComparer.Ordinal);
    private readonly ArtworkDiskCacheLimits _limits;
    private readonly string _root;
    private readonly FileStream _ownership;
    private long _clock, _bytes, _pendingBytes;
    private int _pendingFiles;
    private int _removedSessions, _activeSessions, _unownedSessions, _unsafeSessions, _cleanupFailures;
    private bool _disposed;
    private const string OwnerName = ".owner.json", LockName = ".owner.lock";
    private static readonly Regex PayloadName = new("^[0-9a-f]{64}\\.(png|jpg)(\\.[0-9a-f]{32}\\.tmp)?$", RegexOptions.CultureInvariant);

    public ArtworkDiskCache(string root, ArtworkDiskCacheLimits? limits = null)
    {
        _root = root;
        _limits = limits ?? new();
        if (_limits.MaximumBytes <= 0 || _limits.MaximumFiles <= 0 || _limits.MaximumLeases <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
        Directory.CreateDirectory(root);
        if (IsLink(new DirectoryInfo(root))) throw new IOException("Artwork cache directory must not be a link.");
        _ownership = OpenOwnership(Path.Combine(root, LockName), create: true)
            ?? throw new IOException("Artwork cache ownership could not be established.");
        try
        {
            File.WriteAllText(Path.Combine(root, OwnerName), JsonSerializer.Serialize(new
            { application = "JavMetaLite", kind = "artwork-cache", schemaVersion = 1, sessionId = Path.GetFileName(root) }));
            ReclaimInactiveSessions();
        }
        catch { _ownership.Dispose(); throw; }
    }

    public Lease Reserve(string jobId, string? requestedId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (requestedId is not null && (requestedId.Length is < 1 or > 128 || requestedId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))))
                throw new RpcError(-32602, "INVALID_PARAMS", "Artwork lease IDs must contain 1 to 128 ASCII letters, digits, hyphens or underscores.");
            var id = requestedId ?? Guid.NewGuid().ToString("N");
            if (_leases.ContainsKey(id)) throw new RpcError(-32602, "INVALID_PARAMS", "Artwork lease ID is already in use.");
            if (_leases.Count >= _limits.MaximumLeases) throw Capacity();
            var lease = new Lease(id, jobId, requestedId is not null);
            _leases.Add(id, lease);
            return lease;
        }
    }

    public void Release(string id)
    {
        lock (_gate)
        {
            if (!_leases.TryGetValue(id, out var lease)) return;
            Revoke(lease);
        }
    }

    private void Revoke(Lease lease)
    {
        lease.Revoked = true;
        if (lease.Loading) lease.Cancellation.Cancel();
        if (lease.File is { } file)
        {
            file.Leases.Remove(lease.Id);
            lease.File = null;
            DeleteIfOrphaned(file);
        }
        // A loading action keeps its reservation object, even if this mapping is
        // released. Its revoked flag prevents late publication or reattachment.
        if (_leases.GetValueOrDefault(lease.Id) == lease) _leases.Remove(lease.Id);
    }

    public void FinishLoad(Lease lease, bool succeeded)
    {
        lock (_gate)
        {
            lease.Loading = false;
            if (!succeeded || lease.Revoked || !lease.Explicit) Revoke(lease);
            lease.Cancellation.Dispose();
        }
    }

    public void RemoveJob(string jobId)
    {
        lock (_gate)
        {
            foreach (var lease in _leases.Values.Where(l => l.JobId == jobId).ToArray())
            {
                lease.JobRemoved = true;
                if (!lease.Explicit) Revoke(lease);
            }
            foreach (var file in _files.Values.ToArray())
            {
                file.Jobs.Remove(jobId); file.LegacyJobs.Remove(jobId);
                DeleteIfOrphaned(file);
            }
        }
    }

    public async Task<(string CacheId, string Path)> PublishAsync(byte[] bytes, bool png, Lease lease, CancellationToken token)
    {
        await _publication.WaitAsync(token);
        string? temporary = null;
        try
        {
            var key = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var path = Path.Combine(_root, key + (png ? ".png" : ".jpg"));
            lock (_gate)
            {
                CheckLease(lease, token);
                FileSystemPathSemantics.ValidateCanonicalPath(path);
                if (_files.TryGetValue(path, out var existing))
                {
                    var info = new FileInfo(path);
                    if (IsLink(info) || Directory.Exists(path))
                        throw new RpcError(-32010, "ARTWORK_INVALID", "Generated artwork cache path was replaced.");
                    if (info.Exists)
                    {
                        if (info.Length != existing.Bytes || info.LastWriteTimeUtc.Ticks != existing.ModifiedTicks)
                            throw new RpcError(-32010, "ARTWORK_INVALID", "Generated artwork cache file was replaced.");
                        Attach(existing, lease);
                        return (key, path);
                    }
                    // System cache cleanup can remove a generated payload while
                    // readers still hold its URL. Preserve the entry/old leases,
                    // release only its missing disk charge, then regenerate it.
                    _bytes -= existing.Bytes;
                    existing.Bytes = 0;
                }
                EnsureRoom(bytes.LongLength);
                _pendingBytes = bytes.LongLength; _pendingFiles = 1;
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            }
            await File.WriteAllBytesAsync(temporary, bytes, token);
            lock (_gate)
            {
                CheckLease(lease, token);
                FileSystemPathSemantics.ValidateCanonicalPath(path);
                File.Move(temporary, path, overwrite: false);
                temporary = null;
                var file = _files.GetValueOrDefault(path) ?? new CachedFile(path, 0);
                file.Bytes = bytes.LongLength;
                file.ModifiedTicks = File.GetLastWriteTimeUtc(path).Ticks;
                _files[path] = file; _bytes += file.Bytes;
                _pendingBytes = 0; _pendingFiles = 0;
                Attach(file, lease);
            }
            return (key, path);
        }
        finally
        {
            lock (_gate)
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { TrackFailedTemporary(temporary); }
                    catch (UnauthorizedAccessException) { TrackFailedTemporary(temporary); }
                }
                _pendingBytes = 0; _pendingFiles = 0;
            }
            _publication.Release();
        }
    }

    private void TrackFailedTemporary(string path)
    {
        // Failed cleanup must remain in the actual disk budget, never disappear
        // from accounting so another write can silently exceed the limit.
        var file = new CachedFile(path, _pendingBytes) { Access = ++_clock };
        _files.TryAdd(path, file); _bytes += file.Bytes;
    }

    private void CheckLease(Lease lease, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (lease.Revoked) throw new OperationCanceledException("Artwork preview lease was released.");
    }

    private void Attach(CachedFile file, Lease lease)
    {
        if (!lease.JobRemoved) file.Jobs.Add(lease.JobId);
        if (lease.Explicit) { file.Leases.Add(lease.Id); lease.File = file; }
        else if (!lease.JobRemoved) file.LegacyJobs.Add(lease.JobId);
        file.Access = ++_clock;
    }

    private void EnsureRoom(long bytes)
    {
        if (bytes > _limits.MaximumBytes) throw Capacity();
        foreach (var file in _files.Values.Where(f => !f.Protected).OrderBy(f => f.Access).ToArray())
        {
            if (_bytes + bytes <= _limits.MaximumBytes && PayloadFiles < _limits.MaximumFiles) return;
            TryDelete(file);
        }
        if (_bytes + bytes > _limits.MaximumBytes || PayloadFiles >= _limits.MaximumFiles) throw Capacity();
    }

    private static RpcError Capacity() => new(-32010, "ARTWORK_CACHE_CAPACITY",
        "Artwork cache is full of protected previews. Remove unused movies from the queue and try again.");
    private int PayloadFiles => _files.Values.Count(file => file.Bytes > 0);
    private void DeleteIfOrphaned(CachedFile file)
    {
        if (!file.Protected && file.Jobs.Count == 0) TryDelete(file);
    }
    private void TryDelete(CachedFile file)
    {
        try
        {
            FileSystemPathSemantics.ValidateCanonicalPath(file.Path);
            if (IsLink(new FileInfo(file.Path))) return;
            File.Delete(file.Path);
            if (_files.Remove(file.Path)) _bytes -= file.Bytes;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public object Snapshot()
    {
        lock (_gate) return new
        {
            limits = new { maximumBytes = _limits.MaximumBytes, maximumFiles = _limits.MaximumFiles, maximumLeases = _limits.MaximumLeases },
            bytes = _bytes + _pendingBytes, files = PayloadFiles + _pendingFiles,
            leases = _leases.Count, protectedFiles = _files.Values.Count(f => f.Bytes > 0 && f.Protected),
            pendingBytes = _pendingBytes, pendingFiles = _pendingFiles,
            startupCleanup = new { removedSessions = _removedSessions, activeSessions = _activeSessions,
                unownedSessions = _unownedSessions, unsafeSessions = _unsafeSessions, failures = _cleanupFailures }
        };
    }

    private void ReclaimInactiveSessions()
    {
        foreach (var directory in Directory.EnumerateDirectories(Path.GetDirectoryName(_root)!))
        {
            if (directory == _root) continue;
            try
            {
                if (IsLink(new DirectoryInfo(directory))) { _unsafeSessions++; continue; }
                if (!HasOwner(directory)) { _unownedSessions++; continue; }
                using var ownership = OpenOwnership(Path.Combine(directory, LockName), create: false);
                if (ownership is null) { _activeSessions++; continue; }
                // Ownership is held across validation and deletion. An old version
                // with no recognized marker is deliberately never reclaimed.
                if (DeleteOwnedSession(directory)) _removedSessions++;
                else _unsafeSessions++;
            }
            catch (IOException) { _cleanupFailures++; }
            catch (UnauthorizedAccessException) { _cleanupFailures++; }
            catch (JsonException) { _unsafeSessions++; }
        }
    }

    private static bool HasOwner(string directory)
    {
        var name = Path.GetFileName(directory);
        if (!Guid.TryParseExact(name, "N", out _)) return false;
        var marker = new FileInfo(Path.Combine(directory, OwnerName));
        var lockFile = new FileInfo(Path.Combine(directory, LockName));
        if (!marker.Exists || marker.Length > 2048 || IsLink(marker) || !lockFile.Exists || IsLink(lockFile)) return false;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(marker.FullName));
        var value = doc.RootElement;
        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("application", out var app) && app.ValueKind == JsonValueKind.String && app.GetString() == "JavMetaLite" &&
            value.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "artwork-cache" &&
            value.TryGetProperty("schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var version) && version == 1 &&
            value.TryGetProperty("sessionId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == name;
    }

    private static bool DeleteOwnedSession(string directory)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(directory);
        if (IsLink(new DirectoryInfo(directory)) || !HasOwner(directory)) return false;
        var entries = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToArray();
        if (entries.Any(e => IsLink(e) || e is DirectoryInfo || e.Name != OwnerName && e.Name != LockName && !PayloadName.IsMatch(e.Name))) return false;
        foreach (var entry in entries.Where(e => e.Name != LockName)) File.Delete(entry.FullName);
        File.Delete(Path.Combine(directory, LockName));
        Directory.Delete(directory, recursive: false);
        return true;
    }

    private static bool IsLink(FileSystemInfo info) => info.LinkTarget is not null ||
        (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0);

    private static FileStream? OpenOwnership(string path, bool create)
    {
        FileStream? stream = null;
        try
        {
            if (IsLink(new FileInfo(path))) return null;
            stream = new FileStream(path, create ? FileMode.CreateNew : FileMode.Open,
                FileAccess.ReadWrite, OperatingSystem.IsWindows() ? FileShare.None : FileShare.ReadWrite | FileShare.Delete);
            if (OperatingSystem.IsMacOS() ? FlockMac(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0
                : OperatingSystem.IsLinux() ? FlockLinux(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0
                : !OperatingSystem.IsWindows())
            { stream.Dispose(); return null; }
            return stream;
        }
        catch (IOException) { stream?.Dispose(); return null; }
        catch (UnauthorizedAccessException) { stream?.Dispose(); return null; }
    }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)]
    private static extern int FlockMac(int descriptor, int operation);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int FlockLinux(int descriptor, int operation);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var lease in _leases.Values.ToArray()) Revoke(lease);
            try { DeleteOwnedSession(_root); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            finally { _ownership.Dispose(); }
        }
        _publication.Dispose();
    }

    internal sealed class Lease(string id, string jobId, bool explicitLease)
    {
        public string Id { get; } = id;
        public string JobId { get; } = jobId;
        public bool Explicit { get; } = explicitLease;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Loading = true, Revoked, JobRemoved;
        internal CachedFile? File;
    }
    internal sealed class CachedFile(string path, long bytes)
    {
        public string Path { get; } = path;
        public long Bytes = bytes;
        public long ModifiedTicks;
        public long Access;
        public HashSet<string> Jobs { get; } = [];
        public HashSet<string> LegacyJobs { get; } = [];
        public HashSet<string> Leases { get; } = [];
        public bool Protected => Leases.Count > 0 || LegacyJobs.Count > 0;
    }
}
