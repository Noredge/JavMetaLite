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
        LocalSaveContext? localContext = null) =>
        BuildPlan([videoPath], metadata, saveOptions, organizationOptions, localContext);

    public static SavePlan BuildPlan(
        IEnumerable<string> videoPaths,
        MovieMetadata metadata,
        SaveOptions saveOptions,
        OrganizationOptions organizationOptions,
        LocalSaveContext? localContext = null)
    {
        var fileSet = MovieFileSet.Create(videoPaths);
        foreach (var sourcePath in fileSet.VideoPaths)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("找不到所选影片分段。", sourcePath);
            }
        }

        var sourceVideoPath = fileSet.PrimaryPath;
        ValidateOutputs(saveOptions);
        var effectiveOrganizationOptions = OrganizationPathPlanner.ResolveEffectiveOptions(
            organizationOptions,
            fileSet.UsesMultipartNaming);
        var outputNamingMode = fileSet.UsesMultipartNaming
            ? OutputNamingMode.MovieFolder
            : OutputNamingMode.VideoBase;
        var pathPlan = OrganizationPathPlanner.Resolve(
            sourceVideoPath,
            metadata.Id,
            effectiveOrganizationOptions);
        var sourceDirectory = pathPlan.SourceDirectory;
        var targetDirectory = pathPlan.TargetDirectory;
        var targetBaseName = fileSet.UsesMultipartNaming && !effectiveOrganizationOptions.RenameVideo
            ? fileSet.MovieBaseName
            : pathPlan.TargetBaseName;
        var videoTransfers = fileSet.Parts.Select(part =>
        {
            var targetFileName = fileSet.UsesMultipartNaming
                ? effectiveOrganizationOptions.RenameVideo
                    ? $"{targetBaseName}-cd{part.PartNumber}{Path.GetExtension(part.Path)}"
                    : Path.GetFileName(part.Path)
                : targetBaseName + Path.GetExtension(part.Path);
            var targetPath = Path.Combine(targetDirectory, targetFileName);
            return new VideoFileTransfer(
                part.Path,
                targetPath,
                part.PartNumber,
                OrganizationPathPlanner.RequiresVerifiedCopy(part.Path, targetPath));
        }).ToArray();
        var targetVideoPath = videoTransfers[0].TargetPath;
        var outputAnchorPath = Path.Combine(
            targetDirectory,
            targetBaseName + Path.GetExtension(sourceVideoPath));

        var changes = new List<PlannedFileChange>();
        var overwriteConflicts = new List<string>();
        var blockingConflicts = new List<string>();
        var transfers = new List<LocalSidecarTransfer>();
        var expectations = new List<SourceFileExpectation>();
        var retirePaths = new List<string>();

        if (pathPlan.UsesCustomRoot && File.Exists(pathPlan.TargetRootDirectory))
        {
            blockingConflicts.Add($"自定义目标根目录路径已被文件占用：{pathPlan.TargetRootDirectory}");
        }
        else if (pathPlan.UsesCustomRoot && !Directory.Exists(pathPlan.TargetRootDirectory))
        {
            blockingConflicts.Add(
                $"自定义目标根目录当前不可用，程序不会自动创建该根目录：{pathPlan.TargetRootDirectory}");
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

        foreach (var videoTransfer in videoTransfers.Where(transfer => transfer.WillMove))
        {
            var partSourceDirectory = Path.GetDirectoryName(videoTransfer.SourcePath)!;
            var directoryChanges = !PathsEqual(partSourceDirectory, targetDirectory);
            var nameChanges = !Path.GetFileName(videoTransfer.SourcePath)
                .Equals(Path.GetFileName(videoTransfer.TargetPath), StringComparison.OrdinalIgnoreCase);
            var kind = videoTransfer.RequiresVerifiedCopy
                ? effectiveOrganizationOptions.CrossVolumeVerification is CrossVolumeVerificationMode.FullSha256
                    ? PlannedChangeKind.CopyAndVerifyVideo
                    : PlannedChangeKind.CopyVideo
                : directoryChanges && nameChanges
                    ? PlannedChangeKind.MoveAndRenameVideo
                    : directoryChanges
                        ? PlannedChangeKind.MoveVideo
                        : PlannedChangeKind.RenameVideo;
            var description = kind switch
            {
                PlannedChangeKind.CopyAndVerifyVideo => "安全复制并校验影片，成功后移除来源",
                PlannedChangeKind.CopyVideo => "快速复制影片并检查文件大小，成功后移除来源",
                PlannedChangeKind.MoveAndRenameVideo => "移动并重命名影片",
                PlannedChangeKind.MoveVideo => "移动影片",
                _ => "重命名影片"
            };
            var blocked = File.Exists(videoTransfer.TargetPath) || Directory.Exists(videoTransfer.TargetPath);
            changes.Add(new PlannedFileChange(
                kind,
                description,
                videoTransfer.TargetPath,
                videoTransfer.SourcePath,
                false,
                blocked));
            if (blocked)
            {
                blockingConflicts.Add($"目标影片已经存在，软件不会覆盖影片：{videoTransfer.TargetPath}");
            }
        }

        var localBundle = localContext?.MetadataBundle;
        if (localBundle is not null && !fileSet.VideoPaths.Any(path =>
                PathsEqual(localBundle.Sidecars.VideoPath, path)))
        {
            throw new InvalidOperationException("本地 NFO 上下文不属于当前影片，请重新选择影片。");
        }

        var localNfoPath = NormalizePath(localBundle?.Sidecars.NfoPath);
        var localPosterPath = NormalizePath(localContext?.LocalArtwork?.LocalPosterPath);
        var localFanartPath = NormalizePath(localContext?.LocalArtwork?.LocalFanartPath);
        var localExtrafanartPaths = localContext?.LocalExtrafanartPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var localExtrafanartSet = localExtrafanartPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canUseOnlineExtrafanart = localContext is null || localContext.CanReplaceLocalExtrafanart;
        var generateExtrafanart = saveOptions.DownloadExtrafanart && canUseOnlineExtrafanart;
        var replaceLocalExtrafanart = generateExtrafanart && saveOptions.ReplaceLocalExtrafanart;
        IReadOnlyList<string>? extrafanartSourceLocations = replaceLocalExtrafanart
            ? metadata.ScreenshotUrls
                .Where(location =>
                    !ArtworkLocationHelper.TryGetLocalPath(location, out var localPath) ||
                    !localExtrafanartSet.Contains(localPath))
                .ToArray()
            : null;
        var sourceExtrafanartDirectory = Path.Combine(sourceDirectory, "extrafanart");
        if (localExtrafanartPaths.Any(path =>
                !PathsEqual(Path.GetDirectoryName(path)!, sourceExtrafanartDirectory)))
        {
            throw new InvalidOperationException("本地 extrafanart 上下文不属于当前影片，请重新选择影片。");
        }
        AddExpectation(localNfoPath, localBundle?.OriginalNfoSha256, "已载入的本地 NFO", expectations, blockingConflicts);
        AddExpectation(localPosterPath, null, "已载入的本地 poster", expectations, blockingConflicts);
        AddExpectation(localFanartPath, null, "已载入的本地 fanart", expectations, blockingConflicts);
        foreach (var path in localExtrafanartPaths)
        {
            AddExpectation(path, null, "已载入的本地 extrafanart", expectations, blockingConflicts);
        }

        var selectedLocalPair = localContext?.SelectedArtwork?.IsSidecarPair == true;
        var replacePoster = saveOptions.DownloadPoster && !selectedLocalPair;
        var replaceFanart = saveOptions.DownloadFanart && !selectedLocalPair;
        var inPlace = PathsEqual(sourceDirectory, targetDirectory);
        var preserveExistingNames = inPlace && !fileSet.UsesMultipartNaming;
        var folderNfo = string.Equals(Path.GetFileName(localNfoPath), "movie.nfo", StringComparison.OrdinalIgnoreCase);
        var folderArtwork = new[] { localPosterPath, localFanartPath }.Any(path =>
            Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant() is "poster" or "fanart");
        var defaults = OutputFileNames.Create(targetBaseName,
            inPlace && (folderNfo || localNfoPath is null && folderArtwork)
                ? OutputNamingMode.MovieFolder : outputNamingMode);
        string ExistingNameOrDefault(string? path, string fallback)
        {
            if (!preserveExistingNames || path is null) return fallback;
            if (!PathsEqual(Path.GetDirectoryName(path)!, sourceDirectory))
                throw new InvalidOperationException("本地 sidecar 上下文不属于当前影片目录。");
            // Keep directory-scoped names; filename-scoped sidecars still follow an explicit video rename.
            var stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return !pathPlan.TargetBaseName.Equals(Path.GetFileNameWithoutExtension(sourceVideoPath), StringComparison.OrdinalIgnoreCase)
                   && stem is not ("movie" or "poster" or "fanart" or "cover" or "folder" or "default" or "backdrop" or "background" or "art")
                ? fallback : Path.GetFileName(path);
        }
        var outputFileNames = new OutputFileNames(
            ExistingNameOrDefault(localNfoPath, defaults.Nfo),
            ExistingNameOrDefault(localPosterPath, defaults.Poster),
            ExistingNameOrDefault(localFanartPath, defaults.Fanart));
        var targetNfoPath = outputFileNames.Resolve(targetDirectory, outputFileNames.Nfo);
        var targetPosterPath = replacePoster
            ? outputFileNames.Resolve(targetDirectory, outputFileNames.Poster)
            : preserveExistingNames && localPosterPath is not null
                ? outputFileNames.Resolve(targetDirectory, ExistingNameOrDefault(localPosterPath,
                    Path.ChangeExtension(defaults.Poster, Path.GetExtension(localPosterPath))))
            : BuildPreservedTarget(
                localPosterPath,
                targetDirectory,
                targetBaseName,
                "-poster",
                outputNamingMode,
                "poster");
        var targetFanartPath = replaceFanart
            ? outputFileNames.Resolve(targetDirectory, outputFileNames.Fanart)
            : preserveExistingNames && localFanartPath is not null
                ? outputFileNames.Resolve(targetDirectory, ExistingNameOrDefault(localFanartPath,
                    Path.ChangeExtension(defaults.Fanart, Path.GetExtension(localFanartPath))))
            : BuildPreservedTarget(
                localFanartPath,
                targetDirectory,
                targetBaseName,
                "-fanart",
                outputNamingMode,
                "fanart");
        foreach (var path in new[] { replacePoster ? targetPosterPath : null, replaceFanart ? targetFanartPath : null })
        {
            if (path is not null && Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
                blockingConflicts.Add($"无法原地编码 WebP 图片，请保留本地图片或保存到新目录：{path}");
        }

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
            fanartReference,
            saveOptions.IncludeIdInTitle);
        var generateNfo = saveOptions.WriteNfo && nfoHasManagedChanges;
        var outputOptions = saveOptions with
        {
            WriteNfo = generateNfo,
            DownloadPoster = replacePoster,
            DownloadFanart = replaceFanart,
            DownloadExtrafanart = generateExtrafanart
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
                localBundle is null
                    ? targetExists ? "覆盖 NFO" : "生成 NFO"
                    : localBundle.HasUnknownXml ? "更新 NFO（保留未知 XML）" : "更新 NFO",
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
        if (!generateExtrafanart)
        {
            foreach (var localPath in localExtrafanartPaths)
            {
                var targetPath = Path.Combine(targetDirectory, "extrafanart", Path.GetFileName(localPath));
                AddPreservedSidecar(
                    LocalSidecarRole.Extrafanart,
                    "本地 extrafanart 内容保持不变",
                    localPath,
                    targetPath,
                    expectations,
                    transfers,
                    changes,
                    overwriteConflicts,
                    retirePaths);
            }

        }

        var explicitlyPlanned = new HashSet<string>(
            new[] { targetNfoPath, targetPosterPath, targetFanartPath }
                .Where(path => path is not null)
                .Select(path => Path.GetFullPath(path!)),
            StringComparer.OrdinalIgnoreCase);
        var expectedOutputPaths = OutputService.GetExpectedOutputFiles(
                outputAnchorPath,
            metadata,
            outputOptions,
            outputNamingMode,
            OutputService.SelectScreenshotLocations(extrafanartSourceLocations ?? metadata.ScreenshotUrls).Count,
            outputFileNames)
            .ToArray();
        foreach (var outputPath in expectedOutputPaths)
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

        if (generateExtrafanart)
        {
            foreach (var localPath in localExtrafanartPaths)
            {
                retirePaths.Add(localPath);
                if (expectedOutputPaths.Any(outputPath => PathsEqual(outputPath, localPath)))
                {
                    continue;
                }

                changes.Add(new PlannedFileChange(
                    PlannedChangeKind.RemoveFile,
                    "移除未选或迁移后的本地剧照",
                    localPath));
            }
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
            effectiveOrganizationOptions,
            changes,
            overwriteConflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            blockingConflicts.Distinct(StringComparer.Ordinal).ToArray())
        {
            OutputGenerationOptions = outputOptions,
            OutputAnchorPath = outputAnchorPath,
            OutputNamingMode = outputNamingMode,
            OutputFileNames = outputFileNames,
            VideoTransfers = videoTransfers,
            LocalContext = localContext,
            NfoWriteContext = nfoContext,
            SidecarTransfers = transfers.ToArray(),
            SourceFileExpectations = expectations
                .DistinctBy(expectation => expectation.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SourcePathsToRetire = retirePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ExtrafanartSourceLocations = extrafanartSourceLocations,
            RequiresVerifiedVideoCopy = videoTransfers.Any(transfer =>
                transfer.WillMove && transfer.RequiresVerifiedCopy)
        };
    }

    public async Task<OrganizedSaveResult> ExecuteAsync(
        SavePlan plan,
        MovieMetadata metadata,
        bool allowOverwrite,
        CancellationToken cancellationToken = default,
        IProgress<FileTransactionProgress>? progress = null)
    {
        if (plan.VideoTransfers.Count > 1)
        {
            return await ExecuteMultipartAsync(
                plan,
                metadata,
                allowOverwrite,
                cancellationToken,
                progress);
        }

        progress?.Report(new FileTransactionProgress(
            FileTransactionStage.Preparing,
            "正在准备 metadata 与安全事务…"));
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
        var operationId = Guid.NewGuid().ToString("N");
        var sourceStagingRoot = Path.Combine(sourceDirectory, $".JavMetaLite-{operationId}.tmp");
        var stagingVideoPath = Path.Combine(
            sourceStagingRoot,
            plan.TargetBaseName + Path.GetExtension(plan.TargetVideoPath));
        var crossVolumeCopy = plan.RequiresVerifiedVideoCopy && plan.VideoWillMove;
        var fullVerification = crossVolumeCopy &&
                               plan.OrganizationOptions.CrossVolumeVerification is
                                   CrossVolumeVerificationMode.FullSha256;
        var targetStagingRoot = crossVolumeCopy
            ? Path.Combine(plan.TargetDirectory, $".JavMetaLite-target-{operationId}.tmp")
            : sourceStagingRoot;
        var targetPayloadRoot = crossVolumeCopy
            ? Path.Combine(targetStagingRoot, "payload")
            : sourceStagingRoot;
        var backupRoot = Path.Combine(targetStagingRoot, "backup");
        var sourceRetireRoot = Path.Combine(sourceStagingRoot, "retired");
        var committedOutputs = new List<string>();
        var backups = new List<(string BackupPath, string OriginalPath)>();
        var createdTargetDirectory = false;
        var videoMoved = false;
        var targetVideoCommitted = false;
        var sourceVideoRetired = false;
        string? sourceVideoBackupPath = null;
        var operationSucceeded = false;
        var rollbackSucceeded = false;
        using var timing = new SaveTimingLog("transaction", metadata.Id);
        timing.Begin("metadataPrepare");

        if (crossVolumeCopy)
        {
            EnsureTargetCapacity(plan.SourceVideoPath, plan.TargetDirectory);
        }

        AppLog.Info(
            $"开始执行保存计划 source={plan.SourceVideoPath} target={plan.TargetVideoPath} " +
            $"organize={plan.OrganizationOptions.CreateMovieFolder} rename={plan.OrganizationOptions.RenameVideo} " +
            $"roundTrip={plan.NfoWriteContext?.LocalBundle is not null} transfers={plan.SidecarTransfers.Count} " +
            $"crossVolumeCopy={crossVolumeCopy} verification={plan.OrganizationOptions.CrossVolumeVerification}");

        try
        {
            Directory.CreateDirectory(sourceStagingRoot);
            var stagedResult = HasOutputs(plan.OutputGenerationOptions)
                ? await _outputService.SaveAsync(
                    plan.SourceVideoPath,
                    stagingVideoPath,
                    metadata,
                    plan.OutputGenerationOptions with { OverwriteExisting = true },
                    plan.NfoWriteContext,
                    plan.OutputNamingMode,
                    cancellationToken,
                    plan.ExtrafanartSourceLocations,
                    plan.OutputFileNames)
                : new SaveResult(null, null, null, [], false);

            timing.Begin("stageAndVerify");
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
                var stagedPath = Path.Combine(sourceStagingRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Copy(transfer.SourcePath, stagedPath, overwrite: false);
                stagedPaths.Add(stagedPath);
            }

            if (!Directory.Exists(plan.TargetDirectory))
            {
                Directory.CreateDirectory(plan.TargetDirectory);
                createdTargetDirectory = true;
            }

            string? targetStagedVideoPath = null;
            IReadOnlyList<string> commitStagedPaths = stagedPaths;
            if (crossVolumeCopy)
            {
                Directory.CreateDirectory(targetPayloadRoot);
                var targetCopies = new List<string>(stagedPaths.Count);
                foreach (var stagedPath in stagedPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(sourceStagingRoot, stagedPath);
                    var targetStagedPath = Path.Combine(targetPayloadRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetStagedPath)!);
                    File.Copy(stagedPath, targetStagedPath, overwrite: false);
                    targetCopies.Add(targetStagedPath);
                }
                commitStagedPaths = targetCopies;

                targetStagedVideoPath = Path.Combine(
                    targetStagingRoot,
                    "movie",
                    Path.GetFileName(plan.TargetVideoPath));
                var sourceLength = new FileInfo(plan.SourceVideoPath).Length;
                var sourceHash = await CopyMovieAsync(
                    plan.SourceVideoPath,
                    targetStagedVideoPath,
                    fullVerification,
                    progress,
                    cancellationToken);
                var targetLength = new FileInfo(targetStagedVideoPath).Length;
                if (sourceLength != targetLength)
                {
                    throw new IOException(
                        $"目标影片大小检查失败；来源影片已保留，未提交目标文件。" +
                        $"来源 {sourceLength} 字节，目标 {targetLength} 字节。");
                }
                if (fullVerification)
                {
                    progress?.Report(new FileTransactionProgress(
                        FileTransactionStage.VerifyingMovie,
                        "正在校验目标影片 SHA-256…",
                        0,
                        targetLength,
                        targetStagedVideoPath));
                    var targetHash = await ComputeSha256Async(
                        targetStagedVideoPath,
                        progress,
                        cancellationToken);
                    if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("目标影片 SHA-256 校验失败；来源影片已保留，未提交目标文件。");
                    }
                    AppLog.Info($"跨卷影片复制校验完成 sha256={sourceHash} bytes={targetLength}");
                }
                else
                {
                    AppLog.Warning($"跨卷影片使用快速传输，仅检查文件大小 bytes={targetLength}");
                }
            }

            var mappings = commitStagedPaths
                .Select(path => (StagedPath: path, FinalPath: Path.Combine(
                    plan.TargetDirectory,
                    Path.GetRelativePath(targetPayloadRoot, path))))
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

            timing.Begin("commit");
            progress?.Report(new FileTransactionProgress(
                FileTransactionStage.Committing,
                "正在提交目标影片与 metadata…"));
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
                    await FileSharingRetry.MoveAsync(mapping.FinalPath, backupPath, cancellationToken);
                    backups.Add((backupPath, mapping.FinalPath));
                }
                await FileSharingRetry.MoveAsync(mapping.StagedPath, mapping.FinalPath, cancellationToken);
                committedOutputs.Add(mapping.FinalPath);
            }

            if (crossVolumeCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await FileSharingRetry.MoveAsync(targetStagedVideoPath!, plan.TargetVideoPath, cancellationToken);
                targetVideoCommitted = true;
            }

            for (var index = 0; index < plan.SourcePathsToRetire.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = plan.SourcePathsToRetire[index];
                if (!File.Exists(sourcePath) || mappings.Any(mapping => PathsEqual(mapping.FinalPath, sourcePath)))
                {
                    continue;
                }
                var backupPath = Path.Combine(sourceRetireRoot, $"{index:D2}-{Path.GetFileName(sourcePath)}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                await FileSharingRetry.MoveAsync(sourcePath, backupPath, cancellationToken);
                backups.Add((backupPath, sourcePath));
            }

            if (plan.VideoWillMove)
            {
                if (crossVolumeCopy)
                {
                    progress?.Report(new FileTransactionProgress(
                        fullVerification
                            ? FileTransactionStage.RetiringSource
                            : FileTransactionStage.RetiringSourceFast,
                        fullVerification
                            ? "目标校验与提交完成，正在移除来源影片…"
                            : "目标复制与提交完成，正在移除来源影片…"));
                    sourceVideoBackupPath = Path.Combine(
                        sourceRetireRoot,
                        "movie",
                        Path.GetFileName(plan.SourceVideoPath));
                    Directory.CreateDirectory(Path.GetDirectoryName(sourceVideoBackupPath)!);
                    await FileSharingRetry.MoveAsync(plan.SourceVideoPath, sourceVideoBackupPath, cancellationToken);
                    sourceVideoRetired = true;
                }
                else
                {
                    await FileSharingRetry.MoveAsync(plan.SourceVideoPath, plan.TargetVideoPath, cancellationToken);
                }
                videoMoved = true;
            }

            var finalResult = new SaveResult(
                ResolveFinalPath(stagedResult.NfoPath, sourceStagingRoot, plan.TargetDirectory),
                ResolveFinalPath(stagedResult.PosterPath, sourceStagingRoot, plan.TargetDirectory),
                ResolveFinalPath(stagedResult.FanartPath, sourceStagingRoot, plan.TargetDirectory),
                stagedResult.ExtrafanartPaths
                    .Select(path => ResolveFinalPath(path, sourceStagingRoot, plan.TargetDirectory)!)
                    .ToArray(),
                stagedResult.FanartUsedFullCover);
            AppLog.Info($"保存计划完成 video={plan.TargetVideoPath} outputs={committedOutputs.Count} moved={videoMoved}");
            progress?.Report(new FileTransactionProgress(
                FileTransactionStage.Completed,
                crossVolumeCopy
                    ? fullVerification
                        ? "安全复制、校验与提交已完成"
                        : "快速跨卷复制与提交已完成"
                    : "安全保存已完成"));
            operationSucceeded = true;
            rollbackSucceeded = true;
            timing.Complete();
            return new OrganizedSaveResult(finalResult, plan.TargetVideoPath, videoMoved);
        }
        catch (Exception exception)
        {
            timing.Begin("rollback");
            AppLog.Error("保存计划失败，开始恢复文件", exception);
            var rollbackErrors = await RollbackAsync(
                plan,
                videoMoved,
                crossVolumeCopy,
                targetVideoCommitted,
                sourceVideoRetired,
                sourceVideoBackupPath,
                committedOutputs,
                backups);
            rollbackSucceeded = rollbackErrors.Count == 0;
            if (!rollbackSucceeded)
            {
                var recoveryPaths = crossVolumeCopy
                    ? $"{sourceStagingRoot}；{targetStagingRoot}"
                    : sourceStagingRoot;
                AppLog.Error($"文件恢复不完整，临时备份保留在 {recoveryPaths}", new AggregateException(rollbackErrors));
                throw new IOException(
                    $"保存失败且自动恢复不完整。请保留现场并检查：{recoveryPaths}",
                    new AggregateException(new[] { exception }.Concat(rollbackErrors)));
            }
            AppLog.Info("文件恢复完成，原影片和 sidecar 保持不变");
            throw;
        }
        finally
        {
            timing.Begin("cleanup");
            if (operationSucceeded)
                TryRemoveMigratedExtrafanartDirectory(plan);
            if (rollbackSucceeded)
            {
                TryDeleteDirectory(sourceStagingRoot);
                if (!PathsEqual(sourceStagingRoot, targetStagingRoot))
                {
                    TryDeleteDirectory(targetStagingRoot);
                }
                if (!operationSucceeded && createdTargetDirectory)
                {
                    TryDeleteEmptyTree(plan.TargetDirectory);
                }
            }
        }
    }

    private async Task<OrganizedSaveResult> ExecuteMultipartAsync(
        SavePlan plan,
        MovieMetadata metadata,
        bool allowOverwrite,
        CancellationToken cancellationToken,
        IProgress<FileTransactionProgress>? progress)
    {
        progress?.Report(new FileTransactionProgress(
            FileTransactionStage.Preparing,
            "正在准备多分段影片与 metadata 安全事务…"));
        if (plan.HasBlockingConflicts)
        {
            throw new IOException(string.Join(Environment.NewLine, plan.BlockingConflicts));
        }

        foreach (var transfer in plan.VideoTransfers)
        {
            if (!File.Exists(transfer.SourcePath))
            {
                throw new FileNotFoundException("执行前找不到原影片分段，未进行任何更改。", transfer.SourcePath);
            }
            if (transfer.WillMove && (File.Exists(transfer.TargetPath) || Directory.Exists(transfer.TargetPath)))
            {
                throw new IOException($"目标影片分段已经存在，未进行任何更改：{transfer.TargetPath}");
            }
        }

        ValidateSourceExpectations(plan.SourceFileExpectations);
        var currentConflicts = GetPlannedWritePaths(plan, metadata).Where(File.Exists).ToArray();
        if (currentConflicts.Length > 0 && !allowOverwrite)
        {
            throw new IOException(
                $"以下 metadata 文件已经存在：{Environment.NewLine}{string.Join(Environment.NewLine, currentConflicts)}");
        }

        var sourceDirectory = Path.GetDirectoryName(plan.SourceVideoPath)!;
        var operationId = Guid.NewGuid().ToString("N");
        var sourceStagingRoot = Path.Combine(sourceDirectory, $".JavMetaLite-{operationId}.tmp");
        var stagingOutputAnchorPath = Path.Combine(
            sourceStagingRoot,
            plan.TargetBaseName + Path.GetExtension(plan.OutputAnchorPath));
        var crossVolumeCopy = plan.RequiresVerifiedVideoCopy || plan.VideoTransfers.Any(transfer =>
            transfer.WillMove && transfer.RequiresVerifiedCopy);
        var fullVerification = crossVolumeCopy &&
                               plan.OrganizationOptions.CrossVolumeVerification is
                                   CrossVolumeVerificationMode.FullSha256;
        var targetStagingRoot = crossVolumeCopy
            ? Path.Combine(plan.TargetDirectory, $".JavMetaLite-target-{operationId}.tmp")
            : sourceStagingRoot;
        var targetPayloadRoot = crossVolumeCopy
            ? Path.Combine(targetStagingRoot, "payload")
            : sourceStagingRoot;
        var backupRoot = Path.Combine(targetStagingRoot, "backup");
        var sourceRetireRoot = Path.Combine(sourceStagingRoot, "retired");
        var committedOutputs = new List<string>();
        var backups = new List<(string BackupPath, string OriginalPath)>();
        var committedVideoTargets = new List<string>();
        var retiredVideoSources = new List<(string BackupPath, string OriginalPath)>();
        var sameVolumeMoves = new List<VideoFileTransfer>();
        var createdTargetDirectory = false;
        var operationSucceeded = false;
        var rollbackSucceeded = false;
        using var timing = new SaveTimingLog("transaction-multipart", metadata.Id);
        timing.Begin("metadataPrepare");

        if (crossVolumeCopy)
        {
            EnsureTargetCapacity(
                plan.VideoTransfers.Where(transfer => transfer.WillMove).Select(transfer => transfer.SourcePath),
                plan.TargetDirectory);
        }

        AppLog.Info(
            $"开始执行多分段保存计划 parts={plan.VideoTransfers.Count} source={plan.SourceVideoPath} " +
            $"target={plan.TargetDirectory} roundTrip={plan.NfoWriteContext?.LocalBundle is not null} " +
            $"crossVolumeCopy={crossVolumeCopy} verification={plan.OrganizationOptions.CrossVolumeVerification}");

        try
        {
            Directory.CreateDirectory(sourceStagingRoot);
            var stagedResult = HasOutputs(plan.OutputGenerationOptions)
                ? await _outputService.SaveAsync(
                    plan.SourceVideoPath,
                    stagingOutputAnchorPath,
                    metadata,
                    plan.OutputGenerationOptions with { OverwriteExisting = true },
                    plan.NfoWriteContext,
                    plan.OutputNamingMode,
                    cancellationToken,
                    plan.ExtrafanartSourceLocations,
                    plan.OutputFileNames)
                : new SaveResult(null, null, null, [], false);

            timing.Begin("stageAndVerify");
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
                    throw new InvalidOperationException("sidecar 目标超出影片目标目录。 ");
                }
                var stagedPath = Path.Combine(sourceStagingRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Copy(transfer.SourcePath, stagedPath, overwrite: false);
                stagedPaths.Add(stagedPath);
            }

            if (!Directory.Exists(plan.TargetDirectory))
            {
                Directory.CreateDirectory(plan.TargetDirectory);
                createdTargetDirectory = true;
            }

            IReadOnlyList<string> commitStagedPaths = stagedPaths;
            var stagedVideos = new List<(VideoFileTransfer Transfer, string StagedPath)>();
            if (crossVolumeCopy)
            {
                Directory.CreateDirectory(targetPayloadRoot);
                var targetCopies = new List<string>(stagedPaths.Count);
                foreach (var stagedPath in stagedPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(sourceStagingRoot, stagedPath);
                    var targetStagedPath = Path.Combine(targetPayloadRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetStagedPath)!);
                    File.Copy(stagedPath, targetStagedPath, overwrite: false);
                    targetCopies.Add(targetStagedPath);
                }
                commitStagedPaths = targetCopies;

                var movingTransfers = plan.VideoTransfers.Where(transfer => transfer.WillMove).ToArray();
                for (var index = 0; index < movingTransfers.Length; index++)
                {
                    var transfer = movingTransfers[index];
                    var stagedVideoPath = Path.Combine(
                        targetStagingRoot,
                        "movie",
                        $"{index:D2}-{Path.GetFileName(transfer.TargetPath)}");
                    var sourceLength = new FileInfo(transfer.SourcePath).Length;
                    var sourceHash = await CopyMovieAsync(
                        transfer.SourcePath,
                        stagedVideoPath,
                        fullVerification,
                        progress,
                        cancellationToken);
                    var targetLength = new FileInfo(stagedVideoPath).Length;
                    if (sourceLength != targetLength)
                    {
                        throw new IOException(
                            $"目标影片分段大小检查失败；来源已保留：{Path.GetFileName(transfer.SourcePath)}");
                    }
                    if (fullVerification)
                    {
                        var targetHash = await ComputeSha256Async(stagedVideoPath, progress, cancellationToken);
                        if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException(
                                $"目标影片分段 SHA-256 校验失败；来源已保留：{Path.GetFileName(transfer.SourcePath)}");
                        }
                    }
                    stagedVideos.Add((transfer, stagedVideoPath));
                }
            }

            var mappings = commitStagedPaths
                .Select(path => (StagedPath: path, FinalPath: Path.Combine(
                    plan.TargetDirectory,
                    Path.GetRelativePath(targetPayloadRoot, path))))
                .GroupBy(mapping => mapping.FinalPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Single())
                .ToArray();
            var lateVideoConflicts = plan.VideoTransfers
                .Where(transfer => transfer.WillMove)
                .Select(transfer => transfer.TargetPath)
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToArray();
            if (lateVideoConflicts.Length > 0)
            {
                throw new IOException(
                    $"目标影片分段在预览后被占用：{Environment.NewLine}{string.Join(Environment.NewLine, lateVideoConflicts)}");
            }
            var lateConflicts = mappings.Select(item => item.FinalPath).Where(File.Exists).ToArray();
            if (lateConflicts.Length > 0 && !allowOverwrite)
            {
                throw new IOException(
                    $"metadata 文件在预览后出现冲突：{Environment.NewLine}{string.Join(Environment.NewLine, lateConflicts)}");
            }

            timing.Begin("commit");
            progress?.Report(new FileTransactionProgress(
                FileTransactionStage.Committing,
                "正在提交多分段影片与 metadata…"));
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
                    await FileSharingRetry.MoveAsync(mapping.FinalPath, backupPath, cancellationToken);
                    backups.Add((backupPath, mapping.FinalPath));
                }
                await FileSharingRetry.MoveAsync(mapping.StagedPath, mapping.FinalPath, cancellationToken);
                committedOutputs.Add(mapping.FinalPath);
            }

            foreach (var stagedVideo in stagedVideos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await FileSharingRetry.MoveAsync(stagedVideo.StagedPath, stagedVideo.Transfer.TargetPath, cancellationToken);
                committedVideoTargets.Add(stagedVideo.Transfer.TargetPath);
            }

            for (var index = 0; index < plan.SourcePathsToRetire.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = plan.SourcePathsToRetire[index];
                if (!File.Exists(sourcePath) || mappings.Any(mapping => PathsEqual(mapping.FinalPath, sourcePath)))
                {
                    continue;
                }
                var backupPath = Path.Combine(sourceRetireRoot, $"sidecar-{index:D2}-{Path.GetFileName(sourcePath)}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                await FileSharingRetry.MoveAsync(sourcePath, backupPath, cancellationToken);
                backups.Add((backupPath, sourcePath));
            }

            var movingVideoTransfers = plan.VideoTransfers.Where(transfer => transfer.WillMove).ToArray();
            if (crossVolumeCopy)
            {
                progress?.Report(new FileTransactionProgress(
                    fullVerification ? FileTransactionStage.RetiringSource : FileTransactionStage.RetiringSourceFast,
                    fullVerification
                        ? "所有分段校验与提交完成，正在移除来源影片…"
                        : "所有分段复制与提交完成，正在移除来源影片…"));
                for (var index = 0; index < movingVideoTransfers.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var transfer = movingVideoTransfers[index];
                    var backupPath = Path.Combine(
                        sourceRetireRoot,
                        "movie",
                        $"{index:D2}-{Path.GetFileName(transfer.SourcePath)}");
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    await FileSharingRetry.MoveAsync(transfer.SourcePath, backupPath, cancellationToken);
                    retiredVideoSources.Add((backupPath, transfer.SourcePath));
                }
            }
            else
            {
                foreach (var transfer in movingVideoTransfers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await FileSharingRetry.MoveAsync(transfer.SourcePath, transfer.TargetPath, cancellationToken);
                    sameVolumeMoves.Add(transfer);
                }
            }

            var finalResult = new SaveResult(
                ResolveFinalPath(stagedResult.NfoPath, sourceStagingRoot, plan.TargetDirectory),
                ResolveFinalPath(stagedResult.PosterPath, sourceStagingRoot, plan.TargetDirectory),
                ResolveFinalPath(stagedResult.FanartPath, sourceStagingRoot, plan.TargetDirectory),
                stagedResult.ExtrafanartPaths
                    .Select(path => ResolveFinalPath(path, sourceStagingRoot, plan.TargetDirectory)!)
                    .ToArray(),
                stagedResult.FanartUsedFullCover);
            var targetVideoPaths = plan.VideoTransfers.Select(transfer => transfer.TargetPath).ToArray();
            AppLog.Info(
                $"多分段保存计划完成 parts={targetVideoPaths.Length} outputs={committedOutputs.Count} " +
                $"moved={movingVideoTransfers.Length > 0}");
            progress?.Report(new FileTransactionProgress(
                FileTransactionStage.Completed,
                "多分段影片与共用 metadata 已安全保存"));
            operationSucceeded = true;
            rollbackSucceeded = true;
            timing.Complete();
            return new OrganizedSaveResult(finalResult, targetVideoPaths[0], movingVideoTransfers.Length > 0)
            {
                VideoPaths = targetVideoPaths
            };
        }
        catch (Exception exception)
        {
            timing.Begin("rollback");
            AppLog.Error("多分段保存计划失败，开始恢复文件", exception);
            var rollbackErrors = new List<Exception>();
            foreach (var transfer in sameVolumeMoves.AsEnumerable().Reverse())
            {
                await TryRollbackAsync(() => FileSharingRetry.MoveAsync(transfer.TargetPath, transfer.SourcePath), rollbackErrors);
            }
            foreach (var retired in retiredVideoSources.AsEnumerable().Reverse())
            {
                await TryRollbackAsync(() => FileSharingRetry.MoveAsync(retired.BackupPath, retired.OriginalPath), rollbackErrors);
            }
            foreach (var targetPath in committedVideoTargets.AsEnumerable().Reverse())
            {
                var matchingSource = plan.VideoTransfers.First(transfer =>
                    PathsEqual(transfer.TargetPath, targetPath)).SourcePath;
                if (File.Exists(matchingSource))
                {
                    await TryRollbackAsync(() => FileSharingRetry.DeleteAsync(targetPath), rollbackErrors);
                }
                else
                {
                    rollbackErrors.Add(new IOException($"来源分段尚未恢复，因此保留目标分段：{targetPath}"));
                }
            }
            foreach (var outputPath in committedOutputs.AsEnumerable().Reverse())
            {
                await TryRollbackAsync(() => FileSharingRetry.DeleteAsync(outputPath), rollbackErrors);
            }
            foreach (var backup in backups.AsEnumerable().Reverse())
            {
                await TryRollbackAsync(async () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalPath)!);
                    await FileSharingRetry.MoveAsync(backup.BackupPath, backup.OriginalPath);
                }, rollbackErrors);
            }

            rollbackSucceeded = rollbackErrors.Count == 0;
            if (!rollbackSucceeded)
            {
                var recoveryPaths = crossVolumeCopy
                    ? $"{sourceStagingRoot}；{targetStagingRoot}"
                    : sourceStagingRoot;
                throw new IOException(
                    $"多分段保存失败且自动恢复不完整。请保留现场并检查：{recoveryPaths}",
                    new AggregateException(new[] { exception }.Concat(rollbackErrors)));
            }
            AppLog.Info("多分段文件恢复完成，原影片和 sidecar 保持不变");
            throw;
        }
        finally
        {
            timing.Begin("cleanup");
            if (operationSucceeded)
                TryRemoveMigratedExtrafanartDirectory(plan);
            if (rollbackSucceeded)
            {
                TryDeleteDirectory(sourceStagingRoot);
                if (!PathsEqual(sourceStagingRoot, targetStagingRoot))
                {
                    TryDeleteDirectory(targetStagingRoot);
                }
                if (!operationSucceeded && createdTargetDirectory)
                {
                    TryDeleteEmptyTree(plan.TargetDirectory);
                }
            }
        }
    }

    private static void TryRemoveMigratedExtrafanartDirectory(SavePlan plan)
    {
        var sourceDirectory = Path.GetDirectoryName(plan.SourceVideoPath)!;
        if (PathsEqual(sourceDirectory, plan.TargetDirectory)) return;
        var directory = Path.Combine(sourceDirectory, "extrafanart");
        // Only clean the directory involved in this successful transaction, never its parent.
        if (!plan.SourcePathsToRetire.Any(path => PathsEqual(Path.GetDirectoryName(path)!, directory))) return;
        try
        {
            if (!Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0 ||
                Directory.EnumerateFileSystemEntries(directory).Any()) return;
            // A concurrent new entry makes this fail safely; never recursively delete.
            Directory.Delete(directory, recursive: false);
            AppLog.Info($"已清理迁移后的空剧照目录：{directory}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warning($"保存已完成，无法清理空剧照目录：{directory}", exception);
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
        string suffix,
        OutputNamingMode namingMode,
        string folderBaseName) =>
        sourcePath is null
            ? null
            : Path.Combine(
                targetDirectory,
                (namingMode is OutputNamingMode.MovieFolder
                    ? folderBaseName
                    : targetBaseName + suffix) + Path.GetExtension(sourcePath).ToLowerInvariant());

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
        OutputService.GetExpectedOutputFiles(
                plan.OutputAnchorPath,
                metadata,
                plan.OutputGenerationOptions,
                plan.OutputNamingMode,
                OutputService.SelectScreenshotLocations(plan.ExtrafanartSourceLocations ?? metadata.ScreenshotUrls).Count,
                plan.OutputFileNames)
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

    private static void EnsureTargetCapacity(string sourceVideoPath, string targetDirectory)
    {
        EnsureTargetCapacity([sourceVideoPath], targetDirectory);
    }

    private static void EnsureTargetCapacity(IEnumerable<string> sourceVideoPaths, string targetDirectory)
    {
        const long metadataReserve = 32L * 1024 * 1024;
        var targetRoot = Path.GetPathRoot(Path.GetFullPath(targetDirectory));
        if (string.IsNullOrWhiteSpace(targetRoot) || targetRoot.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var drive = new DriveInfo(targetRoot);
            if (!drive.IsReady)
            {
                throw new IOException($"目标磁盘当前不可用：{targetRoot}");
            }

            var videoLength = sourceVideoPaths.Aggregate(0L, (total, sourcePath) =>
            {
                var length = new FileInfo(sourcePath).Length;
                return total > long.MaxValue - length ? long.MaxValue : total + length;
            });
            var required = videoLength > long.MaxValue - metadataReserve
                ? long.MaxValue
                : videoLength + metadataReserve;
            if (drive.AvailableFreeSpace < required)
            {
                throw new IOException(
                    $"目标磁盘空间不足。至少需要约 {required / 1024d / 1024d:F0} MB，" +
                    $"当前可用 {drive.AvailableFreeSpace / 1024d / 1024d:F0} MB：{targetRoot}");
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warning($"无法预估目标磁盘空间，将由写入事务继续验证：{targetRoot}", exception);
        }
    }

    private static async Task<string?> CopyMovieAsync(
        string sourcePath,
        string destinationPath,
        bool computeSha256,
        IProgress<FileTransactionProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = computeSha256
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
        var buffer = GC.AllocateUninitializedArray<byte>(bufferSize);
        var totalBytes = source.Length;
        long copiedBytes = 0;
        var lastPercentage = -1;
        ReportProgress(FileTransactionStage.CopyingMovie, "正在复制影片到目标临时区…", 0, totalBytes, destinationPath);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash?.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copiedBytes += read;
            var percentage = totalBytes <= 0 ? 100 : (int)(copiedBytes * 100L / totalBytes);
            if (percentage != lastPercentage)
            {
                lastPercentage = percentage;
                ReportProgress(
                    FileTransactionStage.CopyingMovie,
                    $"正在复制影片到目标临时区… {percentage}%",
                    copiedBytes,
                    totalBytes,
                    destinationPath);
            }
        }

        await destination.FlushAsync(cancellationToken);
        return hash is null ? null : Convert.ToHexString(hash.GetHashAndReset());

        void ReportProgress(
            FileTransactionStage stage,
            string message,
            long bytesProcessed,
            long bytesTotal,
            string temporaryPath) =>
            progress?.Report(new FileTransactionProgress(
                stage,
                message,
                bytesProcessed,
                bytesTotal,
                temporaryPath));
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        IProgress<FileTransactionProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = GC.AllocateUninitializedArray<byte>(bufferSize);
        var totalBytes = stream.Length;
        long processedBytes = 0;
        var lastPercentage = -1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            processedBytes += read;
            var percentage = totalBytes <= 0 ? 100 : (int)(processedBytes * 100L / totalBytes);
            if (percentage != lastPercentage)
            {
                lastPercentage = percentage;
                progress?.Report(new FileTransactionProgress(
                    FileTransactionStage.VerifyingMovie,
                    $"正在校验目标影片 SHA-256… {percentage}%",
                    processedBytes,
                    totalBytes,
                    path));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool HasOutputs(SaveOptions options) =>
        options.WriteNfo || options.DownloadPoster || options.DownloadFanart || options.DownloadExtrafanart;

    private static async Task<List<Exception>> RollbackAsync(
        SavePlan plan,
        bool videoMoved,
        bool verifiedCopy,
        bool targetVideoCommitted,
        bool sourceVideoRetired,
        string? sourceVideoBackupPath,
        IReadOnlyList<string> committedOutputs,
        IReadOnlyList<(string BackupPath, string OriginalPath)> backups)
    {
        var errors = new List<Exception>();
        if (verifiedCopy)
        {
            if (sourceVideoRetired)
            {
                if (sourceVideoBackupPath is null || !File.Exists(sourceVideoBackupPath))
                {
                    errors.Add(new IOException("无法找到来源影片的事务备份，已保留目标影片。"));
                }
                else
                {
                    await TryRollbackAsync(() => FileSharingRetry.MoveAsync(sourceVideoBackupPath, plan.SourceVideoPath), errors);
                }
            }

            if (targetVideoCommitted)
            {
                if (File.Exists(plan.SourceVideoPath))
                {
                    await TryRollbackAsync(() => FileSharingRetry.DeleteAsync(plan.TargetVideoPath), errors);
                }
                else
                {
                    errors.Add(new IOException("来源影片尚未恢复，因此保留已校验的目标影片。"));
                }
            }
        }
        else if (videoMoved)
        {
            await TryRollbackAsync(() => FileSharingRetry.MoveAsync(plan.TargetVideoPath, plan.SourceVideoPath), errors);
        }
        foreach (var outputPath in committedOutputs.Reverse())
        {
            await TryRollbackAsync(() => FileSharingRetry.DeleteAsync(outputPath), errors);
        }
        foreach (var backup in backups.Reverse())
        {
            await TryRollbackAsync(async () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalPath)!);
                await FileSharingRetry.MoveAsync(backup.BackupPath, backup.OriginalPath);
            }, errors);
        }
        return errors;
    }

    // Recovery deliberately does not inherit the canceled foreground-operation token.
    private static async Task TryRollbackAsync(Func<Task> action, ICollection<Exception> errors)
    {
        try
        {
            await action();
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
