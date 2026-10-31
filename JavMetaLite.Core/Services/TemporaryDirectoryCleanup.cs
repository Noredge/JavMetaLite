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
    internal sealed record ExistingRoot(string Path, MacFileIdentity? Identity)
    {
        internal static ExistingRoot Capture(string path)
        {
            var canonical = PathTrim(FileSystemPathSemantics.CanonicalizePath(path));
            ConfirmDirectory(canonical);
            return new(canonical, OperatingSystem.IsMacOS() ? MacFileIdentity.ReadDirectory(canonical) : null);
        }

        internal void Validate()
        {
            FileSystemPathSemantics.ValidateCanonicalPath(Path);
            ConfirmDirectory(Path);
            if (Identity is { } identity && MacFileIdentity.ReadDirectory(Path) != identity)
                throw new IOException("Transaction cleanup root was replaced or its volume is unavailable: " + Path);
        }

        private static void ConfirmDirectory(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                throw new IOException("Transaction cleanup root is not a directory: " + path);
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            _ = entries.MoveNext(); // Force access without enumerating a potentially large library.
        }
    }

    internal static async Task<TemporaryCleanupIssue?> TryDeleteUnderRootAsync(string path, ExistingRoot root,
        Func<string, Task<TemporaryCleanupIssue?>> cleanup)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.EndsInDirectorySeparator(root.Path) ? root.Path : root.Path + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, FileSystemPathSemantics.PathComparison))
                throw new IOException("Transaction staging is outside its existing cleanup root: " + path);
            root.Validate();
            if (await cleanup(path) is { } issue) return issue;
            root.Validate();
            ConfirmAbsent(path, root.Path);
            root.Validate();
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warning($"临时目录所在卷或事务根不可确认，保留恢复记录：{path}", error);
            return new TemporaryCleanupIssue(path, error.Message);
        }
    }

    internal static async Task<TemporaryCleanupIssue?> TryDeleteAsync(string path)
    {
        try
        {
            // Do not use Directory.Exists: it also returns false for inaccessible paths.
            await FileSharingRetry.RunAsync(() =>
            {
                FileSystemPathSemantics.ValidateCanonicalPath(path);
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

    private static void ConfirmAbsent(string path, string? existingRoot = null)
    {
        // Find a readable ancestor and inspect its entries. A missing/unavailable drive
        // is not proof that the staging directory was deleted.
        var child = Path.GetFullPath(path);
        while (Path.GetDirectoryName(child) is { } parent)
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(parent); }
            catch (DirectoryNotFoundException)
            {
                if (existingRoot is not null && FileSystemPathSemantics.PathComparer.Equals(PathTrim(parent), existingRoot))
                    throw new IOException($"事务根不可访问，不能确认临时文件已清理：{path}");
                child = parent;
                continue;
            }
            if (entries.Any(entry => string.Equals(entry, child, StringComparison.OrdinalIgnoreCase)))
                throw new IOException($"临时路径仍存在或无法确认已移除：{path}");
            return;
        }
        throw new IOException($"无法访问临时目录所在磁盘，清理结果未知：{path}");
    }

    private static string PathTrim(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
