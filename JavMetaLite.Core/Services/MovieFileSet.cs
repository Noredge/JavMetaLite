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

    public static MovieFileSet Create(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var parts = paths
            .Select(Analyze)
            .DistinctBy(part => part.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parts.Length == 0)
        {
            throw new ArgumentException("至少需要一个影片文件。", nameof(paths));
        }

        var groupKey = GetGroupKey(parts[0]);
        if (parts.Any(part => !string.Equals(GetGroupKey(part), groupKey, StringComparison.OrdinalIgnoreCase)))
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
            .ThenBy(part => part.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new MovieFileSet(ordered, groupKey);
    }

    public static MovieFileSet Discover(string videoPath)
    {
        var selected = Analyze(videoPath);
        if (!selected.IsMultipart)
        {
            return Create([selected.Path]);
        }

        var directory = Path.GetDirectoryName(selected.Path)!;
        var groupKey = GetGroupKey(selected);
        var siblings = Directory.EnumerateFiles(directory)
            .Where(VideoFileSupport.IsSupportedExistingFile)
            .Select(Analyze)
            .Where(part => string.Equals(GetGroupKey(part), groupKey, StringComparison.OrdinalIgnoreCase))
            .Select(part => part.Path)
            .ToArray();
        return Create(siblings);
    }

    public static IReadOnlyList<MovieFileSet> Group(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selectedParts = paths
            .Where(VideoFileSupport.IsSupportedExistingFile)
            .Select(Analyze)
            .DistinctBy(part => part.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedMultipartKeys = selectedParts
            .Where(part => part.IsMultipart)
            .Select(GetGroupKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expandedParts = new List<MovieFilePart>(selectedParts);
        foreach (var directory in selectedParts
                     .Where(part => part.IsMultipart)
                     .Select(part => Path.GetDirectoryName(part.Path)!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            expandedParts.AddRange(Directory.EnumerateFiles(directory)
                .Where(VideoFileSupport.IsSupportedExistingFile)
                .Select(Analyze)
                .Where(part => selectedMultipartKeys.Contains(GetGroupKey(part))));
        }

        return expandedParts
            .DistinctBy(part => part.Path, StringComparer.OrdinalIgnoreCase)
            .GroupBy(GetGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => Create(group.Select(part => part.Path)))
            .OrderBy(set => set.PrimaryPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetGroupKey(string videoPath) => GetGroupKey(Analyze(videoPath));

    private static MovieFilePart Analyze(string videoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        var fullPath = Path.GetFullPath(videoPath);
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
