using System.Security.Cryptography;
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
        OrganizationOptions organizationOptions,
        LocalSaveContext? localContext = null)
    {
        var sourceVideoPath = Path.GetFullPath(videoPath);
        if (!File.Exists(sourceVideoPath))
        {
            throw new FileNotFoundException("找不到所选影片。", sourceVideoPath);
        }

        ValidateOutputs(saveOptions);
        var pathPlan = OrganizationPathPlanner.Resolve(
            sourceVideoPath,
            metadata.Id,
            organizationOptions);
        var sourceDirectory = pathPlan.SourceDirectory;
        var targetDirectory = pathPlan.TargetDirectory;
        var targetBaseName = pathPlan.TargetBaseName;
        var targetVideoPath = pathPlan.TargetVideoPath;

        var changes = new List<PlannedFileChange>();
        var overwriteConflicts = new List<string>();
        var blockingConflicts = new List<string>();
        var transfers = new List<LocalSidecarTransfer>();
        var expectations = new List<SourceFileExpectation>();
        var retirePaths = new List<string>();

        var executionBlockReason = OrganizationPathPlanner.GetExecutionBlockReason(pathPlan);
        if (executionBlockReason is not null)
        {
            blockingConflicts.Add(executionBlockReason);
        }

        if (pathPlan.UsesCustomRoot && File.Exists(pathPlan.TargetRootDirectory))
        {
            blockingConflicts.Add($"自定义目标根目录路径已被文件占用：{pathPlan.TargetRootDirectory}");
        }
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

        var localBundle = localContext?.MetadataBundle;
        if (localBundle is not null && !PathsEqual(localBundle.Sidecars.VideoPath, sourceVideoPath))
        {
            throw new InvalidOperationException("本地 NFO 上下文不属于当前影片，请重新选择影片。");
        }

        var localNfoPath = NormalizePath(localBundle?.Sidecars.NfoPath);
        var localPosterPath = NormalizePath(localContext?.LocalArtwork?.LocalPosterPath);
        var localFanartPath = NormalizePath(localContext?.LocalArtwork?.LocalFanartPath);
        AddExpectation(localNfoPath, localBundle?.OriginalNfoSha256, "已载入的本地 NFO", expectations, blockingConflicts);
        AddExpectation(localPosterPath, null, "已载入的本地 poster", expectations, blockingConflicts);
        AddExpectation(localFanartPath, null, "已载入的本地 fanart", expectations, blockingConflicts);

        var selectedLocalPair = localContext?.SelectedArtwork?.IsSidecarPair == true;
        var replacePoster = saveOptions.DownloadPoster && !selectedLocalPair;
        var replaceFanart = saveOptions.DownloadFanart && !selectedLocalPair;
        var targetNfoPath = Path.Combine(targetDirectory, $"{targetBaseName}.nfo");
        var targetPosterPath = replacePoster
            ? Path.Combine(targetDirectory, $"{targetBaseName}-poster.jpg")
            : BuildPreservedTarget(localPosterPath, targetDirectory, targetBaseName, "-poster");
        var targetFanartPath = replaceFanart
            ? Path.Combine(targetDirectory, $"{targetBaseName}-fanart.jpg")
            : BuildPreservedTarget(localFanartPath, targetDirectory, targetBaseName, "-fanart");

        var updatePosterReference = targetPosterPath is not null &&
            (localBundle is null || replacePoster ||
             !string.Equals(Path.GetFileName(localPosterPath), Path.GetFileName(targetPosterPath), StringComparison.OrdinalIgnoreCase));
        var updateFanartReference = targetFanartPath is not null &&
            (localBundle is null || replaceFanart ||
             !string.Equals(Path.GetFileName(localFanartPath), Path.GetFileName(targetFanartPath), StringComparison.OrdinalIgnoreCase));
        var posterReference = targetPosterPath is null ? null : Path.GetFileName(targetPosterPath);
        var fanartReference = targetFanartPath is null ? null : Path.GetFileName(targetFanartPath);
        var nfoHasManagedChanges = localBundle is null || NfoRoundTripWriter.HasChanges(
            localBundle,
            metadata,
            updatePosterReference,
            posterReference,
            updateFanartReference,
            fanartReference);
        var generateNfo = saveOptions.WriteNfo && nfoHasManagedChanges;
        var outputOptions = saveOptions with
        {
            WriteNfo = generateNfo,
            DownloadPoster = replacePoster,
            DownloadFanart = replaceFanart
        };

        if (saveOptions.WriteNfo && generateNfo)
        {
            var targetExists = File.Exists(targetNfoPath);
            var kind = localBundle is not null
                ? PlannedChangeKind.UpdateFile
                : targetExists
                    ? PlannedChangeKind.OverwriteFile
                    : PlannedChangeKind.CreateFile;
            changes.Add(new PlannedFileChange(
                kind,
                localBundle is null ? (targetExists ? "覆盖 NFO" : "生成 NFO") : "更新 NFO（保留未知 XML）",
                targetNfoPath,
                localNfoPath,
                targetExists));
            AddOverwriteConflict(targetNfoPath, targetExists, overwriteConflicts);
            AddRetirePath(localNfoPath, targetNfoPath, retirePaths);
        }
        else if (localNfoPath is not null)
        {
            AddPreservedSidecar(
                LocalSidecarRole.Nfo,
                "NFO 内容保持不变",
                localNfoPath,
                targetNfoPath,
                expectations,
                transfers,
                changes,
                overwriteConflicts,
                retirePaths);
        }

        PlanArtwork(LocalSidecarRole.Poster, "poster", saveOptions.DownloadPoster, replacePoster,
            localPosterPath, targetPosterPath, expectations, transfers, changes, overwriteConflicts, retirePaths);
        PlanArtwork(LocalSidecarRole.Fanart, "fanart", saveOptions.DownloadFanart, replaceFanart,
            localFanartPath, targetFanartPath, expectations, transfers, changes, overwriteConflicts, retirePaths);

        var explicitlyPlanned = new HashSet<string>(
            new[] { targetNfoPath, targetPosterPath, targetFanartPath }
                .Where(path => path is not null)
                .Select(path => Path.GetFullPath(path!)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var outputPath in OutputService.GetExpectedOutputFiles(targetVideoPath, metadata, outputOptions))
        {
            if (explicitlyPlanned.Contains(Path.GetFullPath(outputPath)))
            {
                continue;
            }

            var exists = File.Exists(outputPath);
            changes.Add(new PlannedFileChange(
                exists ? PlannedChangeKind.ReplaceImage : PlannedChangeKind.CreateFile,
                exists ? "替换剧照" : "生成剧照",
                outputPath,
                null,
                exists));
            AddOverwriteConflict(outputPath, exists, overwriteConflicts);
        }

        var nfoContext = outputOptions.WriteNfo
            ? new NfoWriteContext(localBundle, updatePosterReference, posterReference, updateFanartReference, fanartReference)
            : null;
        return new SavePlan(
            sourceVideoPath,
            targetVideoPath,
            targetDirectory,
            targetBaseName,
            saveOptions,
            organizationOptions,
            changes,
            overwriteConflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            blockingConflicts.Distinct(StringComparer.Ordinal).ToArray())
        {
            OutputGenerationOptions = outputOptions,
            LocalContext = localContext,
            NfoWriteContext = nfoContext,
            SidecarTransfers = transfers.ToArray(),
            SourceFileExpectations = expectations
                .DistinctBy(expectation => expectation.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SourcePathsToRetire = retirePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
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
        ValidateSourceExpectations(plan.SourceFileExpectations);
        if (plan.VideoWillMove && (File.Exists(plan.TargetVideoPath) || Directory.Exists(plan.TargetVideoPath)))
        {
            throw new IOException($"目标影片已经存在，未进行任何更改：{plan.TargetVideoPath}");
        }

        var currentConflicts = GetPlannedWritePaths(plan, metadata).Where(File.Exists).ToArray();
        if (currentConflicts.Length > 0 && !allowOverwrite)
        {
            throw new IOException(
                $"以下 metadata 文件已经存在：{Environment.NewLine}{string.Join(Environment.NewLine, currentConflicts)}");
        }

        var sourceDirectory = Path.GetDirectoryName(plan.SourceVideoPath)!;
        var stagingRoot = Path.Combine(sourceDirectory, $".JavMetaLite-{Guid.NewGuid():N}.tmp");
        var stagingVideoPath = Path.Combine(stagingRoot, plan.TargetBaseName + Path.GetExtension(plan.TargetVideoPath));
        var backupRoot = Path.Combine(stagingRoot, "backup");
        var committedOutputs = new List<string>();
        var backups = new List<(string BackupPath, string OriginalPath)>();
        var createdTargetDirectory = false;
        var videoMoved = false;
        var rollbackSucceeded = false;

        AppLog.Info(
            $"开始执行保存计划 source={plan.SourceVideoPath} target={plan.TargetVideoPath} " +
            $"organize={plan.OrganizationOptions.CreateMovieFolder} rename={plan.OrganizationOptions.RenameVideo} " +
            $"roundTrip={plan.NfoWriteContext?.LocalBundle is not null} transfers={plan.SidecarTransfers.Count}");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            var stagedResult = HasOutputs(plan.OutputGenerationOptions)
                ? await _outputService.SaveAsync(
                    plan.SourceVideoPath,
                    stagingVideoPath,
                    metadata,
                    plan.OutputGenerationOptions with { OverwriteExisting = true },
                    plan.NfoWriteContext,
                    cancellationToken)
                : new SaveResult(null, null, null, [], false);

            ValidateSourceExpectations(plan.SourceFileExpectations);
            var stagedPaths = new[] { stagedResult.NfoPath, stagedResult.PosterPath, stagedResult.FanartPath }
                .Where(path => path is not null)
                .Select(path => path!)
                .Concat(stagedResult.ExtrafanartPaths)
                .ToList();
            foreach (var transfer in plan.SidecarTransfers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PathsEqual(transfer.SourcePath, transfer.DestinationPath))
                {
                    continue;
                }
                var relativePath = Path.GetRelativePath(plan.TargetDirectory, transfer.DestinationPath);
                if (relativePath.StartsWith("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("sidecar 目标超出影片目标目录。");
                }
                var stagedPath = Path.Combine(stagingRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Copy(transfer.SourcePath, stagedPath, overwrite: false);
                stagedPaths.Add(stagedPath);
            }

            var mappings = stagedPaths
                .Select(path => (StagedPath: path, FinalPath: Path.Combine(
                    plan.TargetDirectory,
                    Path.GetRelativePath(stagingRoot, path))))
                .GroupBy(mapping => mapping.FinalPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Single())
                .ToArray();
            if (plan.VideoWillMove && (File.Exists(plan.TargetVideoPath) || Directory.Exists(plan.TargetVideoPath)))
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
                Directory.CreateDirectory(Path.GetDirectoryName(mapping.FinalPath)!);
                if (File.Exists(mapping.FinalPath))
                {
                    var backupPath = Path.Combine(
                        backupRoot,
                        "existing",
                        Path.GetRelativePath(plan.TargetDirectory, mapping.FinalPath));
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Move(mapping.FinalPath, backupPath);
                    backups.Add((backupPath, mapping.FinalPath));
                }
                File.Move(mapping.StagedPath, mapping.FinalPath);
                committedOutputs.Add(mapping.FinalPath);
            }

            for (var index = 0; index < plan.SourcePathsToRetire.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = plan.SourcePathsToRetire[index];
                if (!File.Exists(sourcePath) || mappings.Any(mapping => PathsEqual(mapping.FinalPath, sourcePath)))
                {
                    continue;
                }
                var backupPath = Path.Combine(backupRoot, "retired", $"{index:D2}-{Path.GetFileName(sourcePath)}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Move(sourcePath, backupPath);
                backups.Add((backupPath, sourcePath));
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
            AppLog.Info($"保存计划完成 video={plan.TargetVideoPath} outputs={committedOutputs.Count} moved={videoMoved}");
            rollbackSucceeded = true;
            return new OrganizedSaveResult(finalResult, plan.TargetVideoPath, videoMoved);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存计划失败，开始恢复文件", exception);
            var rollbackErrors = Rollback(plan, videoMoved, committedOutputs, backups, createdTargetDirectory);
            rollbackSucceeded = rollbackErrors.Count == 0;
            if (!rollbackSucceeded)
            {
                AppLog.Error($"文件恢复不完整，临时备份保留在 {stagingRoot}", new AggregateException(rollbackErrors));
                throw new IOException(
                    $"保存失败且自动恢复不完整。请保留现场并检查：{stagingRoot}",
                    new AggregateException(new[] { exception }.Concat(rollbackErrors)));
            }
            AppLog.Info("文件恢复完成，原影片和 sidecar 保持不变");
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

    private static void PlanArtwork(
        LocalSidecarRole role,
        string displayName,
        bool requested,
        bool replace,
        string? localPath,
        string? targetPath,
        IReadOnlyList<SourceFileExpectation> expectations,
        ICollection<LocalSidecarTransfer> transfers,
        ICollection<PlannedFileChange> changes,
        ICollection<string> overwriteConflicts,
        ICollection<string> retirePaths)
    {
        if (replace && targetPath is not null)
        {
            var targetExists = File.Exists(targetPath);
            var replacesLocal = localPath is not null;
            changes.Add(new PlannedFileChange(
                targetExists || replacesLocal ? PlannedChangeKind.ReplaceImage : PlannedChangeKind.CreateFile,
                targetExists || replacesLocal ? $"替换 {displayName}" : $"生成 {displayName}",
                targetPath,
                localPath,
                targetExists));
            AddOverwriteConflict(targetPath, targetExists, overwriteConflicts);
            AddRetirePath(localPath, targetPath, retirePaths);
            return;
        }
        if (localPath is not null && targetPath is not null)
        {
            AddPreservedSidecar(role, $"{displayName} 内容保持不变", localPath, targetPath,
                expectations, transfers, changes, overwriteConflicts, retirePaths);
            return;
        }
        if (requested)
        {
            changes.Add(new PlannedFileChange(
                PlannedChangeKind.KeepFile,
                $"本地 {displayName} 缺失，保持缺失",
                targetPath ?? $"{displayName}（无文件）"));
        }
    }

    private static void AddPreservedSidecar(
        LocalSidecarRole role,
        string description,
        string sourcePath,
        string destinationPath,
        IReadOnlyList<SourceFileExpectation> expectations,
        ICollection<LocalSidecarTransfer> transfers,
        ICollection<PlannedFileChange> changes,
        ICollection<string> overwriteConflicts,
        ICollection<string> retirePaths)
    {
        var samePath = PathsEqual(sourcePath, destinationPath);
        changes.Add(new PlannedFileChange(
            PlannedChangeKind.KeepFile,
            samePath ? description : $"迁移并保持 {description}",
            destinationPath,
            samePath ? null : sourcePath,
            !samePath && File.Exists(destinationPath)));
        if (samePath)
        {
            return;
        }
        var expectation = expectations.FirstOrDefault(item => PathsEqual(item.Path, sourcePath))
            ?? throw new InvalidOperationException($"缺少 sidecar 指纹：{sourcePath}");
        transfers.Add(new LocalSidecarTransfer(role, sourcePath, destinationPath, expectation.ExpectedSha256));
        AddOverwriteConflict(destinationPath, File.Exists(destinationPath), overwriteConflicts);
        AddRetirePath(sourcePath, destinationPath, retirePaths);
    }

    private static string? BuildPreservedTarget(
        string? sourcePath,
        string targetDirectory,
        string targetBaseName,
        string suffix) =>
        sourcePath is null
            ? null
            : Path.Combine(targetDirectory, targetBaseName + suffix + Path.GetExtension(sourcePath).ToLowerInvariant());

    private static void AddExpectation(
        string? path,
        string? expectedHash,
        string description,
        ICollection<SourceFileExpectation> expectations,
        ICollection<string> blockingConflicts)
    {
        if (path is null)
        {
            return;
        }
        if (!File.Exists(path))
        {
            blockingConflicts.Add($"{description} 已不存在，请重新选择影片：{path}");
            return;
        }
        var currentHash = ComputeSha256(path);
        if (!string.IsNullOrWhiteSpace(expectedHash) &&
            !string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            blockingConflicts.Add($"{description} 在载入后已被外部修改，请重新选择影片：{path}");
        }
        expectations.Add(new SourceFileExpectation(path, expectedHash ?? currentHash, description));
    }

    private static void ValidateSourceExpectations(IEnumerable<SourceFileExpectation> expectations)
    {
        foreach (var expectation in expectations)
        {
            if (!File.Exists(expectation.Path))
            {
                throw new IOException($"{expectation.Description} 在预览后消失，未执行保存：{expectation.Path}");
            }
            if (!string.Equals(ComputeSha256(expectation.Path), expectation.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"{expectation.Description} 在预览后发生变化，未执行保存：{expectation.Path}");
            }
        }
    }

    private static IReadOnlyList<string> GetPlannedWritePaths(SavePlan plan, MovieMetadata metadata) =>
        OutputService.GetExpectedOutputFiles(plan.TargetVideoPath, metadata, plan.OutputGenerationOptions)
            .Concat(plan.SidecarTransfers
                .Where(transfer => !PathsEqual(transfer.SourcePath, transfer.DestinationPath))
                .Select(transfer => transfer.DestinationPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void AddOverwriteConflict(string path, bool exists, ICollection<string> conflicts)
    {
        if (exists)
        {
            conflicts.Add(path);
        }
    }

    private static void AddRetirePath(string? sourcePath, string? destinationPath, ICollection<string> retirePaths)
    {
        if (sourcePath is not null && destinationPath is not null && !PathsEqual(sourcePath, destinationPath))
        {
            retirePaths.Add(sourcePath);
        }
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool HasOutputs(SaveOptions options) =>
        options.WriteNfo || options.DownloadPoster || options.DownloadFanart || options.DownloadExtrafanart;

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
        stagedPath is null ? null : Path.Combine(targetDirectory, Path.GetRelativePath(stagingRoot, stagedPath));

    private static void ValidateOutputs(SaveOptions options)
    {
        if (!HasOutputs(options))
        {
            throw new InvalidOperationException("请至少选择一种输出：NFO、海报、fanart 或全部剧照。 ");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

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
