using System.Text.RegularExpressions;

namespace JavMetaLite.Core.Services;

public sealed record MovieFilePart(
    string Path,
    string MovieBaseName,
    int? PartNumber)
{
    public bool IsMultipart => PartNumber.HasValue;
}

public sealed class MovieFileSet
{
    private static readonly Regex CdSuffixPattern = new(
        @"^(?<base>.+?)[ ._-]+cd(?<number>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private MovieFileSet(IReadOnlyList<MovieFilePart> parts, string groupKey)
    {
        Parts = parts;
        GroupKey = groupKey;
    }

    public IReadOnlyList<MovieFilePart> Parts { get; }

    public IReadOnlyList<string> VideoPaths => Parts.Select(part => part.Path).ToArray();

    public string PrimaryPath => Parts[0].Path;

    public string MovieBaseName => Parts[0].MovieBaseName;

    public string GroupKey { get; }

    public bool UsesMultipartNaming => Parts.All(part => part.IsMultipart);

    // Explicit user confirmation only; automatic discovery deliberately does not infer a/b.
    public static MovieFileSet CreateConfirmedParts(IEnumerable<string> orderedPaths, string movieBaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(movieBaseName);
        var paths = orderedPaths.Select(FileSystemPathSemantics.CanonicalizePath).ToArray();
        if (paths.Length < 2 || paths.Distinct(FileSystemPathSemantics.PathComparer).Count() != paths.Length)
            throw new ArgumentException("需要至少两个不同的影片文件。", nameof(orderedPaths));
        var directory = Path.GetDirectoryName(paths[0]);
        if (paths.Any(path => !string.Equals(Path.GetDirectoryName(path), directory, FileSystemPathSemantics.PathComparison)))
            throw new InvalidOperationException("手动分段必须位于同一来源目录。");
        return new MovieFileSet(paths.Select((path, index) =>
            new MovieFilePart(path, movieBaseName, index + 1)).ToArray(), $"multipart|{directory}|{movieBaseName}");
    }

    public static MovieFileSet Create(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return CreateFromParts(paths.Select(Analyze));
    }

    private static MovieFileSet CreateFromParts(IEnumerable<MovieFilePart> candidates)
    {
        var parts = candidates
            .DistinctBy(part => part.Path, FileSystemPathSemantics.PathComparer)
            .ToArray();
        if (parts.Length == 0)
        {
            throw new ArgumentException("至少需要一个影片文件。", "paths");
        }

        var groupKey = GetGroupKey(parts[0]);
        if (parts.Any(part => !string.Equals(GetGroupKey(part), groupKey, FileSystemPathSemantics.PathComparison)))
        {
            throw new InvalidOperationException("这些影片文件不属于同一个 CD 分段组。 ");
        }

        var duplicatePart = parts
            .Where(part => part.PartNumber.HasValue)
            .GroupBy(part => part.PartNumber!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePart is not null)
        {
            throw new InvalidOperationException($"同一影片存在重复的 CD{duplicatePart.Key} 分段。 ");
        }

        var ordered = parts
            .OrderBy(part => part.PartNumber ?? 0)
            .ThenBy(part => part.Path, FileSystemPathSemantics.PathComparer)
            .ToArray();
        return new MovieFileSet(ordered, groupKey);
    }

    public static MovieFileSet Discover(string videoPath)
    {
        var selected = Analyze(videoPath);
        if (!selected.IsMultipart)
        {
            return CreateFromParts([selected]);
        }

        var directory = Path.GetDirectoryName(selected.Path)!;
        var groupKey = GetGroupKey(selected);
        return CreateFromParts(DiscoverSiblings(directory)
            .Where(part => string.Equals(GetGroupKey(part), groupKey, FileSystemPathSemantics.PathComparison)));
    }

    public static IReadOnlyList<MovieFileSet> Group(IEnumerable<string> paths) => Group(paths, null, default);

    internal static IReadOnlyList<MovieFileSet> Group(
        IEnumerable<string> paths, Action<string, Exception>? onUnreadableSibling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selectedParts = paths
            .Where(VideoFileSupport.IsSupportedExistingFile)
            .Select(Analyze)
            .DistinctBy(part => part.Path, FileSystemPathSemantics.PathComparer)
            .ToArray();
        var selectedMultipartKeys = selectedParts
            .Where(part => part.IsMultipart)
            .Select(GetGroupKey)
            .ToHashSet(FileSystemPathSemantics.PathComparer);
        var expandedParts = new List<MovieFilePart>(selectedParts);
        foreach (var directory in selectedParts
                     .Where(part => part.IsMultipart)
                     .Select(part => Path.GetDirectoryName(part.Path)!)
                     .Distinct(FileSystemPathSemantics.PathComparer))
        {
            expandedParts.AddRange(DiscoverSiblings(directory, onUnreadableSibling, cancellationToken)
                .Where(part => selectedMultipartKeys.Contains(GetGroupKey(part))));
        }

        return expandedParts
            .GroupBy(GetGroupKey, FileSystemPathSemantics.PathComparer)
            .Select(CreateFromParts)
            .OrderBy(set => set.PrimaryPath, FileSystemPathSemantics.PathComparer)
            .ToArray();
    }

    private static IEnumerable<MovieFilePart> DiscoverSiblings(
        string directory, Action<string, Exception>? onUnreadableSibling = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!VideoFileSupport.HasSupportedExtension(path))
                continue;

            MovieFilePart part;
            try { part = Analyze(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                onUnreadableSibling?.Invoke(path, exception);
                continue;
            }

            if (!File.Exists(part.Path))
            {
                onUnreadableSibling?.Invoke(path,
                    new FileNotFoundException($"找不到解析后的影片文件：{part.Path}", part.Path));
                continue;
            }

            yield return part;
        }
    }

    public static string GetGroupKey(string videoPath) => GetGroupKey(Analyze(videoPath));

    private static MovieFilePart Analyze(string videoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        var fullPath = FileSystemPathSemantics.CanonicalizePath(videoPath);
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var match = CdSuffixPattern.Match(stem);
        if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var partNumber) || partNumber <= 0)
        {
            return new MovieFilePart(fullPath, stem, null);
        }

        var movieBaseName = match.Groups["base"].Value.TrimEnd(' ', '.', '_', '-');
        if (string.IsNullOrWhiteSpace(movieBaseName))
        {
            return new MovieFilePart(fullPath, stem, null);
        }

        return new MovieFilePart(fullPath, movieBaseName, partNumber);
    }

    private static string GetGroupKey(MovieFilePart part)
    {
        var directory = Path.GetDirectoryName(part.Path)!;
        return part.IsMultipart
            ? $"multipart|{directory}|{part.MovieBaseName}"
            : $"single|{part.Path}";
    }
}
