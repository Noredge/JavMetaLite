using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class OrganizationPathPlanner
{
    public static OrganizationPathPlan Resolve(
        string videoPath,
        string movieId,
        OrganizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sourceVideoPath = Path.GetFullPath(videoPath);
        var sourceDirectory = Path.GetDirectoryName(sourceVideoPath)
            ?? throw new InvalidOperationException("无法确定所选影片的文件夹。");
        var normalizedId = options.CreateMovieFolder || options.RenameVideo
            ? NormalizeId(movieId)
            : movieId.Trim().ToUpperInvariant();

        var targetRootDirectory = options.TargetMode switch
        {
            OrganizationTargetMode.VideoDirectory => sourceDirectory,
            OrganizationTargetMode.SourceNumberFolder => sourceDirectory,
            OrganizationTargetMode.CustomRootNumberFolder => NormalizeCustomRoot(options.CustomRootDirectory),
            _ => throw new InvalidOperationException($"不支持的整理目标模式：{options.TargetMode}")
        };

        var targetDirectory = options.TargetMode switch
        {
            OrganizationTargetMode.VideoDirectory => sourceDirectory,
            OrganizationTargetMode.SourceNumberFolder => AppendIdUnlessAlreadySelected(sourceDirectory, normalizedId),
            OrganizationTargetMode.CustomRootNumberFolder => AppendIdUnlessAlreadySelected(targetRootDirectory, normalizedId),
            _ => throw new InvalidOperationException($"不支持的整理目标模式：{options.TargetMode}")
        };
        targetDirectory = Path.GetFullPath(targetDirectory);

        var targetBaseName = options.RenameVideo
            ? normalizedId
            : Path.GetFileNameWithoutExtension(sourceVideoPath);
        var targetVideoPath = Path.Combine(
            targetDirectory,
            targetBaseName + Path.GetExtension(sourceVideoPath));

        return new OrganizationPathPlan(
            sourceVideoPath,
            sourceDirectory,
            normalizedId,
            targetRootDirectory,
            targetDirectory,
            targetBaseName,
            targetVideoPath,
            options.UsesCustomRoot,
            RequiresVerifiedCopy(sourceVideoPath, targetVideoPath));
    }

    public static bool RequiresVerifiedCopy(string sourcePath, string targetPath)
    {
        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
        var targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(targetRoot) ||
            !Path.TrimEndingDirectorySeparator(sourceRoot)
                .Equals(Path.TrimEndingDirectorySeparator(targetRoot), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeCustomRoot(string? customRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(customRootDirectory))
        {
            throw new InvalidOperationException("请选择自定义目标根目录。");
        }

        var candidate = customRootDirectory.Trim();
        if (!Path.IsPathFullyQualified(candidate))
        {
            throw new InvalidOperationException("自定义目标根目录必须是绝对路径。");
        }

        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            ValidateDirectorySegments(fullPath);
            return fullPath;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("自定义目标根目录不是有效路径。", exception);
        }
    }

    private static string AppendIdUnlessAlreadySelected(string rootDirectory, string normalizedId)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var directoryName = Path.GetFileName(normalizedRoot);
        return directoryName.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)
            ? normalizedRoot
            : Path.Combine(normalizedRoot, normalizedId);
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
                throw new InvalidOperationException($"自定义目标根目录包含不能用于 Windows 文件夹名的字符：{segment}");
            }
        }
    }

    private static string NormalizeId(string id)
    {
        var normalized = id.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("整理文件前需要有效的影片番号。");
        }
        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.EndsWith('.') || normalized.EndsWith(' '))
        {
            throw new InvalidOperationException($"影片番号不能作为 Windows 文件名：{id}");
        }
        return normalized;
    }
}
