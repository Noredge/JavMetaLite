using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

internal static partial class Program
{
    private static async Task RollbackCleanupRootLost()
    {
        using (var temp = new Temp())
        {
            var partial = FileSystemPathSemantics.CanonicalizePath(temp.Write("volume/.JavMetaLite-test.tmp/partial.mp4", "RETAIN PARTIAL COPY"));
            var volume = Path.GetDirectoryName(Path.GetDirectoryName(partial)!)!;
            var offline = volume + "-offline";
            var staging = Path.GetDirectoryName(partial)!;
            var root = TemporaryDirectoryCleanup.ExistingRoot.Capture(volume);
            Check(await TemporaryDirectoryCleanup.TryDeleteUnderRootAsync(Path.Combine(volume, "never-created", "staging"),
                root, TemporaryDirectoryCleanup.TryDeleteAsync) is null, "Missing descendants under the original readable root should be confirmed");
            var outside = temp.Write("outside/keep.txt", "OUTSIDE ROOT");
            Check(await TemporaryDirectoryCleanup.TryDeleteUnderRootAsync(Path.GetDirectoryName(outside)!,
                root, TemporaryDirectoryCleanup.TryDeleteAsync) is not null, "Cleanup escaped its captured root");
            Equal("OUTSIDE ROOT", File.ReadAllText(outside));

            // Root disappears after the initial check but before the legacy
            // cleanup confirms absence. A readable higher ancestor is not proof.
            var issue = await TemporaryDirectoryCleanup.TryDeleteUnderRootAsync(staging, root, async path =>
            {
                Directory.Move(volume, offline);
                return await TemporaryDirectoryCleanup.TryDeleteAsync(path);
            });
            Check(issue is not null, "Cleanup claimed success after the transaction root disappeared");
            Equal("RETAIN PARTIAL COPY", File.ReadAllText(Path.Combine(offline, Path.GetRelativePath(volume, partial))));
            var calls = 0;
            Check(await TemporaryDirectoryCleanup.TryDeleteUnderRootAsync(staging, root, path =>
            { calls++; return Task.FromResult<TemporaryCleanupIssue?>(null); }) is not null,
                "An already unavailable root was treated as empty");
            Equal(0, calls);
            if (OperatingSystem.IsMacOS())
            {
                // A detached mountpoint can remain on the host volume. The
                // same readable path must not authorize removal of new files.
                var replacement = temp.Write("volume/.JavMetaLite-test.tmp/foreign.txt", "EXTERNAL REPLACEMENT ROOT");
                Check(await TemporaryDirectoryCleanup.TryDeleteUnderRootAsync(staging, root,
                    TemporaryDirectoryCleanup.TryDeleteAsync) is not null, "Replacement directory identity was accepted");
                Equal("EXTERNAL REPLACEMENT ROOT", File.ReadAllText(replacement));
                Equal("RETAIN PARTIAL COPY", File.ReadAllText(Path.Combine(offline, Path.GetRelativePath(volume, partial))));
            }
        }

        if (OperatingSystem.IsWindows()) return; // POSIX permits renaming a directory containing the open staged copy.
        foreach (var multipart in new[] { false, true })
        {
            using var temp = new Temp();
            var first = temp.Write(multipart ? "source/DEMO-814-cd1.mp4" : "source/DEMO-814.mp4", new string('v', 2 * 1024 * 1024));
            var sources = multipart ? new[] { first, temp.Write("source/DEMO-814-cd2.mp4", "SECOND ORIGINAL") } : [first];
            var originals = sources.ToDictionary(path => path, File.ReadAllText);
            var sentinel = temp.Write("volume/unrelated.txt", "UNRELATED");
            var volume = FileSystemPathSemantics.CanonicalizePath(Path.GetDirectoryName(sentinel)!);
            var offline = volume + "-offline";
            var metadata = new MovieMetadata { Id = "DEMO-814", Title = "Volume lost during copy" };
            var plan = FileOrganizationService.BuildPlan(sources, metadata, NfoOnly,
                new(OrganizationTargetMode.CustomRootNumberFolder, true, volume)) with { RequiresVerifiedVideoCopy = true };
            var moved = false;
            using var output = new OutputService(Images);
            TemporaryCleanupException? failure = null;
            try
            {
                await new FileOrganizationService(output).ExecuteAsync(plan, metadata, false, default,
                    new ImmediateProgress<FileTransactionProgress>(progress =>
                    {
                        if (moved || progress.Stage != FileTransactionStage.CopyingMovie || progress.BytesProcessed <= 0) return;
                        Directory.Move(volume, offline);
                        moved = true;
                        throw new IOException("Synthetic volume unavailable after copying began");
                    }));
            }
            catch (TemporaryCleanupException error) { failure = error; }
            Check(moved && failure is not null, "Lost transaction root did not surface cleanup uncertainty");
            Check(failure!.InnerException is IOException && failure.Issues.Any(issue => issue.Path.StartsWith(volume + Path.DirectorySeparatorChar,
                FileSystemPathSemantics.PathComparison)), "Missing target recovery path or original device error");
            foreach (var path in sources) Equal(originals[path], File.ReadAllText(path));
            Check(!Directory.Exists(volume), "Cleanup recreated the missing root");
            Check(Directory.EnumerateDirectories(offline, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Offline partial copy was discarded");
            Equal("UNRELATED", File.ReadAllText(Path.Combine(offline, "unrelated.txt")));
            Check(!Directory.EnumerateDirectories(Path.GetDirectoryName(first)!, ".JavMetaLite*", SearchOption.AllDirectories).Any(),
                "Reachable source staging was not cleaned");
        }
    }
}
