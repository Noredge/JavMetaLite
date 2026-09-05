namespace JavMetaLite.Core.Services;

public enum MovieFileDiscoveryDiagnosticKind { ReadFailed, PathUnavailable }

public sealed record MovieFileDiscoveryDiagnostic(string Path, string Message)
{
    public MovieFileDiscoveryDiagnosticKind Kind { get; init; } = MovieFileDiscoveryDiagnosticKind.ReadFailed;
}

public sealed record MovieFileDiscoveryResult(
    string RootPath,
    bool IncludedSubdirectories,
    IReadOnlyList<string> VideoPaths,
    int IgnoredFileCount,
    int SkippedDirectoryCount,
    IReadOnlyList<MovieFileDiscoveryDiagnostic> Diagnostics);

public static class MovieFileDiscovery
{
    public static Task<MovieFileDiscoveryResult> DiscoverAsync(
        string rootPath,
        bool includeSubdirectories,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Discover(rootPath, includeSubdirectories, cancellationToken),
            cancellationToken);

    private static MovieFileDiscoveryResult Discover(
        string rootPath,
        bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var normalizedRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"找不到扫描目录：{normalizedRoot}");
        }

        var videoPaths = new List<string>();
        var diagnostics = new List<MovieFileDiscoveryDiagnostic>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(normalizedRoot);
        var ignoredFileCount = 0;
        var skippedDirectoryCount = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new MovieFileDiscoveryDiagnostic(currentDirectory, exception.Message));
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (VideoFileSupport.HasSupportedExtension(file))
                {
                    videoPaths.Add(Path.GetFullPath(file));
                }
                else
                {
                    ignoredFileCount++;
                }
            }

            if (!includeSubdirectories)
            {
                continue;
            }

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new MovieFileDiscoveryDiagnostic(currentDirectory, exception.Message));
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedDirectoryCount++;
                        continue;
                    }

                    pendingDirectories.Push(subdirectory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skippedDirectoryCount++;
                    diagnostics.Add(new MovieFileDiscoveryDiagnostic(subdirectory, exception.Message));
                }
            }
        }

        return new MovieFileDiscoveryResult(
            normalizedRoot,
            includeSubdirectories,
            videoPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ignoredFileCount,
            skippedDirectoryCount,
            diagnostics);
    }
}
