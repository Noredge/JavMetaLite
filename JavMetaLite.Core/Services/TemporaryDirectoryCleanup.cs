using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class FileRecoveryException(string message, IReadOnlyList<string> paths, Exception originalError)
    : IOException(message, originalError)
{
    public IReadOnlyList<string> RecoveryPaths { get; } = paths;
}

public sealed class TemporaryCleanupException(
    IReadOnlyList<TemporaryCleanupIssue> issues, Exception? originalError)
    : IOException("保存未完成，临时目录清理未确认：" + Environment.NewLine +
        string.Join(Environment.NewLine, issues.Select(issue => issue.Path)), originalError)
{
    public IReadOnlyList<TemporaryCleanupIssue> Issues { get; } = issues;
}

internal static class TemporaryDirectoryCleanup
{
    internal static async Task<TemporaryCleanupIssue?> TryDeleteAsync(string path)
    {
        try
        {
            // Do not use Directory.Exists: it also returns false for inaccessible paths.
            await FileSharingRetry.RunAsync(() =>
            {
                try { Directory.Delete(path, recursive: true); }
                catch (DirectoryNotFoundException) { ConfirmAbsent(path); }
                ConfirmAbsent(path);
            }, "cleanup-directory", path);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warning($"临时目录清理未确认：{path}", error);
            return new TemporaryCleanupIssue(path, error.Message);
        }
    }

    private static void ConfirmAbsent(string path)
    {
        // Find a readable ancestor and inspect its entries. A missing/unavailable drive
        // is not proof that the staging directory was deleted.
        var child = Path.GetFullPath(path);
        while (Path.GetDirectoryName(child) is { } parent)
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(parent); }
            catch (DirectoryNotFoundException) { child = parent; continue; }
            if (entries.Any(entry => string.Equals(entry, child, StringComparison.OrdinalIgnoreCase)))
                throw new IOException($"临时路径仍存在或无法确认已移除：{path}");
            return;
        }
        throw new IOException($"无法访问临时目录所在磁盘，清理结果未知：{path}");
    }
}
