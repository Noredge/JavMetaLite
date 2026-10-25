using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using SkiaSharp;

internal static partial class Program
{
    private sealed record ParsingCase(string CaseId, string Name, string? ExpectedId);

    private static async Task VerifyParsingCases(IEnumerable<ParsingCase> cases)
    {
        using var temp = new Temp();
        foreach (var item in cases)
        {
            // Case identifiers only in failure messages: optional private names never enter logs.
            Check(MovieIdParser.TryExtract(item.Name) == item.ExpectedId, item.CaseId + ": full filename parse differs");
            Check(MovieIdParser.TryExtractFromBaseName(Path.GetFileNameWithoutExtension(item.Name)) == item.ExpectedId,
                item.CaseId + ": already-stripped basename parse differs");
            var video = temp.Write(item.CaseId + "/DEMO-999/" + item.Name, "KNOWN SYNTHETIC BYTES");
            Check(MovieIdParser.TryExtract(video) == item.ExpectedId, item.CaseId + ": POSIX path parse differs or borrowed parent ID");
            var discovered = await MovieInputDiscovery.DiscoverAsync([Path.GetDirectoryName(video)!, video], false);
            Check(discovered.MovieFileSets.Count == 1, item.CaseId + ": file/folder overlap changed import identity");
            var set = discovered.MovieFileSets[0];
            Check(MovieIdParser.TryExtractFromBaseName(set.MovieBaseName) == item.ExpectedId, item.CaseId + ": discovery basename parse differs");
            using var job = new MovieJob();
            var loaded = await MovieJobLoader.LoadAsync(job, set.VideoPaths, Images);
            Check(loaded.MetadataStatus == LocalMetadataLoadStatus.NoNfo && job.Metadata.Id == (item.ExpectedId ?? ""),
                item.CaseId + ": queue ID differs from discovery");
        }
    }

    private static async Task RegressionParsingEntrypoints()
    {
        await VerifyParsingCases([
            new("PREFIX-U", "mirror.example@DEMO-391-U.mp4", "DEMO-391"),
            new("PREFIX-UC", "mirror.example@DEMO-950-UC.mp4", "DEMO-950"),
            new("C01-EQUIVALENT", "MIDE-720 合成中文边界 OL 长标题.mp4", "MIDE-720"),
            new("H01", "[1080p] MIDE-720 OL.mp4", "MIDE-720"),
            new("H02", "MIDE-480.mp4", "MIDE-480"), new("H03", "MIDE-1080.mp4", "MIDE-1080"),
            new("H04", "MIDE-2160.mp4", "MIDE-2160"), new("H05", "ABF.391-U.mp4", "ABF-391"),
            new("H06", "mirror.example@IPZZ-950-UC.mp4", "IPZZ-950"),
            new("H07", "mirror.example@FC2-PPV-1234567.mp4", "FC2-PPV-1234567"),
            new("H08", "mirror.example@SSNI-177-UC-CD1.mp4", "SSNI-177"),
            new("H09", "042619-903 IPX-123.mp4", "IPX-123"),
            new("H10", "vacation-1080p.mp4", null), new("H11", "HD-720.mp4", null),
            new("C02", "042619-903.mp4", null), new("C03", "012318-589.mp4", null),
            new("C04", "032614_779.mp4", null), new("B-EQUIVALENT", "合成uu无番号说明 20年样本.mp4", null),
            new("TRAILING-ABC", "ABCD-123abc.mp4", null), new("TRAILING-X", "ABCD-123x.mp4", null),
            new("TRAILING-AB-BASELINE", "RBD-487a.mp4", null)
        ]);
        foreach (var value in new string?[] { null, "", " ", "\t\n" })
        {
            Equal<string?>(null, MovieIdParser.TryExtract(value));
            Equal<string?>(null, MovieIdParser.TryExtractFromBaseName(value));
        }
    }

    private static async Task RegressionPrivateNames()
    {
        var path = Environment.GetEnvironmentVariable("JAVMETALITE_PRIVATE_PARSING_CASES")!;
        var cases = JsonSerializer.Deserialize<ParsingCase[]>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Check(cases.Length >= 16, "Private handoff matrix is incomplete");
        await VerifyParsingCases(cases);
        Console.WriteLine("PRIVATE PARSING CASE IDS: " + string.Join(",", cases.Select(item => item.CaseId)));
    }

    private static Dictionary<string, string> TreeHashes(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private static async Task RegressionExplicitParts()
    {
        using var temp = new Temp();
        var a = temp.Write("independent/RBD-487a.mp4", "FIRST INPUT");
        var b = temp.Write("independent/RBD-487b.mp4", "SECOND INPUT");
        var u = temp.Write("independent/MFYD-190-U.mp4", "U INPUT");
        var uc = temp.Write("independent/MFYD-190-UC.mp4", "UC INPUT");
        var found = await MovieInputDiscovery.DiscoverAsync([Path.GetDirectoryName(a)!]);
        Equal(4, found.MovieFileSets.Count);
        Check(found.MovieFileSets.All(set => set.VideoPaths.Count == 1), "a/b or matching IDs were auto-merged");
        var cd1 = temp.Write("parts/mirror.example@DEMO-177-UC-CD1.mp4", "CD FIRST");
        var cd2 = temp.Write("parts/mirror.example@DEMO-177-UC-CD2.mp4", "CD SECOND");
        var grouped = await MovieInputDiscovery.DiscoverAsync([cd2, cd1]);
        Equal(1, grouped.MovieFileSets.Count);
        Check(grouped.MovieFileSets[0].VideoPaths.Select(File.ReadAllText).SequenceEqual(["CD FIRST", "CD SECOND"]), "Explicit CD order differs");
        using var first = new MovieJob(); using var second = new MovieJob();
        first.ResetForVideo(a, "RBD-487"); second.ResetForVideo(b, "RBD-487"); // User confirmation supplies the known ID.
        var org = new OrganizationOptions(true, true);
        var firstPlan = FileOrganizationService.BuildPlan(a, first.Metadata, NfoOnly, org);
        var secondPlan = FileOrganizationService.BuildPlan(b, second.Metadata, NfoOnly, org);
        var conflicts = BatchSavePathConflicts.Find([new(first, firstPlan, first.ReviewRevision), new(second, secondPlan, second.ReviewRevision)]);
        Check(conflicts.ContainsKey(first) && conflicts.ContainsKey(second), "Same-ID independent files did not conflict");
        var confirmed = FileOrganizationService.BuildConfirmedMultipartPlan([b, a], first.Metadata, NfoOnly, org);
        using var output = new OutputService(Images);
        var saved = await new FileOrganizationService(output).ExecuteAsync(confirmed, first.Metadata, false);
        Equal("RBD-487-cd1.mp4", Path.GetFileName(saved.VideoPaths[0]));
        Equal("RBD-487-cd2.mp4", Path.GetFileName(saved.VideoPaths[1]));
        Equal("SECOND INPUT", File.ReadAllText(saved.VideoPaths[0])); Equal("FIRST INPUT", File.ReadAllText(saved.VideoPaths[1]));
        Equal("U INPUT", File.ReadAllText(u)); Equal("UC INPUT", File.ReadAllText(uc));
    }

    private static async Task RegressionLegacyNames()
    {
        foreach (var moving in new[] { false, true })
        {
            using var temp = new Temp();
            var video = temp.Write("[DEMO-825] Synthetic legacy folder/DEMO-825.mp4", "KEEP VIDEO");
            var directory = Path.GetDirectoryName(video)!;
            var nfo = temp.Write("[DEMO-825] Synthetic legacy folder/movie.nfo", "<movie><id>DEMO-825</id><title>Before</title><unknown attr='retain'>KEEP</unknown></movie>");
            var poster = WriteImage(temp, "[DEMO-825] Synthetic legacy folder/poster.jpg", SKColors.Blue);
            var fanart = WriteImage(temp, "[DEMO-825] Synthetic legacy folder/fanart.jpg", SKColors.Green);
            var posterHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(poster)));
            var fanartHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fanart)));
            var before = TreeHashes(temp.Path);
            using var job = new MovieJob();
            var loaded = await MovieJobLoader.LoadAsync(job, video, Images);
            Equal(LocalMetadataLoadStatus.Loaded, loaded.MetadataStatus);
            Check(FileSystemPathSemantics.PathsEqual(nfo, loaded.Sidecars!.NfoPath!) && loaded.Sidecars.PosterPath == poster && loaded.Sidecars.FanartPath == fanart, "Folder sidecar ownership not loaded");
            job.Metadata.Title = "Approved synthetic edit";
            var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, moving ? new(true, true) : InPlace, job.CreateLocalSaveContext());
            Equal(moving ? "DEMO-825.nfo" : "movie.nfo", plan.OutputFileNames!.Nfo);
            Check(TreeHashes(temp.Path).OrderBy(p => p.Key).SequenceEqual(before.OrderBy(p => p.Key)), "Load/edit/preview wrote media");
            using var output = new OutputService(Images);
            var saved = await new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true);
            Equal("KEEP VIDEO", File.ReadAllText(saved.VideoPath));
            var xml = XDocument.Load(saved.Outputs.NfoPath!);
            Equal("KEEP", xml.Root!.Element("unknown")!.Value);
            Equal("retain", xml.Root.Element("unknown")!.Attribute("attr")!.Value);
            Equal("Approved synthetic edit", xml.Root.Element("title")!.Value);
            var posterTarget = moving ? Path.Combine(plan.TargetDirectory, "DEMO-825-poster.jpg") : poster;
            var fanartTarget = moving ? Path.Combine(plan.TargetDirectory, "DEMO-825-fanart.jpg") : fanart;
            Equal(posterHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(posterTarget))));
            Equal(fanartHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fanartTarget))));
            Check(!File.Exists(Path.Combine(directory, moving ? "DEMO-825.nfo" : "DEMO-825-poster.jpg")), "An unwanted duplicate sidecar layout was created");
        }
    }

    private static async Task RegressionFolderOwnership()
    {
        foreach (var kind in new[] { "single", "distinct", "cd" })
        {
            using var temp = new Temp();
            var video = temp.Write(kind == "cd" ? "DEMO-310-cd1.mp4" : "DEMO-310.mp4", "ONE");
            var other = kind == "single" ? null : temp.Write(kind == "cd" ? "DEMO-310-cd2.mp4" : "DEMO-311.mp4", "TWO");
            var nfo = temp.Write("movie.nfo", "<movie><id>DEMO-310</id><title>Shared folder baseline</title><unknown>KEEP</unknown></movie>");
            WriteImage(temp, "poster.jpg", SKColors.Red); WriteImage(temp, "fanart.jpg", SKColors.Blue);
            using var job = new MovieJob();
            var result = await MovieJobLoader.LoadAsync(job, video, Images);
            Equal(kind == "distinct" ? LocalMetadataLoadStatus.NoNfo : LocalMetadataLoadStatus.Loaded, result.MetadataStatus);
            if (kind == "distinct")
            {
                Check(result.Sidecars!.NfoPath is null && result.Sidecars.PosterPath is null && result.Sidecars.FanartPath is null, "Shared folder metadata polluted a distinct movie");
                using var next = new MovieJob();
                Equal(LocalMetadataLoadStatus.NoNfo, (await MovieJobLoader.LoadAsync(next, other!, Images)).MetadataStatus);
                temp.Write("DEMO-310.nfo", "<movie><title>First owned baseline</title></movie>");
                temp.Write("DEMO-311.nfo", "<movie><title>Second owned baseline</title></movie>");
                await MovieJobLoader.LoadAsync(job, video, Images); await MovieJobLoader.LoadAsync(next, other!, Images);
                Equal("First owned baseline", job.Metadata.Title); Equal("Second owned baseline", next.Metadata.Title);
            }
            else
            {
                Equal("Shared folder baseline", job.Metadata.Title);
                Check(FileSystemPathSemantics.PathsEqual(nfo, result.Sidecars!.NfoPath!), "Owned shared NFO was not used");
                Equal(kind == "cd" ? 2 : 1, job.VideoPaths.Count);
            }
        }
    }

    private static async Task RegressionExtrafanartCleanup()
    {
        foreach (var keepOtherContent in new[] { false, true })
        {
            using var temp = new Temp();
            var video = temp.Write("source/DEMO-172.mp4", "SYNTHETIC ORIGINAL");
            var sample = WriteImage(temp, "source/extrafanart/fanart1.jpg", SKColors.Red);
            var sourceFolder = Path.GetDirectoryName(video)!;
            var untouchedEmpty = Path.Combine(temp.Path, "unrelated/extrafanart"); Directory.CreateDirectory(untouchedEmpty);
            var note = keepOtherContent ? temp.Write("source/extrafanart/KEEP.txt", "KEEP THIS FILE") : null;
            using var job = new MovieJob(); await MovieJobLoader.LoadAsync(job, video, Images);
            var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, new(true, true), job.CreateLocalSaveContext());
            using var output = new OutputService(Images);
            await new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, false);
            Equal(keepOtherContent, Directory.Exists(Path.Combine(sourceFolder, "extrafanart")));
            Check(Directory.Exists(sourceFolder) && Directory.Exists(untouchedEmpty), "Cleanup removed a parent or unrelated directory");
            if (note is not null) Equal("KEEP THIS FILE", File.ReadAllText(note));
            Check(!File.Exists(sample), "Migrated original sample was not retired");
        }
    }

    private static async Task RegressionFilesystemIdentity()
    {
        using var temp = new Temp(Environment.GetEnvironmentVariable("JAVMETALITE_CASE_SENSITIVE_ROOT"));
        var source = temp.Write("Case/DEMO-410.mp4", "KNOWN ONE");
        var alias = Path.Combine(Path.GetDirectoryName(source)!, "demo-410.mp4");
        var insensitive = File.Exists(alias);
        if (insensitive)
        {
            var result = await MovieInputDiscovery.DiscoverAsync([source, alias]);
            Equal(1, result.MovieFileSets.Count);
            if (!OperatingSystem.IsWindows()) ThrowsSync<IOException>(() => FileSystemPathSemantics.ValidateUnambiguousPath(alias));
        }
        else
        {
            File.WriteAllText(alias, "KNOWN TWO");
            Equal(2, (await MovieInputDiscovery.DiscoverAsync([source, alias])).MovieFileSets.Count);
            if (OperatingSystem.IsMacOS())
                Check(MacFileIdentity.TryRead(source, out var firstId, out _) && MacFileIdentity.TryRead(alias, out var secondId, out _) && firstId != secondId,
                    "Case-sensitive siblings were not distinct native identities");
        }
        var composed = temp.Write("unicode/Caf\u00e9 DEMO-411.mp4", "UNICODE ONE");
        var decomposed = composed.Normalize(NormalizationForm.FormD);
        var normalizationInsensitive = File.Exists(decomposed);
        if (!normalizationInsensitive) File.WriteAllText(decomposed, "UNICODE TWO");
        Equal(normalizationInsensitive ? 1 : 2, (await MovieInputDiscovery.DiscoverAsync([composed, decomposed])).MovieFileSets.Count);
        var different = temp.Write("different/DEMO-410.mp4", "SAME ID DIFFERENT FILE");
        Equal(2, (await MovieInputDiscovery.DiscoverAsync([source, different])).MovieFileSets.Count);
        if (OperatingSystem.IsMacOS())
        {
            Check(MacFileIdentity.TryRead(source, out var originalIdentity, out _) &&
                  MacFileIdentity.TryRead(different, out var differentIdentity, out _) && originalIdentity != differentIdentity,
                  "Distinct real files share an identity unexpectedly");
            var hardLink = Path.Combine(temp.Path, "hardlink.mp4");
            var command = new System.Diagnostics.ProcessStartInfo("/bin/ln") { UseShellExecute = false };
            command.ArgumentList.Add(source); command.ArgumentList.Add(hardLink);
            using var process = System.Diagnostics.Process.Start(command)!; await process.WaitForExitAsync();
            Equal(0, process.ExitCode);
            Equal(1, (await MovieInputDiscovery.DiscoverAsync([source, hardLink])).MovieFileSets.Count);
            Equal("KNOWN ONE", File.ReadAllText(source)); Equal("KNOWN ONE", File.ReadAllText(hardLink));
            var oldMode = File.GetUnixFileMode(source);
            try
            {
                File.SetUnixFileMode(source, UnixFileMode.None);
                var denied = await MovieInputDiscovery.DiscoverAsync([source, hardLink]);
                Equal(2, denied.MovieFileSets.Count);
                Check(denied.Diagnostics.Any(d => d.Kind == MovieFileDiscoveryDiagnosticKind.AccessDenied), "Unavailable file identity was silently treated as known");
            }
            finally { File.SetUnixFileMode(source, oldMode); }
        }
        Console.WriteLine($"FILESYSTEM identity probe: caseInsensitive={insensitive}; normalizationInsensitive={normalizationInsensitive}; actual fixture volume only");
    }

    private static async Task RegressionSecondPartCommitFailure()
    {
        using var temp = new Temp();
        var paths = new[] { temp.Write("source/DEMO-510-cd1.mp4", "FIRST ORIGINAL"), temp.Write("source/DEMO-510-cd2.mp4", "SECOND ORIGINAL") };
        var nfo = temp.Write("source/movie.nfo", "<movie><title>Original</title><unknown>KEEP</unknown></movie>");
        using var job = new MovieJob(); await MovieJobLoader.LoadAsync(job, paths, Images);
        job.Metadata.Title = "Pending edit";
        var before = TreeHashes(Path.GetDirectoryName(paths[0])!);
        var plan = FileOrganizationService.BuildPlan(paths, job.Metadata, NfoOnly, new(true, true), job.CreateLocalSaveContext());
        var injected = false;
        var progress = new ImmediateProgress<FileTransactionProgress>(p =>
        {
            if (p.Stage == FileTransactionStage.Committing)
            { File.WriteAllText(plan.VideoTransfers[1].TargetPath, "EXTERNAL TARGET DO NOT OVERWRITE"); injected = true; }
        });
        using var output = new OutputService(Images);
        await Throws<IOException>(() => new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true, default, progress));
        Check(injected, "Second-part collision was not injected at commit");
        foreach (var pair in before) Equal(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(paths[0])!, pair.Key)))));
        Check(!File.Exists(plan.VideoTransfers[0].TargetPath), "First committed part was not rolled back");
        Equal("EXTERNAL TARGET DO NOT OVERWRITE", File.ReadAllText(plan.VideoTransfers[1].TargetPath));
        Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Failed second-part commit left unexplained staging");
    }

    private static async Task RegressionCancelCleanupFailure()
    {
        using var temp = new Temp();
        var video = temp.Write("source/DEMO-128.mp4", new string('k', 2 * 1024 * 1024));
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(video)));
        var metadata = new MovieMetadata { Id = "DEMO-128", Title = "Synthetic cancellation" };
        var plan = FileOrganizationService.BuildPlan(video, metadata, NfoOnly, new(true, true));
        using var cancellation = new CancellationTokenSource();
        using var output = new OutputService(Images);
        var service = new FileOrganizationService(output, path => Task.FromResult<TemporaryCleanupIssue?>(new(path, "Injected cleanup I/O failure")));
        TemporaryCleanupException? failure = null;
        try
        {
            await service.ExecuteAsync(plan, metadata, false, cancellation.Token,
                new ImmediateProgress<FileTransactionProgress>(p => { if (p.Stage == FileTransactionStage.CopyingMovie && p.BytesProcessed > 0) cancellation.Cancel(); }));
        }
        catch (TemporaryCleanupException error) { failure = error; }
        Check(failure is not null && failure.InnerException is OperationCanceledException, "Cleanup failure was incorrectly reported as fully safe cancellation");
        Check(failure!.Issues.Count > 0 && failure.Issues.All(issue => Directory.Exists(issue.Path) && issue.Error.Contains("Injected")), "Cleanup report lacks retained paths and error detail");
        Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(video))));
        Check(!File.Exists(plan.TargetVideoPath), "Canceled copy committed a target video");
    }

    private static async Task RegressionImageVolumeTransfer()
    {
        Check(OperatingSystem.IsMacOS(), "Mounted-image validation requires macOS");
        var root = Environment.GetEnvironmentVariable("JAVMETALITE_CASE_SENSITIVE_ROOT")!;
        foreach (var cancel in new[] { false, true })
        {
            using var source = new Temp(); using var target = new Temp(root);
            var video = source.Write("DEMO-512.mp4", new string('v', 16 * 1024 * 1024));
            source.Write("movie.nfo", "<movie><id>DEMO-512</id><title>Before</title><unknown>KEEP</unknown></movie>");
            var probe = target.Write("device-probe.txt", "UNRELATED TARGET FILE");
            Check(MacFileIdentity.TryRead(video, out var sourceId, out _) && MacFileIdentity.TryRead(probe, out var targetId, out _) &&
                  sourceId.Device != targetId.Device, "Source and image fixture are not different actual devices");
            Equal(Path.GetPathRoot(video), Path.GetPathRoot(probe)); // Both '/', despite distinct devices.
            var before = TreeHashes(source.Path);
            using var job = new MovieJob(); await MovieJobLoader.LoadAsync(job, video, Images);
            job.Metadata.Title = "Approved cross-device edit";
            var organization = new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, target.Path,
                CrossVolumeVerificationMode.FullSha256);
            var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, organization, job.CreateLocalSaveContext());
            Check(plan.VideoWillMove && OrganizationPathPlanner.RequiresVerifiedCopy(video, plan.TargetVideoPath), "Cross-device operation skipped verified copy");
            using var cancellation = new CancellationTokenSource();
            var copied = false; var verified = false;
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            {
                if (p.Stage == FileTransactionStage.CopyingMovie && p.BytesProcessed > 0)
                { copied = true; if (cancel) cancellation.Cancel(); }
                if (p.Stage == FileTransactionStage.VerifyingMovie && p.BytesProcessed == p.TotalBytes) verified = true;
            });
            using var output = new OutputService(Images);
            var service = new FileOrganizationService(output);
            if (cancel)
            {
                await Throws<OperationCanceledException>(() => service.ExecuteAsync(plan, job.Metadata, false, cancellation.Token, progress));
                Check(before.OrderBy(x => x.Key).SequenceEqual(TreeHashes(source.Path).OrderBy(x => x.Key)), "Canceled cross-device copy altered source files");
                Check(!File.Exists(plan.TargetVideoPath), "Canceled cross-device copy committed video");
            }
            else
            {
                var saved = await service.ExecuteAsync(plan, job.Metadata, false, cancellation.Token, progress);
                Check(verified && copied && !File.Exists(video), "Committed cross-device copy did not verify then retire source");
                Equal(before["DEMO-512.mp4"], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(saved.VideoPath))));
                Equal("KEEP", XDocument.Load(saved.Outputs.NfoPath!).Root!.Element("unknown")!.Value);
            }
            Check(copied, "Copy boundary was not exercised");
            Equal("UNRELATED TARGET FILE", File.ReadAllText(probe));
            Check(!Directory.EnumerateDirectories(source.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any() &&
                  !Directory.EnumerateDirectories(target.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Image-volume operation retained unexpected staging");
        }
        Console.WriteLine("CROSS-DEVICE PROBE: 16 MiB synthetic video; distinct native device IDs; full SHA success and copying cancellation; local mounted image, not physical external media");
    }
}
