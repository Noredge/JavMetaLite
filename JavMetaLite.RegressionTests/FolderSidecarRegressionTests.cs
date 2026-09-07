using System.IO;
using System.Xml.Linq;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using SaveOptions = JavMetaLite.Core.Models.SaveOptions;

namespace JavMetaLite.RegressionTests;

internal static class FolderSidecarRegressionTests
{
    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("folder-sidecar", "Single movie folder fallback and filename priority", TestDiscovery),
        new("folder-sidecar", "CD group reads folder metadata but mixed movies do not", TestMultipartDiscovery),
        new("folder-sidecar", "In-place folder update preserves names and unknown XML", TestInPlace),
        new("folder-sidecar", "New directory retains standard output naming", TestNewDirectory),
        new("folder-sidecar", "Artwork replacement uses original PNG name and encoding", TestReplacement),
        new("folder-sidecar", "Missing artwork follows folder naming", TestMissingArtwork),
        new("folder-sidecar", "External NFO changes block folder update", TestExternalChange),
        new("folder-sidecar", "Both layouts coexist without rewriting the unselected NFO", TestBothLayouts),
        new("folder-sidecar", "Explicit num and cid identities remain distinct", TestContentId),
        new("folder-sidecar", "No-op folder save leaves bytes unchanged", TestNoOp),
        new("folder-sidecar", "Empty migrated still directory cleanup is success-only and nonrecursive", TestEmptyDirectoryCleanup)
    ];

    private static async Task<string> CreateMovie(TestWorkspace workspace, string name = "TEST-123.mp4")
    {
        var video = workspace.WriteFile(name, [1, 2, 3]);
        await File.WriteAllTextAsync(workspace.PathOf("movie.NFO"),
            "<movie><title>Original</title><id>TEST-123</id><unknown>keep</unknown></movie>");
        workspace.WriteFile("poster.jpg", TestImageFactory.CreateJpeg(40, 60));
        workspace.WriteFile("fanart.jpg", TestImageFactory.CreateJpeg(80, 60));
        return video;
    }

    private static async Task TestDiscovery()
    {
        using var workspace = new TestWorkspace("folder-discovery");
        var video = await CreateMovie(workspace);
        var sidecars = LocalSidecarLocator.Locate(video);
        AssertEx.Equal(workspace.PathOf("movie.NFO"), sidecars.NfoPath);
        AssertEx.Equal(workspace.PathOf("poster.jpg"), sidecars.PosterPath);
        var job = new MovieJob();
        var loaded = await MovieJobLoader.LoadAsync(job, video);
        AssertEx.Equal(LocalMetadataLoadStatus.Loaded, loaded.MetadataStatus);
        await File.WriteAllTextAsync(workspace.PathOf("TEST-123.nfo"), "<movie><title>Named</title></movie>");
        workspace.WriteFile("TEST-123-poster.jpg", TestImageFactory.CreateJpeg());
        sidecars = LocalSidecarLocator.Locate(video);
        AssertEx.Equal(workspace.PathOf("TEST-123.nfo"), sidecars.NfoPath);
        AssertEx.Equal(workspace.PathOf("TEST-123-poster.jpg"), sidecars.PosterPath);
        workspace.WriteFile("OTHER-456.mp4", [4]);
        sidecars = LocalSidecarLocator.Locate(video);
        AssertEx.Equal(workspace.PathOf("TEST-123.nfo"), sidecars.NfoPath);
        AssertEx.Equal<string?>(null, sidecars.FanartPath);
        var other = LocalSidecarLocator.Locate(workspace.PathOf("OTHER-456.mp4"));
        AssertEx.False(other.HasNfo, "Mixed folder must not assign movie.nfo to another movie.");
        AssertEx.False(other.HasArtwork, "Mixed folder must not assign folder artwork.");
    }

    private static async Task TestMultipartDiscovery()
    {
        using var workspace = new TestWorkspace("folder-cd");
        var video = await CreateMovie(workspace, "TEST-123-cd1.mp4");
        workspace.WriteFile("TEST-123-cd2.mp4", [2]);
        var sidecars = LocalSidecarLocator.Locate(video, "TEST-123", true);
        AssertEx.True(sidecars.HasNfo && sidecars.HasArtwork, "CD group lost folder metadata.");
        workspace.WriteFile("OTHER-456.mp4", [3]);
        sidecars = LocalSidecarLocator.Locate(video, "TEST-123", true);
        AssertEx.False(sidecars.HasNfo, "Mixed CD folder must not share NFO.");
    }

    private static async Task<(LocalSaveContext Context, MovieMetadata Metadata)> Load(string video)
    {
        var sidecars = LocalSidecarLocator.Locate(video);
        var bundle = await NfoReader.ReadAsync(sidecars);
        var artwork = ArtworkCoverCandidate.CreateSidecarPair(
            new MetadataCandidateSource("local-images", "Local", Path.GetDirectoryName(video)!),
            sidecars.PosterPath, sidecars.FanartPath);
        return (new(bundle, artwork, artwork), LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata);
    }

    private static readonly SaveOptions Options = new(true, true, true, false, true, false);

    private static async Task TestInPlace()
    {
        foreach (var mode in new[] { OrganizationTargetMode.VideoDirectory, OrganizationTargetMode.CustomRootNumberFolder })
        {
            using var workspace = new TestWorkspace("folder-inplace");
            var video = await CreateMovie(workspace);
            var (context, metadata) = await Load(video);
            // Point the custom root at the parent and use the current directory's name as the ID:
            // target mode is not a reliable substitute for comparing resolved directories.
            var organization = mode == OrganizationTargetMode.VideoDirectory
                ? new OrganizationOptions(false, false)
                : new OrganizationOptions(mode, false, Path.GetDirectoryName(workspace.Root));
            if (mode != OrganizationTargetMode.VideoDirectory)
                metadata.Id = Path.GetFileName(workspace.Root);
            metadata.Title = "Updated";
            var hash = AssertEx.Sha256(video);
            var plan = FileOrganizationService.BuildPlan(video, metadata, Options, organization, context);
            AssertEx.True(workspace.Root.Equals(plan.TargetDirectory, StringComparison.OrdinalIgnoreCase), "Target must resolve to the source directory.");
            AssertEx.Equal("movie.NFO", plan.OutputFileNames?.Nfo);
            AssertEx.Equal(0, plan.SourcePathsToRetire.Count);
            using var output = new OutputService();
            await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true);
            AssertEx.Equal("keep", XDocument.Load(workspace.PathOf("movie.NFO")).Root?.Element("unknown")?.Value);
            AssertEx.FileDoesNotExist(workspace.PathOf("TEST-123.nfo"));
            AssertEx.Equal(hash, AssertEx.Sha256(video));
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static async Task TestNewDirectory()
    {
        using var workspace = new TestWorkspace("folder-new");
        var video = await CreateMovie(workspace);
        var (context, metadata) = await Load(video);
        var plan = FileOrganizationService.BuildPlan(video, metadata, Options, new OrganizationOptions(true, true), context);
        AssertEx.Equal("TEST-123.nfo", plan.OutputFileNames?.Nfo);
        using var output = new OutputService();
        await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true);
        AssertEx.FileExists(Path.Combine(plan.TargetDirectory, "TEST-123.nfo"));
        AssertEx.FileExists(Path.Combine(plan.TargetDirectory, "TEST-123-poster.jpg"));
        AssertEx.FileExists(Path.Combine(plan.TargetDirectory, "TEST-123-fanart.jpg"));
        AssertEx.FileDoesNotExist(Path.Combine(plan.TargetDirectory, "movie.nfo"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestReplacement()
    {
        using var workspace = new TestWorkspace("folder-replace");
        var video = await CreateMovie(workspace);
        // Use a PNG-only poster fixture without touching user files.
        File.Move(workspace.PathOf("poster.jpg"), workspace.PathOf("unrelated.jpg"));
        workspace.WriteFile("poster.png", PosterImageProcessor.CreateFanartPng(TestImageFactory.CreateJpeg()));
        var (context, metadata) = await Load(video);
        metadata.CoverUrl = workspace.PathOf("fanart.jpg");
        var plan = FileOrganizationService.BuildPlan(video, metadata, Options, new OrganizationOptions(false, false),
            context with { SelectedArtwork = null });
        AssertEx.Equal("poster.png", plan.OutputFileNames?.Poster);
        using var output = new OutputService();
        var result = await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true);
        AssertEx.Equal(workspace.PathOf("poster.png"), result.Outputs.PosterPath);
        AssertEx.Equal((byte)137, File.ReadAllBytes(result.Outputs.PosterPath!)[0]);
        AssertEx.FileDoesNotExist(workspace.PathOf("TEST-123-poster.jpg"));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestMissingArtwork()
    {
        using var workspace = new TestWorkspace("folder-missing");
        var video = await CreateMovie(workspace);
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(video));
        var metadata = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;
        metadata.CoverUrl = workspace.PathOf("fanart.jpg");
        var plan = FileOrganizationService.BuildPlan(video, metadata, Options, new OrganizationOptions(false, false),
            new LocalSaveContext(bundle, null, null));
        AssertEx.Equal("poster.jpg", plan.OutputFileNames?.Poster);
        AssertEx.Equal("fanart.jpg", plan.OutputFileNames?.Fanart);
        using var output = new OutputService();
        await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true);
        AssertEx.FileDoesNotExist(workspace.PathOf("TEST-123-poster.jpg"));
        AssertEx.FileExists(workspace.PathOf("poster.jpg"));
    }

    private static async Task TestExternalChange()
    {
        using var workspace = new TestWorkspace("folder-external");
        var video = await CreateMovie(workspace);
        var (context, metadata) = await Load(video);
        metadata.Title = "Updated";
        var plan = FileOrganizationService.BuildPlan(video, metadata, Options, new OrganizationOptions(false, false), context);
        await File.AppendAllTextAsync(workspace.PathOf("movie.NFO"), "<!--external-->");
        var hash = AssertEx.Sha256(workspace.PathOf("movie.NFO"));
        using var output = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(() => new FileOrganizationService(output).ExecuteAsync(plan, metadata, true),
            "External edits must block commit.");
        AssertEx.Equal(hash, AssertEx.Sha256(workspace.PathOf("movie.NFO")));
    }

    private static async Task TestBothLayouts()
    {
        using var workspace = new TestWorkspace("folder-both");
        var video = await CreateMovie(workspace);
        await File.WriteAllTextAsync(workspace.PathOf("TEST-123.nfo"), "<movie><title>Named</title><id>TEST-123</id></movie>");
        var hash = AssertEx.Sha256(workspace.PathOf("movie.NFO"));
        var (context, metadata) = await Load(video);
        metadata.Title = "Updated named";
        var plan = FileOrganizationService.BuildPlan(video, metadata, Options, new OrganizationOptions(false, false), context);
        AssertEx.Equal("TEST-123.nfo", plan.OutputFileNames?.Nfo);
        using var output = new OutputService();
        await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true);
        AssertEx.Equal(hash, AssertEx.Sha256(workspace.PathOf("movie.NFO")));
    }

    private static async Task TestContentId()
    {
        using var workspace = new TestWorkspace("folder-ids");
        var video = await CreateMovie(workspace);
        await File.WriteAllTextAsync(workspace.PathOf("movie.NFO"),
            "<movie><uniqueid type=\"num\" default=\"true\">TEST-123</uniqueid><uniqueid type=\"cid\">test00123</uniqueid></movie>");
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(video));
        AssertEx.Equal("TEST-123", bundle.Metadata.Id);
        AssertEx.Equal("test00123", bundle.Metadata.ContentId);
    }

    private static async Task TestEmptyDirectoryCleanup()
    {
        foreach (var multipart in new[] { false, true })
        foreach (var scenario in new[] { "success", "hidden", "nested", "cancel", "failure", "inplace", "unrelated" })
        {
            using var workspace = new TestWorkspace("empty-stills");
            var video = await CreateMovie(workspace, multipart ? "TEST-123-cd1.mp4" : "TEST-123.mp4");
            if (multipart) workspace.WriteFile("TEST-123-cd2.mp4", [2, 3]);
            var (context, metadata) = await Load(video);
            var extraDirectory = workspace.CreateDirectory("extrafanart");
            var extraPath = workspace.PathOf("extrafanart/fanart1.jpg");
            if (scenario != "unrelated")
            {
                workspace.WriteFile("extrafanart/fanart1.jpg", TestImageFactory.CreateJpeg());
                context = context with { LocalExtrafanartPaths = [extraPath] };
            }
            if (scenario == "hidden")
            {
                var hidden = workspace.WriteFile("extrafanart/keep.txt", [5]);
                File.SetAttributes(hidden, FileAttributes.Hidden);
            }
            if (scenario == "nested") workspace.CreateDirectory("extrafanart", "keep");
            var organization = scenario == "inplace" ? new OrganizationOptions(false, false) : new OrganizationOptions(true, true);
            // Multipart forces a number folder; use the existing source folder as its resolved destination.
            if (scenario == "inplace" && multipart)
            {
                metadata.Id = Path.GetFileName(workspace.Root);
                organization = new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, false, Path.GetDirectoryName(workspace.Root));
            }
            var plan = FileOrganizationService.BuildPlan(MovieFileSet.Discover(video).VideoPaths, metadata, Options, organization, context);
            using var output = new OutputService();
            using var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress<FileTransactionProgress>(value =>
            {
                if (value.Stage != FileTransactionStage.Committing) return;
                if (scenario == "cancel") cancellation.Cancel();
                if (scenario == "failure") throw new IOException("Injected commit failure");
            });
            var organizer = new FileOrganizationService(output);
            if (scenario == "cancel")
                await AssertEx.ThrowsAsync<OperationCanceledException>(() => organizer.ExecuteAsync(plan, metadata, true, cancellation.Token, progress), "Expected cancellation");
            else if (scenario == "failure")
                await AssertEx.ThrowsAsync<IOException>(() => organizer.ExecuteAsync(plan, metadata, true, cancellation.Token, progress), "Expected failure");
            else
                await organizer.ExecuteAsync(plan, metadata, true, cancellation.Token, progress);
            AssertEx.Equal(scenario != "success", Directory.Exists(extraDirectory));
            AssertEx.True(Directory.Exists(workspace.Root), "Source parent directory must remain.");
            if (scenario is "cancel" or "failure" or "inplace") AssertEx.FileExists(extraPath);
            if (scenario == "hidden") AssertEx.FileExists(workspace.PathOf("extrafanart/keep.txt"));
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static async Task TestNoOp()
    {
        using var workspace = new TestWorkspace("folder-noop");
        var video = await CreateMovie(workspace);
        await File.WriteAllTextAsync(workspace.PathOf("movie.NFO"),
            "<movie><title>TEST-123 · Original</title><id>TEST-123</id><uniqueid type=\"javnumber\" default=\"true\">TEST-123</uniqueid></movie>");
        var (context, metadata) = await Load(video);
        var hash = AssertEx.Sha256(workspace.PathOf("movie.NFO"));
        var plan = FileOrganizationService.BuildPlan(video, metadata, Options, new OrganizationOptions(false, false), context);
        AssertEx.False(plan.HasActualChanges, "Unchanged folder metadata should not be rewritten.");
        using var output = new OutputService();
        await new FileOrganizationService(output).ExecuteAsync(plan, metadata, false);
        AssertEx.Equal(hash, AssertEx.Sha256(workspace.PathOf("movie.NFO")));
    }
}
