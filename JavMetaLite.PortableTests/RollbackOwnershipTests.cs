using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

internal static partial class Program
{
    private static async Task RollbackExternalEdits()
    {
        foreach (var multipart in new[] { false, true })
        foreach (var videoTarget in new[] { false, true })
        foreach (var replacement in new[] { false, true })
        {
            using var temp = new Temp();
            var first = temp.Write(multipart ? "incoming/DEMO-081-cd1.mp4" : "incoming/DEMO-081.mp4", "ORIGINAL FIRST");
            var paths = multipart ? new[] { first, temp.Write("incoming/DEMO-081-cd2.mp4", "ORIGINAL SECOND") } : [first];
            var originals = paths.ToDictionary(path => path, File.ReadAllText);
            var metadata = new MovieMetadata { Id = "DEMO-081", Title = "Our committed title" };
            var plan = FileOrganizationService.BuildPlan(paths, metadata, NfoOnly, new(true, true)) with
            { RequiresVerifiedVideoCopy = true };
            var target = videoTarget ? plan.VideoTransfers.Last().TargetPath :
                plan.Changes.Single(change => change.DestinationPath.EndsWith(".nfo", StringComparison.Ordinal)).DestinationPath;
            string? external = null;
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            {
                if (p.Stage != FileTransactionStage.RetiringSource) return;
                var length = checked((int)new FileInfo(target).Length);
                var writeTime = File.GetLastWriteTimeUtc(target);
                external = new string('x', length);
                // Same length and restored mtime exercise content verification,
                // while replacement also changes filesystem identity.
                if (replacement) File.Delete(target);
                File.WriteAllText(target, external);
                File.SetLastWriteTimeUtc(target, writeTime);
                cancellation.Cancel();
            });
            using var output = new OutputService(Images);
            FileRecoveryException? recovery = null;
            try { await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true, cancellation.Token, progress); }
            catch (FileRecoveryException error) { recovery = error; }
            Check(recovery is not null && external is not null, "External output was treated as an ordinary restored cancellation");
            Equal(external, File.ReadAllText(target));
            foreach (var path in paths) Equal(originals[path], File.ReadAllText(path));
            Check(recovery!.RecoveryPaths.Any(Directory.Exists), "Ownership failure discarded recovery staging");
            Check(plan.VideoTransfers.Where(transfer => transfer.TargetPath != target).All(transfer => !File.Exists(transfer.TargetPath)),
                "Unchanged committed copies were not safely withdrawn");
        }
    }

    private static async Task RollbackQuarantineRaces()
    {
        var scenarios = OperatingSystem.IsWindows() ? new[] { "changed-quarantine", "new-target" } :
            new[] { "changed-quarantine", "new-target", "target-symlink", "quarantine-symlink", "ancestor-symlink" };
        foreach (var multipart in new[] { false, true })
        foreach (var scenario in scenarios)
        {
            using var temp = new Temp();
            var first = temp.Write(multipart ? "incoming/DEMO-082-cd1.mp4" : "incoming/DEMO-082.mp4", "FIRST");
            var paths = multipart ? new[] { first, temp.Write("incoming/DEMO-082-cd2.mp4", "SECOND") } : [first];
            var metadata = new MovieMetadata { Id = "DEMO-082", Title = "Quarantine ownership" };
            var plan = FileOrganizationService.BuildPlan(paths, metadata, NfoOnly, new(true, true)) with
            { RequiresVerifiedVideoCopy = true };
            var target = plan.Changes.Single(change => change.DestinationPath.EndsWith(".nfo", StringComparison.Ordinal)).DestinationPath;
            var foreign = temp.Write("unrelated/keep.nfo", "UNRELATED FILE");
            var foreignOutput = temp.Write("foreign-target/" + Path.GetFileName(target), "UNRELATED TARGET DIRECTORY");
            string? quarantinedOutput = null;
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            { if (p.Stage == FileTransactionStage.RetiringSource) cancellation.Cancel(); });
            using var output = new OutputService(Images);
            var service = new FileOrganizationService(output, TemporaryDirectoryCleanup.TryDeleteAsync, (original, quarantined) =>
            {
                if (original != target) return;
                quarantinedOutput = quarantined;
                switch (scenario)
                {
                    case "changed-quarantine": File.WriteAllText(quarantined, "EXTERNAL AFTER QUARANTINE"); break;
                    case "new-target": File.WriteAllText(original, "EXTERNAL NEW TARGET"); break;
                    case "target-symlink": File.CreateSymbolicLink(original, foreign); break;
                    case "quarantine-symlink": File.Delete(quarantined); File.CreateSymbolicLink(quarantined, foreign); break;
                    case "ancestor-symlink":
                        var relocated = plan.TargetDirectory + "-relocated";
                        Directory.Move(plan.TargetDirectory, relocated);
                        Directory.CreateSymbolicLink(plan.TargetDirectory, Path.GetDirectoryName(foreignOutput)!);
                        quarantinedOutput = Path.Combine(relocated, Path.GetRelativePath(plan.TargetDirectory, quarantined));
                        break;
                }
            });
            await Throws<FileRecoveryException>(() => service.ExecuteAsync(plan, metadata, true, cancellation.Token, progress));
            Check(quarantinedOutput is not null, "Rollback quarantine boundary was not exercised");
            Equal("UNRELATED FILE", File.ReadAllText(foreign));
            Equal("UNRELATED TARGET DIRECTORY", File.ReadAllText(foreignOutput));
            if (scenario == "changed-quarantine") Equal("EXTERNAL AFTER QUARANTINE", File.ReadAllText(target));
            else
            {
                Check(File.Exists(quarantinedOutput!), "Failed ownership check removed quarantine evidence");
                if (scenario == "new-target") Equal("EXTERNAL NEW TARGET", File.ReadAllText(target));
                if (scenario == "target-symlink") Check(new FileInfo(target).LinkTarget is not null, "External target link was overwritten");
                if (scenario == "quarantine-symlink") Check(new FileInfo(quarantinedOutput!).LinkTarget is not null, "Quarantine link was deleted");
            }
            foreach (var path in paths) Check(File.Exists(path), "Rollback lost an original movie");
            Check(Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(),
                "Rollback ownership failure reached successful cleanup");
        }
    }

    private static async Task RollbackReplacedNfo()
    {
        foreach (var externalChange in new[] { false, true })
        foreach (var longName in new[] { false, true })
        {
            using var temp = new Temp();
            var basename = longName ? "DEMO-086 " + new string('x', 232) : "DEMO-086";
            var video = temp.Write(basename + ".mp4", "ORIGINAL MOVIE");
            var nfo = temp.Write(basename + ".nfo", "<movie><id>DEMO-086</id><title>Original baseline</title></movie>");
            // NTFS may tunnel this older birth time onto the new file published at
            // the same name. It must not make normal rollback look externally edited.
            File.SetCreationTimeUtc(nfo, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var before = File.ReadAllText(nfo);
            using var job = new MovieJob();
            await MovieJobLoader.LoadAsync(job, video, Images);
            job.Metadata.Title = "Our updated title";
            var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, InPlace, job.CreateLocalSaveContext());
            using var output = new OutputService(Images);
            var external = "<movie><title>EXTERNAL EDIT AFTER COMMIT</title></movie>";
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            {
                if (p.Stage != FileTransactionStage.Completed) return;
                if (externalChange) File.WriteAllText(nfo, external);
                // A synchronous journal/progress failure at this boundary still
                // enters rollback; it must not erase a later independent edit.
                throw new IOException("Controlled final-journal write failure");
            });
            Exception? failure = null;
            try { await new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true, default, progress); }
            catch (Exception error) { failure = error; }
            Check(failure is IOException && (failure is FileRecoveryException) == externalChange,
                $"Normal replacement rollback or external-edit recovery was classified incorrectly (external={externalChange}, longName={longName}): {failure}");
            Equal(externalChange ? external : before, File.ReadAllText(nfo));
            Equal("ORIGINAL MOVIE", File.ReadAllText(video));
            if (externalChange)
                Check(Directory.EnumerateFiles(temp.Path, "*.nfo", SearchOption.AllDirectories)
                    .Any(path => path != nfo && File.ReadAllText(path) == before), "Original NFO backup was discarded");
            else
                Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(),
                    "Normal replacement rollback retained unnecessary staging");
        }
        // Long legal movie components reach the real committed-video rollback.
        // Multipart uses short movie.nfo, independently of NFO writer temp names.
        using (var temp = new Temp())
        {
            var basename = "DEMO-086 " + new string('x', 225);
            var paths = new[] { temp.Write(basename + "-cd1.mp4", "LONG FIRST"), temp.Write(basename + "-cd2.mp4", "LONG SECOND") };
            var metadata = new MovieMetadata { Id = "DEMO-086", Title = "Long filename rollback" };
            var plan = FileOrganizationService.BuildPlan(paths, metadata, NfoOnly, new(true, false)) with
            { RequiresVerifiedVideoCopy = true };
            using var output = new OutputService(Images);
            using var cancellation = new CancellationTokenSource();
            var committed = false;
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            {
                if (p.Stage != FileTransactionStage.RetiringSource) return;
                committed = plan.VideoTransfers.All(transfer => File.Exists(transfer.TargetPath));
                cancellation.Cancel();
            });
            await Throws<OperationCanceledException>(() => new FileOrganizationService(output).ExecuteAsync(plan, metadata, true, cancellation.Token, progress));
            Check(committed, "Long filenames did not reach committed rollback");
            Equal("LONG FIRST", File.ReadAllText(paths[0])); Equal("LONG SECOND", File.ReadAllText(paths[1]));
            Check(plan.VideoTransfers.All(transfer => !File.Exists(transfer.TargetPath)) &&
                !Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(),
                "Long filename rollback failed to remove transaction-owned copies and staging");
        }
    }

    private static async Task RollbackLateQuarantineMutation()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var temp = new Temp();
            var first = temp.Write(multipart ? "incoming/DEMO-087-cd1.mp4" : "incoming/DEMO-087.mp4", "ORIGINAL FIRST");
            var paths = multipart ? new[] { first, temp.Write("incoming/DEMO-087-cd2.mp4", "ORIGINAL SECOND") } : [first];
            var metadata = new MovieMetadata { Id = "DEMO-087", Title = "Late quarantine write" };
            var plan = FileOrganizationService.BuildPlan(paths, metadata, NfoOnly, new(true, true)) with
            { RequiresVerifiedVideoCopy = true };
            string? quarantinedVideo = null;
            string? external = null;
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            { if (p.Stage == FileTransactionStage.RetiringSource) cancellation.Cancel(); });
            using var output = new OutputService(Images);
            var service = new FileOrganizationService(output, TemporaryDirectoryCleanup.TryDeleteAsync, (original, quarantined) =>
            {
                if (quarantinedVideo is null && original.EndsWith(".mp4", StringComparison.Ordinal)) quarantinedVideo = quarantined;
                if (!original.EndsWith(".nfo", StringComparison.Ordinal)) return;
                Check(quarantinedVideo is not null, "Video was not withdrawn before NFO rollback");
                var time = File.GetLastWriteTimeUtc(quarantinedVideo!);
                external = new string('z', checked((int)new FileInfo(quarantinedVideo!).Length));
                File.WriteAllText(quarantinedVideo!, external);
                File.SetLastWriteTimeUtc(quarantinedVideo!, time);
            });
            await Throws<FileRecoveryException>(() => service.ExecuteAsync(plan, metadata, true, cancellation.Token, progress));
            Check(external is not null, "The already-verified quarantine file was not mutated");
            Equal(external, File.ReadAllText(quarantinedVideo!));
            Equal("ORIGINAL FIRST", File.ReadAllText(first));
            if (multipart) Equal("ORIGINAL SECOND", File.ReadAllText(paths[1]));
        }
    }

    private static async Task RollbackCommittedEvidence()
    {
        using var temp = new Temp();
        var video = temp.Write("DEMO-088.mp4", "ORIGINAL MOVIE");
        var metadata = new MovieMetadata { Id = "DEMO-088", Title = "Our generated bytes" };
        var plan = FileOrganizationService.BuildPlan(video, metadata, NfoOnly, InPlace);
        var nfo = plan.Changes.Single(change => change.DestinationPath.EndsWith(".nfo", StringComparison.Ordinal)).DestinationPath;
        string? generatedHash = null;
        using var output = new OutputService(Images);
        var progress = new ImmediateProgress<FileTransactionProgress>(p =>
        {
            if (p.Stage != FileTransactionStage.Completed) return;
            generatedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(nfo)));
            File.WriteAllText(nfo, "<movie><title>EXTERNAL AFTER CORE COMMIT</title></movie>");
        });
        var saved = await new FileOrganizationService(output).ExecuteAsync(plan, metadata, true, default, progress);
        var proof = saved.CommittedFiles.Single(file => file.Path == nfo);
        Equal(generatedHash, proof.Sha256);
        Check(proof.Sha256 != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(nfo))),
            "Post-commit external bytes became authoritative committed evidence");
        Equal("ORIGINAL MOVIE", File.ReadAllText(video));
    }
}
