using System.IO;
using System.Net.Http;
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
        new("overwrite", "预览模式拒绝覆盖，直接模式允许覆盖", TestOverwritePolicy),
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
        var nfoPath = workspace.WriteFile("IPX-666.nfo", [0x4F, 0x4C, 0x44, 0x2D, 0x4E, 0x46, 0x4F]);
        var posterPath = workspace.WriteFile("IPX-666-poster.jpg", [0x4F, 0x4C, 0x44, 0x2D, 0x4A, 0x50, 0x47]);
        var nfoHash = AssertEx.Sha256(nfoPath);
        var posterHash = AssertEx.Sha256(posterPath);
        var metadata = Metadata("IPX-666", "覆盖中断");
        metadata.CoverUrl = "https://images.example.test/cover.jpg";
        var options = new SaveOptions(true, true, false, false, true);
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            options,
            new OrganizationOptions(false, false));
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
