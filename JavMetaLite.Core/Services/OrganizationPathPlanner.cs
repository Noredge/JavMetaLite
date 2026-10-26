using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class OrganizationPathPlanner
{
    public static OrganizationOptions ResolveEffectiveOptions(
        OrganizationOptions options,
        bool usesMultipartNaming)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows() && options.CrossVolumeVerification != CrossVolumeVerificationMode.FullSha256)
            options = new OrganizationOptions(options.TargetMode, options.RenameVideo, options.CustomRootDirectory,
                CrossVolumeVerificationMode.FullSha256);
        return usesMultipartNaming && options.TargetMode is OrganizationTargetMode.VideoDirectory
            ? new OrganizationOptions(
                OrganizationTargetMode.SourceNumberFolder,
                options.RenameVideo,
                crossVolumeVerification: options.CrossVolumeVerification)
            : options;
    }

    public static OrganizationPathPlan Resolve(
        string videoPath,
        string movieId,
        OrganizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sourceVideoPath = FileSystemPathSemantics.CanonicalizePath(videoPath);
        var sourceDirectory = Path.GetDirectoryName(sourceVideoPath)
            ?? throw new InvalidOperationException("无法确定所选影片的文件夹。");
        var normalizedId = options.CreateMovieFolder || options.RenameVideo
            ? NormalizeId(movieId)
            : movieId.Trim().ToUpperInvariant();

        var targetRootDirectory = options.TargetMode switch
        {
            OrganizationTargetMode.VideoDirectory => sourceDirectory,
            OrganizationTargetMode.SourceNumberFolder => sourceDirectory,
            OrganizationTargetMode.CustomRootNumberFolder => CustomRootHistory.NormalizePath(options.CustomRootDirectory),
            _ => throw new InvalidOperationException($"不支持的整理目标模式：{options.TargetMode}")
        };

        var targetDirectory = options.TargetMode switch
        {
            OrganizationTargetMode.VideoDirectory => sourceDirectory,
            OrganizationTargetMode.SourceNumberFolder => AppendIdUnlessAlreadySelected(sourceDirectory, normalizedId),
            OrganizationTargetMode.CustomRootNumberFolder => AppendIdUnlessAlreadySelected(targetRootDirectory, normalizedId),
            _ => throw new InvalidOperationException($"不支持的整理目标模式：{options.TargetMode}")
        };
        targetDirectory = FileSystemPathSemantics.CanonicalizePath(targetDirectory);

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
        if (FileSystemPathSemantics.PathsEqual(sourcePath, targetPath)) return false;
        return FileSystemPathSemantics.GetVolumeRelation(sourcePath, targetPath) != VolumeRelation.Same;
    }

    private static string AppendIdUnlessAlreadySelected(string rootDirectory, string normalizedId)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var directoryName = Path.GetFileName(normalizedRoot);
        return directoryName.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)
            ? normalizedRoot
            : Path.Combine(normalizedRoot, normalizedId);
    }

    private static string NormalizeId(string id)
    {
        var normalized = id.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("整理文件前需要有效的影片番号。");
        }
        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || normalized.Any(char.IsControl) ||
            normalized.EndsWith('.') || normalized.EndsWith(' '))
        {
            throw new InvalidOperationException($"影片番号不能作为 Windows 文件名：{id}");
        }
        return normalized;
    }
}
