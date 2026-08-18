using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Xml.Linq;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using SaveOptions = JavMetaLite.Core.Models.SaveOptions;

namespace JavMetaLite.RegressionTests;

internal static class FileOrganizationRegressionTests
{
    private static readonly byte[] VideoBytes = [0x4A, 0x41, 0x56, 0x4D, 0x45, 0x54, 0x41];

    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("layout", "四种文件夹与重命名组合", TestOrganizationMatrix),
        new("layout", "当前文件夹已经是番号时不重复嵌套", TestExistingNumberFolder),
        new("layout", "Unicode 文件名与支持的扩展名保持有效", TestUnicodeAndExtensions),
        new("target", "三种目标模式与独立影片重命名得到稳定路径", TestCustomTargetPlanning),
        new("target", "同卷自定义根目录安全执行并保持影片字节", TestSameVolumeCustomTargetExecution),
        new("target", "自定义根目录校验、冲突、跨盘符与 UNC 规划", TestCustomTargetValidation),
        new("transfer", "安全复制、SHA-256 校验并在提交后移除来源", TestVerifiedCopySuccess),
        new("transfer", "复制期间取消会保留来源并清理目标", TestVerifiedCopyCancellation),
        new("transfer", "SHA-256 不一致会拒绝提交并保留来源", TestVerifiedCopyHashMismatch),
        new("transfer", "复制后出现目标影片冲突会保留两边文件", TestVerifiedCopyLateTargetConflict),
        new("transfer", "目标提交后来源锁定会回滚目标", TestVerifiedCopyLateSourceFailure),
        new("overwrite", "预览模式拒绝覆盖，直接模式允许覆盖", TestOverwritePolicy),
        new("roundtrip", "生成变更预览后取消保持全部文件零写入", TestPreviewCancellationIsPure),
        new("roundtrip", "已有 NFO 无变化零写入，修改后保留未知 XML", TestRoundTripUpdate),
        new("roundtrip", "整理时迁移并重命名已知 sidecar", TestRoundTripOrganization),
        new("conflict", "载入后的 NFO 被外部修改时拒绝保存", TestRoundTripExternalChange),
        new("conflict", "影片冲突即使允许覆盖也必须阻止", TestMovieConflict),
        new("conflict", "计划生成后出现 metadata 冲突必须重新检测", TestConflictAfterPlanning),
        new("rollback", "metadata 提交中断时恢复旧文件", TestLockedMetadataRollback),
        new("rollback", "影片移动失败时删除新输出并恢复现场", TestLockedVideoRollback),
        new("validation", "无输出、无番号和文件占位目标均被拒绝", TestInvalidPlans)
    ];

    private static async Task TestOrganizationMatrix()
    {
        var scenarios = new[]
        {
            new LayoutScenario(false, false, false, "下载名 @ IPX-123-UC.MKV", "下载名 @ IPX-123-UC"),
            new LayoutScenario(true, false, true, "下载名 @ IPX-123-UC.MKV", "下载名 @ IPX-123-UC"),
            new LayoutScenario(false, true, true, "下载名 @ IPX-123-UC.MKV", "IPX-123"),
            new LayoutScenario(true, true, true, "下载名 @ IPX-123-UC.MKV", "IPX-123")
        };

        foreach (var scenario in scenarios)
        {
            using var workspace = new TestWorkspace($"matrix-{scenario.CreateFolder}-{scenario.RenameVideo}");
            var sourceDirectory = workspace.CreateDirectory("incoming");
            var sourcePath = Path.Combine(sourceDirectory, scenario.SourceName);
            await File.WriteAllBytesAsync(sourcePath, VideoBytes);
            var originalHash = AssertEx.Sha256(sourcePath);
            var unrelatedPath = workspace.WriteFile("incoming/keep-me.txt", [0x11, 0x22]);
            var unrelatedHash = AssertEx.Sha256(unrelatedPath);
            var metadata = Metadata("ipx-123", "布局矩阵");
            var options = NfoOnly();
            var organization = new OrganizationOptions(scenario.CreateFolder, scenario.RenameVideo);
            var plan = FileOrganizationService.BuildPlan(sourcePath, metadata, options, organization);
            var expectedDirectory = scenario.CreateFolder
                ? Path.Combine(sourceDirectory, "IPX-123")
                : sourceDirectory;
            var expectedVideo = Path.Combine(
                expectedDirectory,
                scenario.ExpectedBaseName + Path.GetExtension(sourcePath));

            AssertEx.Equal(expectedDirectory, plan.TargetDirectory);
            AssertEx.Equal(expectedVideo, plan.TargetVideoPath);
            AssertEx.Equal(scenario.VideoMoves, plan.VideoWillMove);

            using var outputService = new OutputService();
            var result = await new FileOrganizationService(outputService)
                .ExecuteAsync(plan, metadata, false);

            AssertEx.FileExists(expectedVideo);
            AssertEx.Equal(originalHash, AssertEx.Sha256(expectedVideo), "The movie bytes changed during organization.");
            AssertEx.FileExists(Path.Combine(expectedDirectory, $"{scenario.ExpectedBaseName}.nfo"));
            AssertEx.Equal(unrelatedHash, AssertEx.Sha256(unrelatedPath), "An unrelated file was changed.");
            AssertEx.Equal(scenario.VideoMoves, result.VideoMoved);
            if (scenario.VideoMoves)
            {
                AssertEx.FileDoesNotExist(sourcePath);
            }
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static async Task TestExistingNumberFolder()
    {
        using var workspace = new TestWorkspace("existing-number-folder");
        var sourceDirectory = workspace.CreateDirectory("ipx-123");
        var sourcePath = Path.Combine(sourceDirectory, "downloaded.mp4");
        await File.WriteAllBytesAsync(sourcePath, VideoBytes);
        var metadata = Metadata("IPX-123", "不重复嵌套");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(true, true));

        AssertEx.Equal(sourceDirectory, plan.TargetDirectory);
        AssertEx.False(
            plan.Changes.Any(change => change.Kind == PlannedChangeKind.CreateFolder),
            "A duplicate number folder was planned.");

        using var outputService = new OutputService();
        var result = await new FileOrganizationService(outputService)
            .ExecuteAsync(plan, metadata, false);
        AssertEx.Equal(Path.Combine(sourceDirectory, "IPX-123.mp4"), result.VideoPath);
        AssertEx.False(
            Directory.Exists(Path.Combine(sourceDirectory, "IPX-123")),
            "A nested IPX-123 folder was created.");
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestUnicodeAndExtensions()
    {
        foreach (var extension in new[] { ".mp4", ".MKV", ".avi", ".WMV" })
        {
            using var workspace = new TestWorkspace($"extension-{extension.TrimStart('.')}");
            var sourceDirectory = workspace.CreateDirectory("日文 中文 空格");
            var sourcePath = Path.Combine(sourceDirectory, $"样片 @ IPX-123-UC{extension}");
            await File.WriteAllBytesAsync(sourcePath, VideoBytes);
            var originalHash = AssertEx.Sha256(sourcePath);
            var metadata = Metadata("IPX-123", "扩展名测试");
            var plan = FileOrganizationService.BuildPlan(
                sourcePath,
                metadata,
                NfoOnly(),
                new OrganizationOptions(true, true));

            using var outputService = new OutputService();
            var result = await new FileOrganizationService(outputService)
                .ExecuteAsync(plan, metadata, false);
            AssertEx.Equal(extension, Path.GetExtension(result.VideoPath));
            AssertEx.FileExists(result.VideoPath);
            AssertEx.Equal(originalHash, AssertEx.Sha256(result.VideoPath));
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static Task TestCustomTargetPlanning()
    {
        using var workspace = new TestWorkspace("custom-target-planning");
        var sourceDirectory = workspace.CreateDirectory("incoming");
        var sourcePath = workspace.WriteFile("incoming/download @ SNOS-255-UC.mkv", VideoBytes);
        var customRoot = workspace.PathOf("library");
        var metadata = Metadata("snos-255", "自定义目标路径");

        var keepPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(OrganizationTargetMode.VideoDirectory, false));
        AssertEx.Equal(sourceDirectory, keepPlan.TargetDirectory);
        AssertEx.Equal(Path.Combine(sourceDirectory, "download @ SNOS-255-UC.mkv"), keepPlan.TargetVideoPath);

        var sourceFolderPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(OrganizationTargetMode.SourceNumberFolder, false));
        AssertEx.Equal(Path.Combine(sourceDirectory, "SNOS-255"), sourceFolderPlan.TargetDirectory);
        AssertEx.Equal(
            Path.Combine(sourceDirectory, "SNOS-255", "download @ SNOS-255-UC.mkv"),
            sourceFolderPlan.TargetVideoPath);

        var customPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, false, customRoot));
        var expectedCustomDirectory = Path.Combine(customRoot, "SNOS-255");
        AssertEx.Equal(expectedCustomDirectory, customPlan.TargetDirectory);
        AssertEx.Equal(
            Path.Combine(expectedCustomDirectory, "download @ SNOS-255-UC.mkv"),
            customPlan.TargetVideoPath);
        AssertEx.True(customPlan.OrganizationOptions.CreateMovieFolder, "Custom target must create/use a number folder.");
        AssertEx.True(customPlan.OrganizationOptions.UsesCustomRoot, "Custom target mode was not preserved in the plan.");

        var renamePlan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, customRoot));
        AssertEx.Equal(Path.Combine(expectedCustomDirectory, "SNOS-255.mkv"), renamePlan.TargetVideoPath);

        var selectedNumberDirectory = Path.Combine(customRoot, "snos-255");
        var alreadySelectedPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(
                OrganizationTargetMode.CustomRootNumberFolder,
                true,
                selectedNumberDirectory + Path.DirectorySeparatorChar));
        AssertEx.Equal(selectedNumberDirectory, alreadySelectedPlan.TargetDirectory);
        AssertEx.False(
            alreadySelectedPlan.TargetDirectory.EndsWith(
                Path.Combine("snos-255", "SNOS-255"),
                StringComparison.OrdinalIgnoreCase),
            "An already selected number directory was nested again.");

        AssertEx.False(Directory.Exists(expectedCustomDirectory), "Path planning unexpectedly created the target directory.");
        AssertEx.FileExists(sourcePath);
        workspace.AssertNoTemporaryArtifacts();
        return Task.CompletedTask;
    }

    private static Task TestCustomTargetValidation()
    {
        using var workspace = new TestWorkspace("custom-target-validation");
        var sourcePath = workspace.WriteFile("incoming/source.mp4", VideoBytes);

        AssertEx.Throws<InvalidOperationException>(
            () => OrganizationPathPlanner.Resolve(
                sourcePath,
                "IPX-888",
                new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, false)),
            "A blank custom root was accepted.");
        AssertEx.Throws<InvalidOperationException>(
            () => OrganizationPathPlanner.Resolve(
                sourcePath,
                "IPX-888",
                new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, false, "relative/library")),
            "A relative custom root was accepted.");
        AssertEx.Throws<InvalidOperationException>(
            () => OrganizationPathPlanner.Resolve(
                sourcePath,
                "IPX/888",
                new OrganizationOptions(
                    OrganizationTargetMode.CustomRootNumberFolder,
                    false,
                    workspace.PathOf("library"))),
            "An invalid movie number was accepted for a custom target.");
        if (OperatingSystem.IsWindows())
        {
            AssertEx.Throws<InvalidOperationException>(
                () => OrganizationPathPlanner.Resolve(
                    sourcePath,
                    "IPX-888",
                    new OrganizationOptions(
                        OrganizationTargetMode.CustomRootNumberFolder,
                        false,
                        @"C:\Media\Bad*Root")),
                "A custom root with an invalid directory segment was accepted.");
        }

        var occupiedRoot = workspace.WriteFile("occupied-root", [0x01]);
        var occupiedPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            Metadata("IPX-888", "根目录占位"),
            NfoOnly(),
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, false, occupiedRoot));
        AssertEx.True(
            occupiedPlan.BlockingConflicts.Any(conflict => conflict.Contains("自定义目标根目录", StringComparison.Ordinal)),
            "A file occupying the custom root was not reported as a blocking conflict.");

        var customRoot = workspace.CreateDirectory("library");
        var targetDirectory = workspace.CreateDirectory("library", "IPX-888");
        var existingTargetVideo = workspace.WriteFile("library/IPX-888/IPX-888.mp4", [0x09]);
        var movieConflictPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            Metadata("IPX-888", "影片冲突"),
            NfoOnly(overwrite: true),
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, customRoot));
        AssertEx.Equal(targetDirectory, movieConflictPlan.TargetDirectory);
        AssertEx.Equal(existingTargetVideo, movieConflictPlan.TargetVideoPath);
        AssertEx.True(movieConflictPlan.HasBlockingConflicts, "An existing target movie was not blocked.");

        if (OperatingSystem.IsWindows())
        {
            var crossDrive = OrganizationPathPlanner.Resolve(
                sourcePath,
                "IPX-888",
                new OrganizationOptions(
                    OrganizationTargetMode.CustomRootNumberFolder,
                    true,
                    @"Z:\Jellyfin\Movies"));
            AssertEx.Equal(@"Z:\Jellyfin\Movies\IPX-888", crossDrive.TargetDirectory);
            AssertEx.Equal(@"Z:\Jellyfin\Movies\IPX-888\IPX-888.mp4", crossDrive.TargetVideoPath);
            AssertEx.True(
                crossDrive.RequiresVerifiedCopy,
                "A different-drive target did not select the verified-copy transaction.");

            var unc = OrganizationPathPlanner.Resolve(
                sourcePath,
                "IPX-888",
                new OrganizationOptions(
                    OrganizationTargetMode.CustomRootNumberFolder,
                    false,
                    @"\\JavMetaLiteTest\Media"));
            AssertEx.Equal(@"\\JavMetaLiteTest\Media\IPX-888", unc.TargetDirectory);
            AssertEx.Equal(
                @"\\JavMetaLiteTest\Media\IPX-888\source.mp4",
                unc.TargetVideoPath);
            AssertEx.True(
                unc.RequiresVerifiedCopy,
                "A UNC target did not select the verified-copy transaction.");
        }

        AssertEx.FileExists(sourcePath);
        workspace.AssertNoTemporaryArtifacts();
        return Task.CompletedTask;
    }

    private static async Task TestVerifiedCopySuccess()
    {
        using var workspace = new TestWorkspace("verified-copy-success");
        var sourceBytes = Enumerable.Range(0, 3 * 1024 * 1024 + 317)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var sourcePath = workspace.WriteFile("incoming/cross-source.mkv", sourceBytes);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var customRoot = workspace.CreateDirectory("library");
        var metadata = Metadata("IPX-890", "安全复制成功");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, customRoot)) with
        {
            RequiresVerifiedVideoCopy = true
        };
        var stages = new List<FileTransactionStage>();
        var progress = new InlineProgress<FileTransactionProgress>(update => stages.Add(update.Stage));

        using var outputService = new OutputService();
        var result = await new FileOrganizationService(outputService)
            .ExecuteAsync(plan, metadata, false, CancellationToken.None, progress);

        AssertEx.FileDoesNotExist(sourcePath);
        AssertEx.FileExists(result.VideoPath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(result.VideoPath));
        AssertEx.True(stages.Contains(FileTransactionStage.CopyingMovie), "Movie copy progress was not reported.");
        AssertEx.True(stages.Contains(FileTransactionStage.VerifyingMovie), "Movie verification progress was not reported.");
        AssertEx.True(stages.Contains(FileTransactionStage.RetiringSource), "Source retirement was not reported.");
        AssertEx.True(stages.Contains(FileTransactionStage.Completed), "Transaction completion was not reported.");
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestVerifiedCopyCancellation()
    {
        using var workspace = new TestWorkspace("verified-copy-cancel");
        var sourcePath = workspace.WriteFile("incoming/cancel-source.mp4", new byte[3 * 1024 * 1024]);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var metadata = Metadata("IPX-891", "复制取消");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(
                OrganizationTargetMode.CustomRootNumberFolder,
                true,
                workspace.CreateDirectory("library"))) with
        {
            RequiresVerifiedVideoCopy = true
        };
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<FileTransactionProgress>(update =>
        {
            if (update.Stage == FileTransactionStage.CopyingMovie && update.BytesProcessed > 0)
            {
                cancellation.Cancel();
            }
        });

        using var outputService = new OutputService();
        await AssertEx.ThrowsAsync<OperationCanceledException>(
            () => new FileOrganizationService(outputService)
                .ExecuteAsync(plan, metadata, false, cancellation.Token, progress),
            "Cancelling a verified copy did not stop the transaction.");

        AssertEx.FileExists(sourcePath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(sourcePath));
        AssertEx.FileDoesNotExist(plan.TargetVideoPath);
        AssertEx.FileDoesNotExist(Path.Combine(plan.TargetDirectory, "IPX-891.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestVerifiedCopyHashMismatch()
    {
        using var workspace = new TestWorkspace("verified-copy-hash-mismatch");
        var sourcePath = workspace.WriteFile("incoming/hash-source.mp4", new byte[2 * 1024 * 1024]);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var metadata = Metadata("IPX-892", "校验失败");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(
                OrganizationTargetMode.CustomRootNumberFolder,
                true,
                workspace.CreateDirectory("library"))) with
        {
            RequiresVerifiedVideoCopy = true
        };
        var corrupted = false;
        var progress = new InlineProgress<FileTransactionProgress>(update =>
        {
            if (!corrupted && update.Stage == FileTransactionStage.VerifyingMovie &&
                update.BytesProcessed == 0 && update.TemporaryPath is not null)
            {
                using var stream = new FileStream(update.TemporaryPath, FileMode.Open, FileAccess.Write, FileShare.Read);
                stream.WriteByte(0x7F);
                corrupted = true;
            }
        });

        using var outputService = new OutputService();
        var exception = await AssertEx.ThrowsAsync<IOException>(
            () => new FileOrganizationService(outputService)
                .ExecuteAsync(plan, metadata, false, CancellationToken.None, progress),
            "A corrupted target copy was committed.");

        AssertEx.True(exception.Message.Contains("SHA-256", StringComparison.Ordinal), "Hash mismatch was not reported.");
        AssertEx.FileExists(sourcePath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(sourcePath));
        AssertEx.FileDoesNotExist(plan.TargetVideoPath);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestVerifiedCopyLateSourceFailure()
    {
        using var workspace = new TestWorkspace("verified-copy-late-source-failure");
        var sourcePath = workspace.WriteFile("incoming/locked-source.mp4", new byte[2 * 1024 * 1024]);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var metadata = Metadata("IPX-893", "来源移除失败");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(
                OrganizationTargetMode.CustomRootNumberFolder,
                true,
                workspace.CreateDirectory("library"))) with
        {
            RequiresVerifiedVideoCopy = true
        };
        FileStream? sourceLock = null;
        var progress = new InlineProgress<FileTransactionProgress>(update =>
        {
            if (sourceLock is null && update.Stage == FileTransactionStage.RetiringSource)
            {
                sourceLock = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
        });

        using var outputService = new OutputService();
        try
        {
            await AssertEx.ThrowsAsync<IOException>(
                () => new FileOrganizationService(outputService)
                    .ExecuteAsync(plan, metadata, false, CancellationToken.None, progress),
                "A locked source movie did not roll back the committed target.");
        }
        finally
        {
            sourceLock?.Dispose();
        }

        AssertEx.FileExists(sourcePath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(sourcePath));
        AssertEx.FileDoesNotExist(plan.TargetVideoPath);
        AssertEx.FileDoesNotExist(Path.Combine(plan.TargetDirectory, "IPX-893.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestVerifiedCopyLateTargetConflict()
    {
        using var workspace = new TestWorkspace("verified-copy-late-target-conflict");
        var sourcePath = workspace.WriteFile("incoming/late-conflict-source.mp4", new byte[2 * 1024 * 1024]);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var metadata = Metadata("IPX-894", "晚到目标冲突");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(
                OrganizationTargetMode.CustomRootNumberFolder,
                true,
                workspace.CreateDirectory("library"))) with
        {
            RequiresVerifiedVideoCopy = true
        };
        var conflictBytes = new byte[] { 0x43, 0x4F, 0x4E, 0x46, 0x4C, 0x49, 0x43, 0x54 };
        var conflictCreated = false;
        var progress = new InlineProgress<FileTransactionProgress>(update =>
        {
            if (!conflictCreated && update.Stage == FileTransactionStage.VerifyingMovie &&
                update.BytesProcessed == 0)
            {
                Directory.CreateDirectory(plan.TargetDirectory);
                File.WriteAllBytes(plan.TargetVideoPath, conflictBytes);
                conflictCreated = true;
            }
        });

        using var outputService = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(
            () => new FileOrganizationService(outputService)
                .ExecuteAsync(plan, metadata, false, CancellationToken.None, progress),
            "A target movie created after planning was overwritten.");

        AssertEx.FileExists(sourcePath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(sourcePath));
        AssertEx.FileExists(plan.TargetVideoPath);
        AssertEx.Equal(Convert.ToHexString(SHA256.HashData(conflictBytes)), AssertEx.Sha256(plan.TargetVideoPath));
        AssertEx.FileDoesNotExist(Path.Combine(plan.TargetDirectory, "IPX-894.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestSameVolumeCustomTargetExecution()
    {
        using var workspace = new TestWorkspace("same-volume-custom-target");
        var sourcePath = workspace.WriteFile("incoming/download @ IPX-889-UC.mkv", VideoBytes);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var customRoot = workspace.CreateDirectory("library");
        var metadata = Metadata("IPX-889", "同卷自定义目标");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(
                OrganizationTargetMode.CustomRootNumberFolder,
                true,
                customRoot));

        AssertEx.False(plan.HasBlockingConflicts, "A same-volume custom target was unexpectedly blocked.");
        AssertEx.Equal(Path.Combine(customRoot, "IPX-889", "IPX-889.mkv"), plan.TargetVideoPath);

        using var outputService = new OutputService();
        var result = await new FileOrganizationService(outputService)
            .ExecuteAsync(plan, metadata, false);

        AssertEx.Equal(plan.TargetVideoPath, result.VideoPath);
        AssertEx.True(result.VideoMoved, "The movie was not moved to the same-volume custom target.");
        AssertEx.FileDoesNotExist(sourcePath);
        AssertEx.FileExists(result.VideoPath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(result.VideoPath), "The movie bytes changed during custom organization.");
        AssertEx.FileExists(Path.Combine(customRoot, "IPX-889", "IPX-889.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestOverwritePolicy()
    {
        using var workspace = new TestWorkspace("overwrite-policy");
        var sourcePath = workspace.WriteFile("IPX-321.mp4", VideoBytes);
        var previewOptions = NfoOnly();
        AssertEx.True(previewOptions.RequiresPreview, "Safe default must require preview.");
        var initialMetadata = Metadata("IPX-321", "旧标题");
        using var outputService = new OutputService();
        var organizer = new FileOrganizationService(outputService);
        var initialPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            initialMetadata,
            previewOptions,
            new OrganizationOptions(false, false));
        await organizer.ExecuteAsync(initialPlan, initialMetadata, false);

        var nfoPath = workspace.PathOf("IPX-321.nfo");
        var oldHash = AssertEx.Sha256(nfoPath);
        var changedMetadata = Metadata("IPX-321", "新标题");
        var refusedPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            changedMetadata,
            previewOptions,
            new OrganizationOptions(false, false));
        AssertEx.Equal(1, refusedPlan.OverwriteConflicts.Count);
        await AssertEx.ThrowsAsync<IOException>(
            () => organizer.ExecuteAsync(refusedPlan, changedMetadata, false),
            "Preview mode unexpectedly overwrote metadata.");
        AssertEx.Equal(oldHash, AssertEx.Sha256(nfoPath), "Refused overwrite changed the original NFO.");

        var directOptions = NfoOnly(overwrite: true);
        AssertEx.False(directOptions.RequiresPreview, "Direct overwrite mode should skip preview.");
        var directPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            changedMetadata,
            directOptions,
            new OrganizationOptions(false, false));
        await organizer.ExecuteAsync(directPlan, changedMetadata, true);
        AssertEx.Equal("新标题", XDocument.Load(nfoPath).Root?.Element("title")?.Value);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestRoundTripUpdate()
    {
        using var workspace = new TestWorkspace("roundtrip-update");
        var sourcePath = workspace.WriteFile("SNOS-255.mp4", VideoBytes);
        var nfoPath = workspace.PathOf("SNOS-255.nfo");
        await File.WriteAllTextAsync(nfoPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <!--roundtrip-comment-->
            <movie custom="keep-root">
              <title>旧标题</title>
              <id>SNOS-255</id>
              <uniqueid type="jav" default="true">snos00255</uniqueid>
              <thumb aspect="poster">SNOS-255-poster.jpg</thumb>
              <fanart><thumb>SNOS-255-fanart.jpg</thumb></fanart>
              <unknown answer="42"><child>keep</child></unknown>
            </movie>
            """);
        var posterPath = workspace.WriteFile("SNOS-255-poster.jpg", TestImageFactory.CreateJpeg(420, 600));
        var fanartPath = workspace.WriteFile("SNOS-255-fanart.jpg", TestImageFactory.CreateJpeg(800, 538));
        var movieHash = AssertEx.Sha256(sourcePath);
        var nfoHash = AssertEx.Sha256(nfoPath);
        var posterHash = AssertEx.Sha256(posterPath);
        var fanartHash = AssertEx.Sha256(fanartPath);
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(sourcePath));
        var editable = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;
        var localArtwork = ArtworkCoverCandidate.CreateSidecarPair(
            new MetadataCandidateSource("local-images", "本地图片", workspace.Root),
            posterPath,
            fanartPath);
        var context = new LocalSaveContext(bundle, localArtwork, localArtwork);
        var options = new SaveOptions(true, true, true, false, false);
        var noChangePlan = FileOrganizationService.BuildPlan(
            sourcePath,
            editable,
            options,
            new OrganizationOptions(false, false),
            context);

        AssertEx.False(noChangePlan.HasActualChanges, "An untouched local bundle was planned as a write.");
        AssertEx.False(noChangePlan.OutputGenerationOptions.WriteNfo, "Unchanged NFO should not be regenerated.");
        AssertEx.Equal(0, noChangePlan.OverwriteConflicts.Count);
        AssertEx.Equal(3, noChangePlan.Changes.Count(change => change.Kind == PlannedChangeKind.KeepFile));
        using var outputService = new OutputService();
        var organizer = new FileOrganizationService(outputService);
        await organizer.ExecuteAsync(noChangePlan, editable, false);
        AssertEx.Equal(nfoHash, AssertEx.Sha256(nfoPath), "No-op save rewrote the NFO.");
        AssertEx.Equal(posterHash, AssertEx.Sha256(posterPath));
        AssertEx.Equal(fanartHash, AssertEx.Sha256(fanartPath));

        editable.Title = "更新后的标题";
        editable.Plot = "新增简介";
        var updatePlan = FileOrganizationService.BuildPlan(
            sourcePath,
            editable,
            options,
            new OrganizationOptions(false, false),
            context);
        AssertEx.True(updatePlan.HasActualChanges, "Edited NFO was not planned as an update.");
        AssertEx.True(
            updatePlan.Changes.Any(change => change.Kind == PlannedChangeKind.UpdateFile),
            "The NFO update was not classified as UpdateFile.");
        AssertEx.Equal(1, updatePlan.OverwriteConflicts.Count);
        await organizer.ExecuteAsync(updatePlan, editable, true);

        var updated = XDocument.Load(nfoPath, LoadOptions.PreserveWhitespace);
        AssertEx.Equal("更新后的标题", updated.Root?.Element("title")?.Value);
        AssertEx.Equal("新增简介", updated.Root?.Element("plot")?.Value);
        AssertEx.Equal("keep-root", updated.Root?.Attribute("custom")?.Value);
        AssertEx.Equal("42", updated.Root?.Element("unknown")?.Attribute("answer")?.Value);
        AssertEx.Equal("keep", updated.Root?.Element("unknown")?.Element("child")?.Value);
        AssertEx.True(updated.DescendantNodes().OfType<XComment>().Any(), "The original XML comment was lost.");
        AssertEx.Equal(movieHash, AssertEx.Sha256(sourcePath), "The movie changed during round-trip update.");
        AssertEx.Equal(posterHash, AssertEx.Sha256(posterPath), "Preserved poster bytes changed.");
        AssertEx.Equal(fanartHash, AssertEx.Sha256(fanartPath), "Preserved fanart bytes changed.");
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestPreviewCancellationIsPure()
    {
        using var workspace = new TestWorkspace("preview-cancel");
        var sourcePath = workspace.WriteFile("IPX-321.mp4", VideoBytes);
        var nfoPath = workspace.PathOf("IPX-321.nfo");
        await File.WriteAllTextAsync(
            nfoPath,
            "<movie custom=\"keep\"><title>取消前标题</title><id>IPX-321</id><unknown>untouched</unknown></movie>");
        var posterPath = workspace.WriteFile("IPX-321-poster.jpg", TestImageFactory.CreateJpeg(420, 600));
        var movieHash = AssertEx.Sha256(sourcePath);
        var nfoHash = AssertEx.Sha256(nfoPath);
        var posterHash = AssertEx.Sha256(posterPath);
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(sourcePath));
        var editable = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;
        editable.Title = "预览中的新标题";
        var localArtwork = ArtworkCoverCandidate.CreateSidecarPair(
            new MetadataCandidateSource("local-images", "本地图片", workspace.Root),
            posterPath,
            null);

        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            editable,
            new SaveOptions(true, true, true, false, false),
            new OrganizationOptions(true, true),
            new LocalSaveContext(bundle, localArtwork, localArtwork));

        AssertEx.True(plan.HasActualChanges, "The cancellation fixture did not produce a meaningful preview.");
        AssertEx.True(
            plan.Changes.Any(change => change.Kind == PlannedChangeKind.UpdateFile),
            "The cancellation preview did not contain an NFO update.");
        AssertEx.True(plan.VideoWillMove, "The cancellation preview did not contain the planned movie move.");
        // Cancellation is represented by not executing the immutable preview plan.
        AssertEx.Equal(movieHash, AssertEx.Sha256(sourcePath), "Planning changed the movie before confirmation.");
        AssertEx.Equal(nfoHash, AssertEx.Sha256(nfoPath), "Planning changed the NFO before confirmation.");
        AssertEx.Equal(posterHash, AssertEx.Sha256(posterPath), "Planning changed the poster before confirmation.");
        AssertEx.FileDoesNotExist(plan.TargetVideoPath);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestRoundTripOrganization()
    {
        using var workspace = new TestWorkspace("roundtrip-organization");
        var sourcePath = workspace.WriteFile("incoming/download @ IPX-850-UC.mkv", VideoBytes);
        var nfoPath = workspace.PathOf("incoming/download @ IPX-850-UC.nfo");
        await File.WriteAllTextAsync(nfoPath, """
            <movie custom="keep">
              <title>整理测试</title>
              <id>IPX-850</id>
              <thumb aspect="poster">download @ IPX-850-UC-poster.png</thumb>
              <fanart><thumb>download @ IPX-850-UC-fanart.jpg</thumb></fanart>
              <unknown>preserve</unknown>
            </movie>
            """);
        var posterPath = workspace.WriteFile(
            "incoming/download @ IPX-850-UC-poster.png",
            TestImageFactory.CreateJpeg(420, 600));
        var fanartPath = workspace.WriteFile(
            "incoming/download @ IPX-850-UC-fanart.jpg",
            TestImageFactory.CreateJpeg(800, 538));
        var movieHash = AssertEx.Sha256(sourcePath);
        var posterHash = AssertEx.Sha256(posterPath);
        var fanartHash = AssertEx.Sha256(fanartPath);
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(sourcePath));
        var editable = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;
        var localArtwork = ArtworkCoverCandidate.CreateSidecarPair(
            new MetadataCandidateSource("local-images", "本地图片", workspace.Root),
            posterPath,
            fanartPath);
        var context = new LocalSaveContext(bundle, localArtwork, localArtwork);
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            editable,
            new SaveOptions(true, true, true, false, false),
            new OrganizationOptions(true, true),
            context);

        AssertEx.True(plan.VideoWillMove, "Organization did not plan a movie move.");
        AssertEx.True(plan.OutputGenerationOptions.WriteNfo, "Renamed artwork references should update the NFO.");
        AssertEx.Equal(2, plan.SidecarTransfers.Count);
        AssertEx.Equal(3, plan.SourcePathsToRetire.Count);
        AssertEx.True(
            plan.Changes.Any(change => change.Kind == PlannedChangeKind.UpdateFile),
            "The migrated NFO was not classified as an update.");
        AssertEx.Equal(2, plan.Changes.Count(change => change.Kind == PlannedChangeKind.KeepFile));
        using var outputService = new OutputService();
        var result = await new FileOrganizationService(outputService).ExecuteAsync(plan, editable, false);

        var targetDirectory = workspace.PathOf("incoming", "IPX-850");
        var targetVideo = Path.Combine(targetDirectory, "IPX-850.mkv");
        var targetNfo = Path.Combine(targetDirectory, "IPX-850.nfo");
        var targetPoster = Path.Combine(targetDirectory, "IPX-850-poster.png");
        var targetFanart = Path.Combine(targetDirectory, "IPX-850-fanart.jpg");
        AssertEx.Equal(targetVideo, result.VideoPath);
        AssertEx.Equal(movieHash, AssertEx.Sha256(targetVideo));
        AssertEx.Equal(posterHash, AssertEx.Sha256(targetPoster));
        AssertEx.Equal(fanartHash, AssertEx.Sha256(targetFanart));
        AssertEx.Equal("preserve", XDocument.Load(targetNfo).Root?.Element("unknown")?.Value);
        AssertEx.Equal("IPX-850-poster.png", XDocument.Load(targetNfo).Root?.Elements("thumb")
            .Single(element => element.Attribute("aspect")?.Value == "poster").Value);
        AssertEx.Equal("IPX-850-fanart.jpg", XDocument.Load(targetNfo).Root?.Element("fanart")?.Element("thumb")?.Value);
        AssertEx.FileDoesNotExist(sourcePath);
        AssertEx.FileDoesNotExist(nfoPath);
        AssertEx.FileDoesNotExist(posterPath);
        AssertEx.FileDoesNotExist(fanartPath);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestRoundTripExternalChange()
    {
        using var workspace = new TestWorkspace("roundtrip-external-change");
        var sourcePath = workspace.WriteFile("IPX-654.mp4", VideoBytes);
        var nfoPath = workspace.PathOf("IPX-654.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>载入标题</title><id>IPX-654</id><unknown>keep</unknown></movie>");
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(sourcePath));
        var editable = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;
        editable.Title = "准备保存的标题";
        var context = new LocalSaveContext(bundle, null, null);
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            editable,
            NfoOnly(overwrite: true),
            new OrganizationOptions(false, false),
            context);
        await File.WriteAllTextAsync(nfoPath, "<movie><title>外部程序修改</title><id>IPX-654</id></movie>");
        var externalHash = AssertEx.Sha256(nfoPath);
        var movieHash = AssertEx.Sha256(sourcePath);
        using var outputService = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(
            () => new FileOrganizationService(outputService).ExecuteAsync(plan, editable, true),
            "An externally changed NFO was overwritten.");
        AssertEx.Equal(externalHash, AssertEx.Sha256(nfoPath));
        AssertEx.Equal(movieHash, AssertEx.Sha256(sourcePath));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestMovieConflict()
    {
        using var workspace = new TestWorkspace("movie-conflict");
        var sourcePath = workspace.WriteFile("incoming/source.mp4", VideoBytes);
        var targetPath = workspace.WriteFile("incoming/IPX-444/IPX-444.mp4", [0x99, 0x88]);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var targetHash = AssertEx.Sha256(targetPath);
        var metadata = Metadata("IPX-444", "冲突测试");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(overwrite: true),
            new OrganizationOptions(true, true));

        AssertEx.True(plan.HasBlockingConflicts, "The existing destination movie was not marked as blocking.");
        using var outputService = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(
            () => new FileOrganizationService(outputService).ExecuteAsync(plan, metadata, true),
            "Direct overwrite mode overwrote an existing movie.");
        AssertEx.Equal(sourceHash, AssertEx.Sha256(sourcePath));
        AssertEx.Equal(targetHash, AssertEx.Sha256(targetPath));
        AssertEx.FileDoesNotExist(workspace.PathOf("incoming", "IPX-444", "IPX-444.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestConflictAfterPlanning()
    {
        using var workspace = new TestWorkspace("late-conflict");
        var sourcePath = workspace.WriteFile("IPX-555.mp4", VideoBytes);
        var metadata = Metadata("IPX-555", "晚到冲突");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(false, false));
        AssertEx.Equal(0, plan.OverwriteConflicts.Count);

        var nfoPath = workspace.WriteFile("IPX-555.nfo", [0x4F, 0x4C, 0x44]);
        var conflictHash = AssertEx.Sha256(nfoPath);
        using var outputService = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(
            () => new FileOrganizationService(outputService).ExecuteAsync(plan, metadata, false),
            "A metadata conflict created after planning was not detected.");
        AssertEx.Equal(conflictHash, AssertEx.Sha256(nfoPath));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestLockedMetadataRollback()
    {
        using var workspace = new TestWorkspace("locked-metadata");
        var sourcePath = workspace.WriteFile("IPX-666.mp4", VideoBytes);
        var nfoPath = workspace.PathOf("IPX-666.nfo");
        await File.WriteAllTextAsync(
            nfoPath,
            "<movie custom=\"keep\"><title>旧标题</title><id>IPX-666</id><unknown>restore-me</unknown></movie>");
        var posterPath = workspace.WriteFile("IPX-666-poster.jpg", [0x4F, 0x4C, 0x44, 0x2D, 0x4A, 0x50, 0x47]);
        var nfoHash = AssertEx.Sha256(nfoPath);
        var posterHash = AssertEx.Sha256(posterPath);
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(sourcePath));
        var metadata = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;
        metadata.Title = "覆盖中断";
        metadata.CoverUrl = "https://images.example.test/cover.jpg";
        var localArtwork = ArtworkCoverCandidate.CreateSidecarPair(
            new MetadataCandidateSource("local-images", "本地图片", workspace.Root),
            posterPath,
            null);
        var onlineArtwork = ArtworkCoverCandidate.CreateCompleteCover(
            new MetadataCandidateSource("libredmm", "LibreDMM", "https://example.test/IPX-666"),
            metadata.CoverUrl);
        var context = new LocalSaveContext(bundle, localArtwork, onlineArtwork);
        var options = new SaveOptions(true, true, false, false, true);
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            options,
            new OrganizationOptions(false, false),
            context);
        using var httpClient = new HttpClient(new StaticImageHandler(TestImageFactory.CreateJpeg()));
        using var outputService = new OutputService(httpClient);
        var organizer = new FileOrganizationService(outputService);

        using (var lockedPoster = new FileStream(posterPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await AssertEx.ThrowsAsync<IOException>(
                () => organizer.ExecuteAsync(plan, metadata, true),
                "A locked metadata file did not interrupt the commit.");
        }

        AssertEx.Equal(nfoHash, AssertEx.Sha256(nfoPath), "The original NFO was not restored.");
        AssertEx.Equal(posterHash, AssertEx.Sha256(posterPath), "The locked poster changed.");
        AssertEx.FileExists(sourcePath);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestLockedVideoRollback()
    {
        using var workspace = new TestWorkspace("locked-video");
        var sourcePath = workspace.WriteFile("incoming/rollback-source.mp4", VideoBytes);
        var sourceHash = AssertEx.Sha256(sourcePath);
        var metadata = Metadata("IPX-777", "影片回滚");
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(),
            new OrganizationOptions(true, true));
        using var outputService = new OutputService();
        var organizer = new FileOrganizationService(outputService);

        using (var lockedVideo = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await AssertEx.ThrowsAsync<IOException>(
                () => organizer.ExecuteAsync(plan, metadata, false),
                "A locked movie did not interrupt the move.");
        }

        AssertEx.FileExists(sourcePath);
        AssertEx.Equal(sourceHash, AssertEx.Sha256(sourcePath));
        AssertEx.FileDoesNotExist(plan.TargetVideoPath);
        AssertEx.FileDoesNotExist(Path.Combine(plan.TargetDirectory, "IPX-777.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestInvalidPlans()
    {
        using var workspace = new TestWorkspace("invalid-plans");
        var sourcePath = workspace.WriteFile("incoming/source.mp4", VideoBytes);
        var noOutputs = new SaveOptions(false, false, false, false, false);
        AssertEx.Throws<InvalidOperationException>(
            () => FileOrganizationService.BuildPlan(
                sourcePath,
                Metadata("IPX-888", "无输出"),
                noOutputs,
                new OrganizationOptions(false, false)),
            "A plan without outputs was accepted.");
        AssertEx.Throws<InvalidOperationException>(
            () => FileOrganizationService.BuildPlan(
                sourcePath,
                Metadata(string.Empty, "无番号"),
                NfoOnly(),
                new OrganizationOptions(true, false)),
            "Organization without a movie number was accepted.");

        var occupiedTarget = workspace.WriteFile("incoming/IPX-888", [0x01]);
        var metadata = Metadata("IPX-888", "路径占位");
        var blockedPlan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            NfoOnly(overwrite: true),
            new OrganizationOptions(true, false));
        AssertEx.True(blockedPlan.HasBlockingConflicts, "A file occupying the target folder was not blocked.");
        using var outputService = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(
            () => new FileOrganizationService(outputService).ExecuteAsync(blockedPlan, metadata, true),
            "A file occupying the target folder did not prevent execution.");
        AssertEx.FileExists(sourcePath);
        AssertEx.FileExists(occupiedTarget);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static MovieMetadata Metadata(string id, string title) => new()
    {
        Id = id,
        Title = title,
        SourceName = "regression",
        SourceDisplayName = "Regression fixture"
    };

    private static SaveOptions NfoOnly(bool overwrite = false) =>
        new(true, false, false, false, overwrite);

    private sealed record LayoutScenario(
        bool CreateFolder,
        bool RenameVideo,
        bool VideoMoves,
        string SourceName,
        string ExpectedBaseName);
}
