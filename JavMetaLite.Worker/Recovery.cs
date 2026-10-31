using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private FileStream? _recoveryOwnership;
    private bool _recoveryActive;
    private readonly Dictionary<string, string> _listedRecoveryRecords = [];
    private readonly Dictionary<string, RecoveryVerification> _recoveryVerifications = [];
    private const int MaximumRecoveryRecordBytes = 2 * 1024 * 1024;

    private void InitializeRecovery()
    {
        ValidateRecoveryDirectory(_recoveryRoot);
        var owners = Path.Combine(_recoveryRoot, "owners");
        Directory.CreateDirectory(owners);
        ValidateRecoveryDirectory(owners);
        _recoveryOwnership = OpenRecoveryOwnership(Path.Combine(owners, _sessionId + ".lock"), true)
            ?? throw new IOException("Recovery ownership could not be established.");
        RefreshRecoveryWarnings();
    }

    private static void ValidateRecoveryDirectory(string path)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Recovery directory must be an existing, unlinked directory.");
    }

    private IEnumerable<string> RecoveryPaths()
    {
        ValidateRecoveryDirectory(_recoveryRoot);
        return Directory.EnumerateFiles(_recoveryRoot, "*.json").Order(StringComparer.Ordinal);
    }

    private static byte[] ReadRecoveryBytes(string path)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > MaximumRecoveryRecordBytes)
            throw new IOException("Recovery record is missing, linked, or too large to inspect safely.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        var block = new byte[8192]; int count;
        while ((count = stream.Read(block)) != 0)
        {
            if (buffer.Length + count > MaximumRecoveryRecordBytes) throw new IOException("Recovery record exceeded the inspection limit.");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private void RefreshRecoveryWarnings()
    {
        // Missing records previously observed as unresolved remain warnings: deleting a
        // journal outside this protocol must not silently authorize more media writes.
        foreach (var path in RecoveryPaths())
        {
            try
            {
                using var document = JsonDocument.Parse(ReadRecoveryBytes(path));
                if (document.RootElement.GetProperty("status").GetString() is "completed" or "failedRestored" or "canceledRestored") continue;
            }
            catch { }
            if (!_recoveryWarnings.Contains(path, StringComparer.Ordinal)) _recoveryWarnings.Add(path);
        }
    }

    private object ListRecovery(JsonElement parameters)
    {
        lock (_gate)
        {
            RefreshRecoveryWarnings();
            var offset = parameters.TryGetProperty("offset", out var value) && value.TryGetInt32(out var requested) ? requested : 0;
            if (offset < 0) throw new RpcError(-32602, "INVALID_PARAMS", "Recovery offset must be nonnegative.");
            var paths = RecoveryPaths().Concat(_recoveryWarnings).Distinct(StringComparer.Ordinal)
                .OrderByDescending(path => _recoveryWarnings.Contains(path, StringComparer.Ordinal)).ThenBy(path => path, StringComparer.Ordinal).ToArray();
            _listedRecoveryRecords.Clear();
            var rows = paths.Skip(offset).Take(100).Select(path =>
            {
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(path))));
                _listedRecoveryRecords[id] = path;
                string status = "unknown", phase = "", reason = "", startedAt = "";
                bool supported = false;
                try
                {
                    using var document = JsonDocument.Parse(ReadRecoveryBytes(path)); var record = document.RootElement;
                    status = Text(record, "status"); phase = record.TryGetProperty("phase", out var p) ? p.GetString() ?? "" : "";
                    startedAt = record.TryGetProperty("startedAt", out var date) ? date.GetString() ?? "" : "";
                    supported = record.TryGetProperty("recoveryVersion", out var v) && v.TryGetInt32(out var version) && version == 1;
                    if (!supported) reason = "legacyRecord";
                }
                catch { reason = "unreadableRecord"; }
                return new { recordId = id, recordPath = path, status, phase, startedAt, supported, reason,
                    blocksSaving = _recoveryWarnings.Contains(path, StringComparer.Ordinal) };
            }).ToArray();
            return new { records = rows, total = paths.Length, nextOffset = offset + rows.Length < paths.Length ? (int?)(offset + rows.Length) : null,
                recoveryRoot = _recoveryRoot };
        }
    }

    private void EnsureRecoveryIdle()
    {
        EnsureNoTrash();
        if (_recoveryActive || _saveActive || _importsActive > 0 || _discoveryActive || _jobs.Values.Any(entry => entry.Busy) ||
            _operations.Values.Any(operation => operation.Status == "running" || !operation.Task.IsCompleted))
            throw new RpcError(-32010, "JOB_BUSY", "Wait for all operations before inspecting transaction recovery.");
    }

    private object VerifyRecovery(JsonElement parameters)
    {
        lock (_gate)
        {
            EnsureRecoveryIdle();
            if (!_listedRecoveryRecords.TryGetValue(Text(parameters, "recordId"), out var path))
                throw new RpcError(-32602, "RECOVERY_NOT_FOUND", "Refresh the recovery list before choosing a record.");
            _recoveryActive = true;
            return StartOperation(null, async operation =>
            {
                try
                {
                    var inspection = await InspectRecovery(path, operation.Cancellation.Token);
                    string? token = null;
                    if (inspection.Outcome is not null)
                    {
                        lock (_gate)
                        {
                            foreach (var expired in _recoveryVerifications.Where(pair => _utcNow() - pair.Value.CreatedAt > TimeSpan.FromMinutes(10)).Select(pair => pair.Key).ToArray())
                                _recoveryVerifications.Remove(expired);
                            if (_recoveryVerifications.Count >= 32) _recoveryVerifications.Clear();
                            token = Guid.NewGuid().ToString("N");
                            _recoveryVerifications[token] = new(path, inspection, _utcNow());
                        }
                    }
                    return new { recordPath = path, outcome = inspection.Outcome, canArchive = token is not null, verificationToken = token,
                        files = inspection.Files, issues = inspection.Issues, expiresAfterSeconds = 600,
                        mediaChanged = false, requiresReimport = token is not null };
                }
                finally { lock (_gate) _recoveryActive = false; }
            }); // Holds a workspace reservation; reconciliation must see it as blocking.
        }
    }

    private object ArchiveRecovery(JsonElement parameters)
    {
        lock (_gate)
        {
            EnsureRecoveryIdle();
            if (!Bool(parameters, "confirmed", false)) throw new RpcError(-32602, "CONFIRMATION_REQUIRED", "Confirm the verified file outcome before archiving this record.");
            var token = Text(parameters, "verificationToken");
            if (!_recoveryVerifications.Remove(token, out var verification) || _utcNow() - verification.CreatedAt > TimeSpan.FromMinutes(10))
                throw new RpcError(-32010, "RECOVERY_STALE", "The recovery verification expired. Verify the files again.");
            _recoveryActive = true;
            return StartOperation(null, async operation =>
            {
                try
                {
                    var current = await InspectRecovery(verification.Path, operation.Cancellation.Token, holdOwnership: true);
                    using var ownership = current.Ownership;
                    if (current.Outcome is null || current.RecordDigest != verification.Inspection.RecordDigest || current.Outcome != verification.Inspection.Outcome ||
                        !current.Stamps.SequenceEqual(verification.Inspection.Stamps))
                        throw new RpcError(-32010, "RECOVERY_STALE", "The record or files changed since verification. Inspect them again.");
                    var archive = Path.Combine(_recoveryRoot, "archived");
                    ValidateRecoveryDirectory(_recoveryRoot);
                    Directory.CreateDirectory(archive); ValidateRecoveryDirectory(archive);
                    var destination = Path.Combine(archive, Path.GetFileName(verification.Path));
                    FileSystemPathSemantics.ValidateCanonicalPath(destination);
                    if (File.Exists(destination) || Directory.Exists(destination) || new FileInfo(destination).LinkTarget is not null)
                        throw new RpcError(-32010, "RECOVERY_ARCHIVE_CONFLICT", "An archive already occupies this record's destination.");
                    // The owner lock is held across this final record check and rename.
                    if (Convert.ToHexString(SHA256.HashData(ReadRecoveryBytes(verification.Path))) != current.RecordDigest)
                        throw new RpcError(-32010, "RECOVERY_STALE", "The recovery record changed during verification.");
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    File.Move(verification.Path, destination, false);
                    try
                    {
                        ValidateRecoveryDirectory(archive);
                        if (Convert.ToHexString(SHA256.HashData(ReadRecoveryBytes(destination))) != current.RecordDigest)
                            throw new IOException("The archived record does not match the verified record.");
                    }
                    catch
                    {
                        // An external writer is not governed by our ownership lock.
                        // Preserve its replacement and keep the save barrier; restore
                        // only when no newer record occupies the original location.
                        try { File.Move(destination, verification.Path, false); } catch { }
                        lock (_gate)
                            if (!_recoveryWarnings.Contains(verification.Path, StringComparer.Ordinal)) _recoveryWarnings.Add(verification.Path);
                        throw new RpcError(-32010, "RECOVERY_STALE", "The record changed while being archived. Both locations require inspection; saving remains blocked.");
                    }
                    string[] affectedJobIds;
                    lock (_gate)
                    {
                        _recoveryWarnings.RemoveAll(path => path == verification.Path);
                        _listedRecoveryRecords.Clear(); _recoveryVerifications.Clear(); QueueChanged();
                        // Imported baselines may predate the crash or manual repair. Require
                        // explicit remove/reimport; never treat an old editor draft as saved.
                        var directories = current.Stamps.Select(stamp => Path.GetDirectoryName(stamp.Path)!).ToHashSet(FileSystemPathSemantics.PathComparer);
                        var affected = _jobs.Values.Where(entry => entry.Job.VideoPaths.Any(video => directories.Contains(Path.GetDirectoryName(video)!)) ||
                            entry.Job.LocalMetadataBundle?.Sidecars.NfoPath is { } nfo && directories.Contains(Path.GetDirectoryName(nfo)!)).ToArray();
                        affectedJobIds = affected.Select(entry => entry.Id).ToArray();
                        foreach (var entry in affected) entry.Job.BeginLocalNfoRead();
                        RefreshRecoveryWarnings();
                    }
                    return new { archived = true, archivedPath = destination, outcome = current.Outcome, mediaChanged = false, requiresReimport = true, affectedJobIds };
                }
                finally { lock (_gate) _recoveryActive = false; }
            });
        }
    }

    private async Task<RecoveryInspection> InspectRecovery(string path, CancellationToken cancellation, bool holdOwnership = false)
    {
        FileStream? owner = null;
        try
        {
            var bytes = ReadRecoveryBytes(path);
            var digest = Convert.ToHexString(SHA256.HashData(bytes));
            using var document = JsonDocument.Parse(bytes); var record = document.RootElement;
            if (!record.TryGetProperty("recoveryVersion", out var version) || !version.TryGetInt32(out var number) || number != 1)
            {
                // Older records remain useful references, but existence alone cannot
                // prove rollback or commit. Never mint an archive token for them.
                var legacyPaths = new List<string>();
                foreach (var key in new[] { "sourcePaths", "temporaryPaths" })
                    if (record.TryGetProperty(key, out var paths) && paths.ValueKind == JsonValueKind.Array)
                        legacyPaths.AddRange(paths.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!));
                if (record.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                    foreach (var change in changes.EnumerateArray())
                        foreach (var key in new[] { "sourcePath", "destinationPath" })
                            if (change.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String) legacyPaths.Add(value.GetString()!);
                return new(digest, null, [], legacyPaths.Distinct(StringComparer.Ordinal).Take(4096).Select(file => new RecoveryFile(file, "legacyUnverified", null)).ToArray(), ["legacyRecord"]);
            }
            var session = Text(record, "sessionId");
            if (!Guid.TryParseExact(session, "N", out _)) return new(digest, null, [], [], ["invalidRecord"]);
            var ownerPath = Path.Combine(_recoveryRoot, "owners", session + ".lock");
            ValidateRecoveryDirectory(Path.GetDirectoryName(ownerPath)!);
            if (session != _sessionId)
            {
                owner = OpenRecoveryOwnership(ownerPath, false);
                if (owner is null) return new(digest, null, [], [], ["ownerActiveOrUnverifiable"]);
            }
            var before = record.GetProperty("beforeFiles").Deserialize<FileStamp[]>(JsonDefaults.Options) ?? [];
            var after = record.TryGetProperty("afterFiles", out var afterValue) ? afterValue.Deserialize<FileStamp[]>(JsonDefaults.Options) ?? [] : [];
            var temporary = record.GetProperty("temporaryPaths").Deserialize<string[]>(JsonDefaults.Options) ?? [];
            var roots = record.TryGetProperty("recoveryRoots", out var rootValue) ? rootValue.Deserialize<string[]>(JsonDefaults.Options) ?? [] : [];
            if (before.Length == 0 || before.Length > 4096 || after.Length > 4096 || temporary.Length > 4096 ||
                before.Concat(after).Any(stamp => !Path.IsPathFullyQualified(stamp.Path) || stamp.Exists && (stamp.Hash is null || stamp.Hash.Length != 64)))
                return new(digest, null, [], [], ["invalidRecord"]);
            var actual = new List<FileStamp>(); var files = new List<RecoveryFile>(); var issues = new List<string>();
            foreach (var file in before.Concat(after).Select(stamp => stamp.Path).Distinct(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var stamp = await CaptureAsync(file, true, cancellation);
                    if (!stamp.Exists) ConfirmRecoveryAbsent(file, roots);
                    actual.Add(stamp);
                    files.Add(new(file, stamp.Exists ? "present" : "missing", stamp.Hash));
                }
                catch (OperationCanceledException) { throw; }
                catch { issues.Add("fileUnverifiable"); files.Add(new(file, "unverifiable", null)); }
            }
            foreach (var temporaryPath in temporary.Distinct(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (!Path.IsPathFullyQualified(temporaryPath)) throw new IOException();
                    FileSystemPathSemantics.ValidateCanonicalPath(temporaryPath);
                    var exists = File.Exists(temporaryPath) || Directory.Exists(temporaryPath) || new FileInfo(temporaryPath).LinkTarget is not null;
                    if (!exists) ConfirmRecoveryAbsent(temporaryPath, roots);
                    files.Add(new(temporaryPath, exists ? "temporaryPresent" : "temporaryAbsent", null));
                    if (exists) issues.Add("temporaryRemains");
                }
                catch { issues.Add("temporaryUnverifiable"); files.Add(new(temporaryPath, "unverifiable", null)); }
            }
            bool Matches(FileStamp[] expected) => expected.Length > 0 && expected.All(wanted => actual.Any(found => found.Path == wanted.Path && found.Exists == wanted.Exists &&
                (!wanted.Exists || found.Length == wanted.Length && string.Equals(found.Hash, wanted.Hash, StringComparison.OrdinalIgnoreCase))));
            var status = Text(record, "status");
            var known = status is "completed" or "cleanupRequired" or "recoveryRequired" or "inProgress" or "failedRestored" or "canceledRestored";
            string? outcome = null;
            if (!known) issues.Add("unknownOutcome");
            else if (issues.Count == 0)
            {
                if (Matches(after)) outcome = "committed";
                else if (Matches(before)) outcome = "restored";
                else issues.Add("filesDoNotMatch");
            }
            if (Convert.ToHexString(SHA256.HashData(ReadRecoveryBytes(path))) != digest) { outcome = null; issues.Add("recordChanged"); }
            foreach (var stamp in actual)
            {
                var current = await CaptureAsync(stamp.Path, false, cancellation);
                if (!current.Exists) ConfirmRecoveryAbsent(stamp.Path, roots);
                if (current.Exists != stamp.Exists || current.Length != stamp.Length || current.WriteTicks != stamp.WriteTicks)
                { outcome = null; issues.Add("filesChangedDuringVerification"); }
            }
            var result = new RecoveryInspection(digest, outcome, actual.ToArray(), files.ToArray(), issues.Distinct().ToArray(), holdOwnership ? owner : null);
            if (holdOwnership) owner = null;
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch { return new("", null, [], [], ["unreadableRecord"]); }
        finally { owner?.Dispose(); }
    }

    private static void ConfirmRecoveryAbsent(string path, string[] roots)
    {
        // Source extrafanart / newly-created target directories may legitimately be
        // removed by commit/rollback. Climb only within an original selected root,
        // never above a now-unavailable media volume or custom destination root.
        var direct = Path.GetDirectoryName(path) ?? throw new IOException("No recovery parent directory.");
        var anchor = roots.Where(root => Path.IsPathFullyQualified(root) &&
            Path.GetRelativePath(root, path) is { } relative && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            .OrderByDescending(root => root.Length).FirstOrDefault() ?? direct;
        ValidateRecoveryDirectory(anchor);
        var child = path;
        while (Path.GetDirectoryName(child) is { } parent)
        {
            string[] entries;
            try { FileSystemPathSemantics.ValidateCanonicalPath(parent); entries = Directory.GetFileSystemEntries(parent); }
            catch (DirectoryNotFoundException) when (!FileSystemPathSemantics.PathsEqual(parent, anchor)) { child = parent; continue; }
            if (entries.Any(entry => string.Equals(entry, child, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("The recovery path still exists or cannot be distinguished safely.");
            return;
        }
        throw new IOException("The original recovery root is unavailable.");
    }

    private static FileStream? OpenRecoveryOwnership(string path, bool create)
    {
        FileStream? stream = null;
        try
        {
            FileSystemPathSemantics.ValidateCanonicalPath(path);
            if (new FileInfo(path).LinkTarget is not null) return null;
            stream = new FileStream(path, create ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite,
                OperatingSystem.IsWindows() ? FileShare.None : FileShare.ReadWrite);
            if (OperatingSystem.IsMacOS() ? RecoveryFlockMac(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0 :
                OperatingSystem.IsLinux() ? RecoveryFlockLinux(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0 : !OperatingSystem.IsWindows())
            { stream.Dispose(); return null; }
            return stream;
        }
        catch { stream?.Dispose(); return null; }
    }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)] private static extern int RecoveryFlockMac(int fd, int operation);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int RecoveryFlockLinux(int fd, int operation);
    private sealed record RecoveryFile(string Path, string State, string? Sha256);
    private sealed record RecoveryInspection(string RecordDigest, string? Outcome, FileStamp[] Stamps, RecoveryFile[] Files, string[] Issues, FileStream? Ownership = null);
    private sealed record RecoveryVerification(string Path, RecoveryInspection Inspection, DateTimeOffset CreatedAt);
}
