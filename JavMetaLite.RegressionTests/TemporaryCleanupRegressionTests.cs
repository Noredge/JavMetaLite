using System.IO;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal static class TemporaryCleanupRegressionTests
{
    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("cleanup", "Missing directory is confirmed, not confused with missing drive", TestMissing),
        new("cleanup", "Locked temporary file is reported, then removable after release", TestLock),
        new("cleanup", "Single and CD cancellation clean both staging roots", () => TestTransactions(false, false)),
        new("cleanup", "Single and CD cancellation with inaccessible staging preserves diagnostics", () => TestTransactions(true, false)),
        new("cleanup", "Device error plus cancellation retains original error and both paths", () => TestTransactions(true, true)),
        new("cleanup", "Committed saves with cleanup warnings remain completed and stop batch", TestCommitted)
    ];

    private static async Task TestMissing()
    {
        using var w = new TestWorkspace("cleanup-missing");
        AssertEx.True(await TemporaryDirectoryCleanup.TryDeleteAsync(w.PathOf("never-created", "stage")) is null,
            "A genuinely absent staging directory must be accepted.");
        var unusedDrive = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => $"{(char)c}:\\")
            .FirstOrDefault(root => !DriveInfo.GetDrives().Any(d => d.Name.Equals(root, StringComparison.OrdinalIgnoreCase)));
        if (unusedDrive is not null)
            AssertEx.True(await TemporaryDirectoryCleanup.TryDeleteAsync(Path.Combine(unusedDrive, ".JavMetaLite-test.tmp")) is not null,
                "An unavailable drive must not count as successful cleanup.");
    }

    private static async Task TestLock()
    {
        using var w = new TestWorkspace("cleanup-lock");
        var root = w.CreateDirectory(".JavMetaLite-test.tmp");
        var path = w.WriteFile(".JavMetaLite-test.tmp/partial.mp4", [1, 2, 3]);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            AssertEx.True(await TemporaryDirectoryCleanup.TryDeleteAsync(root) is not null, "Lock must not be hidden.");
        AssertEx.FileExists(path);
        AssertEx.True(await TemporaryDirectoryCleanup.TryDeleteAsync(root) is null, "Released file must be removable.");
        AssertEx.False(Directory.Exists(root), "Successful cleanup must actually remove the directory.");
    }

    private static async Task TestTransactions(bool blocked, bool deviceError)
    {
        foreach (var multipart in new[] { false, true })
        foreach (var verification in new[] { CrossVolumeVerificationMode.FullSha256, CrossVolumeVerificationMode.FileSizeOnly })
        {
            using var w = new TestWorkspace("cleanup-transaction");
            var sources = CreateSources(w, multipart);
            var hashes = sources.Select(AssertEx.Sha256).ToArray();
            var metadata = new MovieMetadata { Id = "IPX-123", Title = "Synthetic cleanup test" };
            var plan = Plan(w, sources, metadata, verification);
            using var output = new OutputService();
            var attempted = new List<string>();
            var service = new FileOrganizationService(output, async path =>
            {
                attempted.Add(path);
                return blocked ? new TemporaryCleanupIssue(path, "Synthetic inaccessible device") :
                    await TemporaryDirectoryCleanup.TryDeleteAsync(path);
            });
            using var cancel = new CancellationTokenSource();
            var progress = new InlineProgress<FileTransactionProgress>(update =>
            {
                if (update.Stage == FileTransactionStage.CopyingMovie && update.BytesProcessed > 0)
                {
                    cancel.Cancel();
                    if (deviceError) throw new IOException("Synthetic device disappeared", unchecked((int)0x800701B1));
                }
            });
            if (blocked)
            {
                var error = await AssertEx.ThrowsAsync<TemporaryCleanupException>(
                    () => service.ExecuteAsync(plan, metadata, false, cancel.Token, progress), "Cleanup failure must survive cancellation.");
                AssertEx.Equal(2, error.Issues.Count);
                AssertEx.True(deviceError ? error.InnerException is IOException : error.InnerException is OperationCanceledException,
                    "Original failure was overwritten.");
            }
            else
            {
                await AssertEx.ThrowsAsync<OperationCanceledException>(
                    () => service.ExecuteAsync(plan, metadata, false, cancel.Token, progress), "Clean cancellation must stay cancellation.");
                w.AssertNoTemporaryArtifacts();
            }
            AssertEx.Equal(2, attempted.Count, "Both source and target staging roots must be attempted.");
            for (var n = 0; n < sources.Length; n++) AssertEx.Equal(hashes[n], AssertEx.Sha256(sources[n]));
            foreach (var target in plan.VideoTransfers) AssertEx.FileDoesNotExist(target.TargetPath);
        }
    }

    private static async Task TestCommitted()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var w = new TestWorkspace("cleanup-committed");
            var sources = CreateSources(w, multipart);
            var metadata = new MovieMetadata { Id = "IPX-123", Title = "Synthetic committed save" };
            var plan = Plan(w, sources, metadata, CrossVolumeVerificationMode.FullSha256);
            using var output = new OutputService();
            var service = new FileOrganizationService(output, path =>
                Task.FromResult<TemporaryCleanupIssue?>(new(path, "Synthetic cleanup failure")));
            using var first = new MovieJob();
            using var next = new MovieJob();
            first.ResetForVideo(sources[0], metadata.Id);
            next.ResetForVideo(sources[0], metadata.Id);
            first.InitializeSaveConfiguration(new MovieSaveConfiguration(plan.SaveOptions, plan.OrganizationOptions));
            next.InitializeSaveConfiguration(new MovieSaveConfiguration(plan.SaveOptions, plan.OrganizationOptions));
            var calls = 0;
            var result = await BatchSaveCoordinator.ExecuteAsync(
                [new(first, plan, first.ReviewRevision, false), new(next, plan, next.ReviewRevision, false)],
                (item, token) => { calls++; return service.ExecuteAsync(plan, metadata, false, token); });
            AssertEx.Equal(1, calls);
            AssertEx.Equal(1, result.CompletedCount);
            AssertEx.Equal(0, result.FailedCount);
            AssertEx.Equal(1, result.NotStartedCount);
            AssertEx.Equal(2, result.Items[0].SaveResult!.CleanupIssues.Count);
            foreach (var target in plan.VideoTransfers) AssertEx.FileExists(target.TargetPath);
        }
    }

    private static string[] CreateSources(TestWorkspace w, bool multipart) => multipart
        ? [w.WriteFile("incoming/IPX-123-CD1.mp4", new byte[2 * 1024 * 1024]),
           w.WriteFile("incoming/IPX-123-CD2.mp4", new byte[2 * 1024 * 1024])]
        : [w.WriteFile("incoming/IPX-123.mp4", new byte[2 * 1024 * 1024])];

    private static SavePlan Plan(TestWorkspace w, string[] sources, MovieMetadata metadata, CrossVolumeVerificationMode mode) =>
        FileOrganizationService.BuildPlan(sources, metadata, new SaveOptions(true, false, false, false, true, false),
            new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, w.CreateDirectory("library"), mode))
            with { RequiresVerifiedVideoCopy = true };
}
