using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

// Evidence is captured while the file still lives in transaction staging, before
// publishing its name. A rollback must not delete whatever later occupies that
// public name. Quarantine first, then verify the actual file that was moved.
internal sealed record TransactionFileOwnership(
    string Path, string StagingRoot, long Length, DateTime LastWriteTimeUtc,
    string? Sha256, MacFileIdentity? Identity, WindowsTransactionFileIdentity? WindowsIdentity)
{
    private string? _retiredPath;
    private string? _redundantSource;
    internal CommittedFileFingerprint? Fingerprint => Sha256 is { } hash
        ? new(Path, Length, LastWriteTimeUtc.Ticks, hash) : null;

    internal static TransactionFileOwnership Capture(string stagedPath, string finalPath,
        string stagingRoot, string? sha256 = null)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(stagedPath);
        var info = Inspect(stagedPath);
        MacFileIdentity? identity = null;
        WindowsTransactionFileIdentity? windowsIdentity = null;
        if (OperatingSystem.IsMacOS())
        {
            if (!MacFileIdentity.TryRead(stagedPath, out var value, out var error))
                throw new IOException("Cannot record transaction file identity before commit: " + stagedPath, error);
            identity = value;
        }
        if (OperatingSystem.IsWindows()) windowsIdentity = WindowsTransactionFileIdentity.Read(stagedPath);
        // CreationTime can be inherited from the replaced filename by NTFS
        // tunneling. Native file identity survives rename; birth time need not.
        return new(finalPath, stagingRoot, info.Length, info.LastWriteTimeUtc, sha256, identity, windowsIdentity);
    }

    // A verified copy already has a SHA from the normal copy/verification pass.
    // The Windows opt-in size-only copy computes an equality proof only when
    // rolling back; successful large-video saves gain no extra full-file read.
    internal Task RetireAsync(string? redundantSource, Action<string, string>? afterQuarantine) =>
        WithdrawAsync(null, redundantSource, afterQuarantine);

    internal Task RestoreAsync(string originalPath, Action<string, string>? afterQuarantine) =>
        WithdrawAsync(originalPath, null, afterQuarantine);

    private async Task WithdrawAsync(string? restorePath, string? redundantSource,
        Action<string, string>? afterQuarantine)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(StagingRoot);
        var quarantineRoot = System.IO.Path.Combine(StagingRoot, "rollback");
        FileSystemPathSemantics.ValidateCanonicalPath(quarantineRoot);
        Directory.CreateDirectory(quarantineRoot);
        // The public filename may already use the filesystem's full component
        // budget. A fixed-size opaque name also handles arbitrarily long suffixes.
        var quarantined = System.IO.Path.Combine(quarantineRoot, Guid.NewGuid().ToString("N"));
        var moved = false;
        try
        {
            // Rename is atomic and never overwrites a destination. Later writers
            // at the original name cannot be removed by rollback cleanup.
            await FileSharingRetry.MoveAsync(Path, quarantined);
            moved = true;
            afterQuarantine?.Invoke(Path, quarantined);
            FileSystemPathSemantics.ValidateCanonicalPath(Path);
            await VerifyAsync(quarantined, redundantSource);
            if (File.Exists(Path) || Directory.Exists(Path) || new FileInfo(Path).LinkTarget is not null)
                throw new IOException("A new file appeared at the committed path during rollback: " + Path);
            if (restorePath is not null)
            {
                await FileSharingRetry.MoveAsync(quarantined, restorePath);
                moved = false;
            }
            else { _retiredPath = quarantined; _redundantSource = redundantSource; }
            // Otherwise retain the verified file inside transaction staging until
            // the whole rollback succeeds. Any other recovery error preserves it.
        }
        catch (Exception error)
        {
            if (moved)
            {
                try
                {
                    // Put an externally changed file back only when its original
                    // name is still free. Never replace a newer external version.
                    await FileSharingRetry.MoveAsync(quarantined, Path);
                    moved = false;
                }
                catch (Exception restoreError)
                {
                    throw new IOException($"Rollback ownership could not be confirmed; preserve both {Path} and {quarantined}.",
                        new AggregateException(error, restoreError));
                }
            }
            throw new IOException("Rollback ownership could not be confirmed; the file was preserved: " + Path, error);
        }
    }

    internal async Task VerifyRetiredBeforeCleanupAsync()
    {
        if (_retiredPath is not { } path) return;
        try { await VerifyAsync(path, _redundantSource); }
        catch (Exception error)
        {
            throw new IOException("A quarantined rollback file changed; preserve transaction staging: " + path, error);
        }
    }

    private async Task VerifyAsync(string quarantined, string? redundantSource)
    {
        VerifyIdentityAndMetadata(quarantined);
        var actualHash = Sha256 is not null || redundantSource is not null ? await HashAsync(quarantined) : null;
        if (Sha256 is not null && !string.Equals(Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Committed file contents changed before rollback: " + Path);
        // Check the redundant original after reading the quarantined copy, not
        // before it: a source changed during that read is not a safe fallback.
        if (redundantSource is not null && !string.Equals(actualHash, await HashAsync(redundantSource),
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("Original movie no longer matches the committed copy; preserve both: " + Path);
        VerifyIdentityAndMetadata(quarantined);
    }

    private void VerifyIdentityAndMetadata(string path)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        var info = Inspect(path);
        if (info.Length != Length || info.LastWriteTimeUtc != LastWriteTimeUtc)
            throw new IOException("Committed file metadata changed before rollback: " + Path);
        if (Identity is { } identity &&
            (!MacFileIdentity.TryRead(path, out var actual, out var error) || actual != identity))
            throw new IOException("Committed file identity changed before rollback: " + Path, error);
        if (WindowsIdentity is { } windowsIdentity && WindowsTransactionFileIdentity.Read(path) != windowsIdentity)
            throw new IOException("Committed file identity changed before rollback: " + Path);
    }

    private static FileInfo Inspect(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || Directory.Exists(path))
            throw new IOException("Transaction file is missing, linked or replaced by a directory: " + path);
        return info;
    }

    private static async Task<string> HashAsync(string path)
    {
        FileSystemPathSemantics.ValidateCanonicalPath(path);
        var before = Capture(path, path, System.IO.Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        before.VerifyIdentityAndMetadata(path);
        return hash;
    }
}

internal readonly record struct WindowsTransactionFileIdentity(uint Volume, ulong FileId)
{
    internal static WindowsTransactionFileIdentity Read(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var value))
            throw new IOException("Cannot record transaction file identity: " + path,
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        return new(value.VolumeSerialNumber, ((ulong)value.FileIndexHigh << 32) | value.FileIndexLow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        internal uint Attributes;
        internal uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint VolumeSerialNumber, SizeHigh, SizeLow, Links, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInformation information);
}
