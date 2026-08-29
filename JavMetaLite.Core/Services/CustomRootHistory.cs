namespace JavMetaLite.Core.Services;

public static class CustomRootHistory
{
    public const int MaximumEntries = 5;

    public static IReadOnlyList<string> Normalize(
        IEnumerable<string?>? entries,
        string? promotedEntry = null)
    {
        var normalized = new List<string>(MaximumEntries);
        if (TryNormalizePath(promotedEntry, out var promoted))
        {
            normalized.Add(promoted);
        }

        foreach (var entry in entries ?? [])
        {
            if (!TryNormalizePath(entry, out var path) ||
                normalized.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized.Add(path);
            if (normalized.Count == MaximumEntries)
            {
                break;
            }
        }

        return normalized;
    }

    public static bool TryNormalizePath(string? candidate, out string normalizedPath)
    {
        try
        {
            normalizedPath = NormalizePath(candidate);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    public static string NormalizePath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException("请选择自定义目标根目录。");
        }

        var trimmed = candidate.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidOperationException("自定义目标根目录必须是绝对路径。");
        }

        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
            ValidateDirectorySegments(fullPath);
            return fullPath;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("自定义目标根目录不是有效路径。", exception);
        }
    }

    private static void ValidateDirectorySegments(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var remainder = fullPath[root.Length..];
        foreach (var segment in remainder.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.EndsWith('.') || segment.EndsWith(' '))
            {
                throw new InvalidOperationException(
                    $"自定义目标根目录包含不能用于 Windows 文件夹名的字符：{segment}");
            }
        }
    }
}
