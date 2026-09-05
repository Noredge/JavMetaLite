namespace JavMetaLite.Core.Services;

public sealed record MovieInputDiscoveryResult(
    IReadOnlyList<string> InputPaths,
    bool IncludedSubdirectories,
    IReadOnlyList<MovieFileSet> MovieFileSets,
    int IgnoredFileCount,
    int SkippedDirectoryCount,
    IReadOnlyList<MovieFileDiscoveryDiagnostic> Diagnostics)
{
    public IReadOnlyList<string> VideoPaths => MovieFileSets
        .SelectMany(fileSet => fileSet.VideoPaths)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public static class MovieInputDiscovery
{
    public static Task<MovieInputDiscoveryResult> DiscoverAsync(
        IEnumerable<string> inputPaths,
        bool includeSubdirectories = true,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Discover(inputPaths, includeSubdirectories, cancellationToken),
            cancellationToken);

    private static MovieInputDiscoveryResult Discover(
        IEnumerable<string> inputPaths,
        bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        var normalizedInputs = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedInputs.Length == 0)
        {
            throw new ArgumentException("至少需要一个影片文件或文件夹。", nameof(inputPaths));
        }

        var videoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>();
        var diagnostics = new List<MovieFileDiscoveryDiagnostic>();
        var ignoredFileCount = 0;
        var skippedDirectoryCount = 0;

        foreach (var inputPath in normalizedInputs.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(inputPath))
            {
                AddFile(inputPath);
            }
            else if (Directory.Exists(inputPath))
            {
                pendingDirectories.Push(inputPath);
            }
            else
            {
                diagnostics.Add(new MovieFileDiscoveryDiagnostic(inputPath, "路径不存在或无法访问。")
                { Kind = MovieFileDiscoveryDiagnosticKind.PathUnavailable });
            }
        }

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            if (!visitedDirectories.Add(currentDirectory))
            {
                continue;
            }

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

            foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFile(file);
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

            foreach (var subdirectory in subdirectories
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedDirectoryCount++;
                        continue;
                    }

                    pendingDirectories.Push(Path.GetFullPath(subdirectory));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skippedDirectoryCount++;
                    diagnostics.Add(new MovieFileDiscoveryDiagnostic(subdirectory, exception.Message));
                }
            }
        }

        return new MovieInputDiscoveryResult(
            normalizedInputs,
            includeSubdirectories,
            MovieFileSet.Group(videoPaths),
            ignoredFileCount,
            skippedDirectoryCount,
            diagnostics);

        void AddFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!seenFiles.Add(fullPath))
            {
                return;
            }

            if (VideoFileSupport.IsSupportedExistingFile(fullPath))
            {
                videoPaths.Add(fullPath);
            }
            else
            {
                ignoredFileCount++;
            }
        }
    }
}
