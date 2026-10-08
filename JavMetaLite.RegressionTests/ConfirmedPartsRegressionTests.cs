using System.IO;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal static class ConfirmedPartsRegressionTests
{
    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("confirmed-parts", "Explicit part order does not change automatic a/b discovery", TestOrder),
        new("confirmed-parts", "Explicit parts reject duplicate paths and mixed directories", TestInvalid),
        new("confirmed-parts", "Confirmed parts save atomically with ordered CD names", () => TestSave(false)),
        new("confirmed-parts", "Confirmed parts cancellation restores original names and bytes", () => TestSave(true)),
        new("confirmed-parts", "Confirmed parts never overwrite existing videos", TestExisting)
    ];
    private static readonly SaveOptions Options = new(true, false, false, false, false, false);
    private static MovieMetadata Metadata() => new() { Id = "RBD-487", Title = "Synthetic parts" };

    private static Task TestOrder()
    {
        using var w = new TestWorkspace("confirmed-order");
        var a = w.WriteFile("source/titlea.mp4", [1]);
        var b = w.WriteFile("source/titleb.mp4", [2]);
        AssertEx.Equal(2, MovieFileSet.Group([a, b]).Count);
        AssertEx.Throws<InvalidOperationException>(() => MovieFileSet.Create([a, b]), "Must not infer a/b.");
        var set = MovieFileSet.CreateConfirmedParts([b, a], "RBD-487");
        AssertEx.Equal(b, set.PrimaryPath);
        AssertEx.Equal(1, set.Parts[0].PartNumber);
        AssertEx.Equal(2, set.Parts[1].PartNumber);
        var plan = FileOrganizationService.BuildConfirmedMultipartPlan([b, a], Metadata(), Options,
            new OrganizationOptions(OrganizationTargetMode.VideoDirectory, false));
        AssertEx.Equal(Path.Combine(w.Root, "source", "RBD-487", "RBD-487-cd1.mp4"), plan.VideoTransfers[0].TargetPath);
        AssertEx.FileExists(a); AssertEx.FileExists(b);
        AssertEx.False(Directory.Exists(Path.GetDirectoryName(plan.TargetVideoPath)), "Planning must not create directories.");
        return Task.CompletedTask;
    }
    private static Task TestInvalid()
    {
        using var w = new TestWorkspace("confirmed-invalid");
        var a = w.WriteFile("one/a.mp4", [1]); var b = w.WriteFile("two/b.mp4", [2]);
        AssertEx.Throws<ArgumentException>(() => MovieFileSet.CreateConfirmedParts([a, a.ToUpperInvariant()], "RBD-487"), "Repeated source must be rejected.");
        AssertEx.Throws<InvalidOperationException>(() => MovieFileSet.CreateConfirmedParts([a, b], "RBD-487"), "Mixed source roots are unsupported.");
        return Task.CompletedTask;
    }
    private static async Task TestSave(bool cancel)
    {
        foreach (var verification in new[] { CrossVolumeVerificationMode.FullSha256, CrossVolumeVerificationMode.FileSizeOnly })
        {
            using var w = new TestWorkspace("confirmed-save");
            var a = w.WriteFile("source/titlea.mp4", Enumerable.Repeat((byte)1, 2097152).ToArray());
            var b = w.WriteFile("source/titleb.mp4", Enumerable.Repeat((byte)2, 2097152).ToArray());
            var separate = w.WriteFile("source/titlea.nfo", [7, 8]);
            var hashes = new[] { AssertEx.Sha256(b), AssertEx.Sha256(a) };
            var metadata = Metadata();
            var plan = FileOrganizationService.BuildConfirmedMultipartPlan([b, a], metadata, Options,
                new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, w.CreateDirectory("target"), verification));
            // Exercise the cross-volume transaction path with synthetic same-volume files.
            plan = plan with { RequiresVerifiedVideoCopy = true };
            using var output = new OutputService();
            var service = new FileOrganizationService(output);
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress<FileTransactionProgress>(update =>
            {
                if (cancel && update.Stage == FileTransactionStage.CopyingMovie && update.BytesProcessed > 0) cts.Cancel();
            });
            if (cancel)
            {
                await AssertEx.ThrowsAsync<OperationCanceledException>(() => service.ExecuteAsync(plan, metadata, false, cts.Token, progress), "Cancel must stop.");
                AssertEx.Equal(hashes[0], AssertEx.Sha256(b)); AssertEx.Equal(hashes[1], AssertEx.Sha256(a));
                foreach (var transfer in plan.VideoTransfers) AssertEx.FileDoesNotExist(transfer.TargetPath);
            }
            else
            {
                var result = await service.ExecuteAsync(plan, metadata, false, cts.Token, progress);
                AssertEx.Equal(2, result.VideoPaths.Count);
                for (var i = 0; i < 2; i++) AssertEx.Equal(hashes[i], AssertEx.Sha256(result.VideoPaths[i]));
                AssertEx.FileDoesNotExist(a); AssertEx.FileDoesNotExist(b);
            }
            AssertEx.FileExists(separate);
            w.AssertNoTemporaryArtifacts();
        }
    }
    private static async Task TestExisting()
    {
        using var w = new TestWorkspace("confirmed-existing");
        var a = w.WriteFile("source/a.mp4", [1]); var b = w.WriteFile("source/b.mp4", [2]);
        var target = w.CreateDirectory("target");
        var existing = w.WriteFile("target/RBD-487/RBD-487-cd1.mp4", [9]);
        var plan = FileOrganizationService.BuildConfirmedMultipartPlan([a, b], Metadata(), Options,
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, target));
        AssertEx.True(plan.HasBlockingConflicts, "Existing CD video must block.");
        using var output = new OutputService();
        await AssertEx.ThrowsAsync<IOException>(() => new FileOrganizationService(output).ExecuteAsync(plan, Metadata(), true), "Overwrite approval must not overwrite video.");
        AssertEx.Equal((byte)9, File.ReadAllBytes(existing)[0]); AssertEx.FileExists(a); AssertEx.FileExists(b);
    }
}
