using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class FileOrganizationService
{
    private readonly OutputService _outputService;

    public FileOrganizationService(OutputService outputService)
    {
        _outputService = outputService;
    }

    public static SavePlan BuildPlan(
        string videoPath,
        MovieMetadata metadata,
        SaveOptions saveOptions,
        OrganizationOptions organizationOptions)
    {
        var sourceVideoPath = Path.GetFullPath(videoPath);
        if (!File.Exists(sourceVideoPath))
        {
            throw new FileNotFoundException("找不到所选影片。", sourceVideoPath);
        }

        ValidateOutputs(saveOptions);
        var normalizedId = organizationOptions.CreateMovieFolder || organizationOptions.RenameVideo
            ? NormalizeId(metadata.Id)
            : metadata.Id.Trim().ToUpperInvariant();
        var sourceDirectory = Path.GetDirectoryName(sourceVideoPath)!;
        var sourceDirectoryName = new DirectoryInfo(sourceDirectory).Name;
        var targetDirectory = organizationOptions.CreateMovieFolder &&
                              !sourceDirectoryName.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(sourceDirectory, normalizedId)
            : sourceDirectory;
        targetDirectory = Path.GetFullPath(targetDirectory);

        var targetBaseName = organizationOptions.RenameVideo
            ? normalizedId
            : Path.GetFileNameWithoutExtension(sourceVideoPath);
        var targetVideoPath = Path.Combine(
            targetDirectory,
            targetBaseName + Path.GetExtension(sourceVideoPath));

        var changes = new List<PlannedFileChange>();
        var overwriteConflicts = new List<string>();
        var blockingConflicts = new List<string>();

        if (File.Exists(targetDirectory))
        {
            blockingConflicts.Add($"目标文件夹路径已被文件占用：{targetDirectory}");
        }
        else if (!Directory.Exists(targetDirectory))
        {
            changes.Add(new PlannedFileChange(
                PlannedChangeKind.CreateFolder,
                "创建番号文件夹",
                targetDirectory));
        }

        if (!PathsEqual(sourceVideoPath, targetVideoPath))
        {
            var directoryChanges = !PathsEqual(sourceDirectory, targetDirectory);
            var nameChanges = !Path.GetFileName(sourceVideoPath)
                .Equals(Path.GetFileName(targetVideoPath), StringComparison.OrdinalIgnoreCase);
            var kind = directoryChanges && nameChanges
                ? PlannedChangeKind.MoveAndRenameVideo
                : directoryChanges
                    ? PlannedChangeKind.MoveVideo
                    : PlannedChangeKind.RenameVideo;
            var description = kind switch
            {
                PlannedChangeKind.MoveAndRenameVideo => "移动并重命名影片",
                PlannedChangeKind.MoveVideo => "移动影片",
                _ => "重命名影片"
            };
            var blocked = File.Exists(targetVideoPath) || Directory.Exists(targetVideoPath);
            changes.Add(new PlannedFileChange(
                kind,
                description,
                targetVideoPath,
                sourceVideoPath,
                false,
                blocked));
            if (blocked)
            {
                blockingConflicts.Add($"目标影片已经存在，软件不会覆盖影片：{targetVideoPath}");
            }
        }

        foreach (var outputPath in OutputService.GetExpectedOutputFiles(targetVideoPath, metadata, saveOptions))
        {
            var exists = File.Exists(outputPath);
            changes.Add(new PlannedFileChange(
                exists ? PlannedChangeKind.OverwriteFile : PlannedChangeKind.CreateFile,
                exists ? "覆盖 metadata" : "生成 metadata",
                outputPath,
                null,
                exists));
            if (exists)
            {
                overwriteConflicts.Add(outputPath);
            }
        }

        return new SavePlan(
            sourceVideoPath,
            targetVideoPath,
            targetDirectory,
            targetBaseName,
            saveOptions,
            organizationOptions,
            changes,
            overwriteConflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            blockingConflicts.ToArray());
    }

    public async Task<OrganizedSaveResult> ExecuteAsync(
        SavePlan plan,
        MovieMetadata metadata,
        bool allowOverwrite,
        CancellationToken cancellationToken = default)
    {
        if (plan.HasBlockingConflicts)
        {
            throw new IOException(string.Join(Environment.NewLine, plan.BlockingConflicts));
        }
        if (!File.Exists(plan.SourceVideoPath))
        {
            throw new FileNotFoundException("执行前找不到原影片，未进行任何更改。", plan.SourceVideoPath);
        }
        if (plan.VideoWillMove &&
            (File.Exists(plan.TargetVideoPath) || Directory.Exists(plan.TargetVideoPath)))
        {
            throw new IOException($"目标影片已经存在，未进行任何更改：{plan.TargetVideoPath}");
        }

        var currentConflicts = OutputService.GetExpectedOutputFiles(
                plan.TargetVideoPath,
                metadata,
                plan.SaveOptions)
            .Where(File.Exists)
            .ToArray();
        if (currentConflicts.Length > 0 && !allowOverwrite)
        {
            throw new IOException(
                $"以下 metadata 文件已经存在：{Environment.NewLine}{string.Join(Environment.NewLine, currentConflicts)}");
        }

        var sourceDirectory = Path.GetDirectoryName(plan.SourceVideoPath)!;
        var stagingRoot = Path.Combine(sourceDirectory, $".JavMetaLite-{Guid.NewGuid():N}.tmp");
        var stagingVideoPath = Path.Combine(
            stagingRoot,
            plan.TargetBaseName + Path.GetExtension(plan.TargetVideoPath));
        var backupRoot = Path.Combine(stagingRoot, "backup");
        var committedOutputs = new List<string>();
        var backups = new List<(string BackupPath, string OriginalPath)>();
        var createdTargetDirectory = false;
        var videoMoved = false;
        var rollbackSucceeded = false;

        AppLog.Info(
            $"开始执行保存计划 source={plan.SourceVideoPath} target={plan.TargetVideoPath} " +
            $"organize={plan.OrganizationOptions.CreateMovieFolder} rename={plan.OrganizationOptions.RenameVideo}");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            var stagedResult = await _outputService.SaveAsync(
                plan.SourceVideoPath,
                stagingVideoPath,
                metadata,
                plan.SaveOptions with { OverwriteExisting = true },
                cancellationToken);

            var stagedPaths = new[] { stagedResult.NfoPath, stagedResult.PosterPath, stagedResult.FanartPath }
                .Where(path => path is not null)
                .Select(path => path!)
                .Concat(stagedResult.ExtrafanartPaths)
                .ToArray();
            var mappings = stagedPaths
                .Select(path => (
                    StagedPath: path,
                    FinalPath: Path.Combine(plan.TargetDirectory, Path.GetRelativePath(stagingRoot, path))))
                .ToArray();

            if (plan.VideoWillMove &&
                (File.Exists(plan.TargetVideoPath) || Directory.Exists(plan.TargetVideoPath)))
            {
                throw new IOException($"目标影片在预览后被占用：{plan.TargetVideoPath}");
            }
            var lateConflicts = mappings.Select(item => item.FinalPath).Where(File.Exists).ToArray();
            if (lateConflicts.Length > 0 && !allowOverwrite)
            {
                throw new IOException(
                    $"metadata 文件在预览后出现冲突：{Environment.NewLine}{string.Join(Environment.NewLine, lateConflicts)}");
            }

            if (!Directory.Exists(plan.TargetDirectory))
            {
                Directory.CreateDirectory(plan.TargetDirectory);
                createdTargetDirectory = true;
            }

            foreach (var mapping in mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var finalDirectory = Path.GetDirectoryName(mapping.FinalPath)!;
                Directory.CreateDirectory(finalDirectory);
                if (File.Exists(mapping.FinalPath))
                {
                    var relativePath = Path.GetRelativePath(plan.TargetDirectory, mapping.FinalPath);
                    var backupPath = Path.Combine(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Move(mapping.FinalPath, backupPath);
                    backups.Add((backupPath, mapping.FinalPath));
                }

                File.Move(mapping.StagedPath, mapping.FinalPath);
                committedOutputs.Add(mapping.FinalPath);
            }

            if (plan.VideoWillMove)
            {
                File.Move(plan.SourceVideoPath, plan.TargetVideoPath);
                videoMoved = true;
            }

            var finalResult = new SaveResult(
                ResolveFinalPath(stagedResult.NfoPath, stagingRoot, plan.TargetDirectory),
                ResolveFinalPath(stagedResult.PosterPath, stagingRoot, plan.TargetDirectory),
                ResolveFinalPath(stagedResult.FanartPath, stagingRoot, plan.TargetDirectory),
                stagedResult.ExtrafanartPaths
                    .Select(path => ResolveFinalPath(path, stagingRoot, plan.TargetDirectory)!)
                    .ToArray(),
                stagedResult.FanartUsedFullCover);

            AppLog.Info(
                $"保存计划完成 video={plan.TargetVideoPath} outputs={committedOutputs.Count} moved={videoMoved}");
            rollbackSucceeded = true;
            return new OrganizedSaveResult(finalResult, plan.TargetVideoPath, videoMoved);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存计划失败，开始恢复文件", exception);
            var rollbackErrors = Rollback(
                plan,
                videoMoved,
                committedOutputs,
                backups,
                createdTargetDirectory);
            rollbackSucceeded = rollbackErrors.Count == 0;
            if (!rollbackSucceeded)
            {
                AppLog.Error(
                    $"文件恢复不完整，临时备份保留在 {stagingRoot}",
                    new AggregateException(rollbackErrors));
                throw new IOException(
                    $"保存失败且自动恢复不完整。请保留现场并检查：{stagingRoot}",
                    new AggregateException(new[] { exception }.Concat(rollbackErrors)));
            }

            AppLog.Info("文件恢复完成，原影片保持不变");
            throw;
        }
        finally
        {
            if (rollbackSucceeded)
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
    }

    private static List<Exception> Rollback(
        SavePlan plan,
        bool videoMoved,
        IReadOnlyList<string> committedOutputs,
        IReadOnlyList<(string BackupPath, string OriginalPath)> backups,
        bool createdTargetDirectory)
    {
        var errors = new List<Exception>();
        if (videoMoved)
        {
            TryRollback(() => File.Move(plan.TargetVideoPath, plan.SourceVideoPath), errors);
        }

        foreach (var outputPath in committedOutputs.Reverse())
        {
            TryRollback(() => File.Delete(outputPath), errors);
        }
        foreach (var backup in backups.Reverse())
        {
            TryRollback(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalPath)!);
                File.Move(backup.BackupPath, backup.OriginalPath);
            }, errors);
        }

        if (createdTargetDirectory)
        {
            TryDeleteEmptyTree(plan.TargetDirectory);
        }

        return errors;
    }

    private static void TryRollback(Action action, ICollection<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    private static string? ResolveFinalPath(string? stagedPath, string stagingRoot, string targetDirectory) =>
        stagedPath is null
            ? null
            : Path.Combine(targetDirectory, Path.GetRelativePath(stagingRoot, stagedPath));

    private static void ValidateOutputs(SaveOptions options)
    {
        if (!options.WriteNfo && !options.DownloadPoster && !options.DownloadFanart && !options.DownloadExtrafanart)
        {
            throw new InvalidOperationException("请至少选择一种输出：NFO、海报、fanart 或全部剧照。 ");
        }
    }

    private static string NormalizeId(string id)
    {
        var normalized = id.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("整理文件前需要有效的影片番号。 ");
        }
        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.EndsWith('.') || normalized.EndsWith(' '))
        {
            throw new InvalidOperationException($"影片番号不能作为 Windows 文件名：{id}");
        }

        return normalized;
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning($"无法清理临时目录：{path}", exception);
        }
    }

    private static void TryDeleteEmptyTree(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(path).OrderByDescending(value => value.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning($"无法清理空目录：{path}", exception);
        }
    }
}
