using System.Net;
using System.Security.Cryptography;
using System.Xml.Linq;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using SkiaSharp;
using SaveOptions = JavMetaLite.Core.Models.SaveOptions;

internal static partial class Program
{
    private static string WriteImage(Temp temp, string relative, SKColor color)
    {
        var path = temp.Write(relative, "");
        using var bitmap = new SKBitmap(8, 4);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        File.WriteAllBytes(path, image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray());
        return FileSystemPathSemantics.CanonicalizePath(path);
    }

    private static async Task ArtworkOrganization()
    {
        using var temp = new Temp();
        var video = temp.Write("incoming/DEMO-081.mp4", "SYNTHETIC VIDEO WITH ARTWORK");
        var cover = WriteImage(temp, "covers/cover.jpg", SKColors.CornflowerBlue);
        var sample = WriteImage(temp, "covers/sample.jpg", SKColors.Coral);
        var metadata = new MovieMetadata { Id = "DEMO-081", Title = "Artwork transaction", CoverUrl = cover,
            ScreenshotUrls = [sample, sample] };
        var options = new SaveOptions(true, true, true, true, false, false);
        var plan = FileOrganizationService.BuildPlan(video, metadata, options, new(true, true));
        using var output = new OutputService(Images);
        var result = await new FileOrganizationService(output).ExecuteAsync(plan, metadata, false);
        Check(result.VideoMoved && !File.Exists(video), "Verified move did not retire source");
        Equal("SYNTHETIC VIDEO WITH ARTWORK", File.ReadAllText(result.VideoPath));
        Equal((4, 4), Images.GetDimensions(File.ReadAllBytes(result.Outputs.PosterPath!)));
        Check(File.ReadAllBytes(cover).SequenceEqual(File.ReadAllBytes(result.Outputs.FanartPath!)), "Original JPEG fanart changed");
        Equal(1, result.Outputs.ExtrafanartPaths.Count);
        Check(File.ReadAllBytes(sample).SequenceEqual(File.ReadAllBytes(result.Outputs.ExtrafanartPaths[0])), "Sample bytes changed");
        var xml = XDocument.Load(result.Outputs.NfoPath!);
        Check(xml.ToString().Contains(Path.GetFileName(result.Outputs.PosterPath!)), "NFO omitted generated poster reference");
        Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Successful transaction left staging");
    }

    private static async Task PreservedSidecarMigration()
    {
        using var temp = new Temp();
        var video = temp.Write("incoming/DEMO-082.mp4", "ORIGINAL VIDEO");
        temp.Write("incoming/movie.nfo", "<movie><title>Preserved</title><custom>KEEP</custom></movie>");
        WriteImage(temp, "incoming/poster.jpg", SKColors.Blue);
        WriteImage(temp, "incoming/fanart.jpg", SKColors.Green);
        WriteImage(temp, "incoming/extrafanart/sample-original.jpg", SKColors.Red);
        using var job = new MovieJob();
        await MovieJobLoader.LoadAsync(job, video, Images);
        var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, new(true, true), job.CreateLocalSaveContext());
        var preserved = plan.SidecarTransfers.ToDictionary(t => t.DestinationPath, t => (t.SourcePath, Bytes: File.ReadAllBytes(t.SourcePath)));
        Check(preserved.Count >= 3, "Preserved local sidecars were omitted from migration");
        using var output = new OutputService(Images);
        var saved = await new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, false);
        foreach (var (destination, original) in preserved)
        {
            Check(original.Bytes.SequenceEqual(File.ReadAllBytes(destination)), "Preserved sidecar content changed");
            Check(!File.Exists(original.SourcePath), "Migrated sidecar original was not retired");
        }
        Equal("ORIGINAL VIDEO", File.ReadAllText(saved.VideoPath));
        Check(!Directory.Exists(Path.Combine(Path.GetDirectoryName(video)!, "extrafanart")), "Empty migrated extrafanart directory retained");
    }

    private static async Task LateVideoMutation()
    {
        if (OperatingSystem.IsWindows()) return; // This exercises the enabled Unix verified-copy path.
        foreach (var multipart in new[] { false, true })
        {
            using var temp = new Temp();
            var first = temp.Write(multipart ? "incoming/DEMO-083-cd1.mp4" : "incoming/DEMO-083.mp4", "ORIGINAL FIRST");
            var paths = multipart ? new[] { first, temp.Write("incoming/DEMO-083-cd2.mp4", "ORIGINAL SECOND") } : [first];
            var metadata = new MovieMetadata { Id = "DEMO-083", Title = "Concurrent editor" };
            var plan = FileOrganizationService.BuildPlan(paths, metadata, NfoOnly, new(true, true));
            var changed = plan.VideoTransfers.Last();
            var original = File.ReadAllText(changed.SourcePath);
            var progress = new ImmediateProgress<FileTransactionProgress>(p =>
            {
                if (p.Stage == FileTransactionStage.RetiringSource) File.WriteAllText(changed.SourcePath, "EXTERNAL REPLACEMENT");
            });
            using var output = new OutputService(Images);
            await Throws<FileRecoveryException>(() => new FileOrganizationService(output).ExecuteAsync(plan, metadata, false, default, progress));
            Equal("EXTERNAL REPLACEMENT", File.ReadAllText(changed.SourcePath));
            Equal(original, File.ReadAllText(changed.TargetPath));
            Check(Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Recovery staging was discarded");
            if (multipart)
            {
                Equal("ORIGINAL FIRST", File.ReadAllText(first));
                Check(!File.Exists(plan.VideoTransfers[0].TargetPath), "Unchanged first source was not safely restored");
            }
        }
    }

    private static async Task LateSidecarMutation()
    {
        using var temp = new Temp();
        var video = temp.Write("incoming/DEMO-084.mp4", new string('v', 2 * 1024 * 1024));
        var nfo = temp.Write("incoming/movie.nfo", "<movie><title>Before</title></movie>");
        using var job = new MovieJob();
        await MovieJobLoader.LoadAsync(job, video, Images);
        job.Metadata.Title = "Intended edit";
        var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, new(true, true), job.CreateLocalSaveContext());
        var changed = false;
        var progress = new ImmediateProgress<FileTransactionProgress>(p =>
        {
            if (p.Stage == FileTransactionStage.CopyingMovie && !changed)
            { changed = true; File.WriteAllText(nfo, "<movie><title>External edit during copy</title></movie>"); }
        });
        using var output = new OutputService(Images);
        await Throws<IOException>(() => new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true, default, progress));
        Check(changed, "Copy-stage mutation was not exercised");
        Equal("<movie><title>External edit during copy</title></movie>", File.ReadAllText(nfo));
        Check(File.Exists(video) && !File.Exists(plan.TargetVideoPath), "Failed transaction moved video");
        Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Restored failure left staging");
    }

    private static async Task MultipartCommitCancellation()
    {
        using var temp = new Temp();
        var paths = new[] { temp.Write("incoming/DEMO-085-cd1.mp4", "FIRST"), temp.Write("incoming/DEMO-085-cd2.mp4", "SECOND") };
        var nfo = temp.Write("incoming/movie.nfo", "<movie><title>Original metadata</title></movie>");
        using var job = new MovieJob();
        await MovieJobLoader.LoadAsync(job, paths, Images);
        var plan = FileOrganizationService.BuildPlan(paths, job.Metadata, NfoOnly, new(true, true), job.CreateLocalSaveContext());
        using var token = new CancellationTokenSource();
        var observedCommittedVideos = false;
        var progress = new ImmediateProgress<FileTransactionProgress>(p =>
        {
            if (p.Stage == FileTransactionStage.RetiringSource)
            { observedCommittedVideos = plan.VideoTransfers.All(t => File.Exists(t.TargetPath)); token.Cancel(); }
        });
        using var output = new OutputService(Images);
        await Throws<OperationCanceledException>(() => new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, false, token.Token, progress));
        Check(observedCommittedVideos, "Cancellation did not exercise post-commit rollback");
        Equal("FIRST", File.ReadAllText(paths[0])); Equal("SECOND", File.ReadAllText(paths[1]));
        Equal("<movie><title>Original metadata</title></movie>", File.ReadAllText(nfo));
        Check(plan.VideoTransfers.All(t => !File.Exists(t.TargetPath)), "Canceled multipart left committed targets");
        Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Canceled multipart left staging");
    }

    private static async Task BatchMidCopyCancellation()
    {
        using var temp = new Temp();
        var jobs = Enumerable.Range(0, 3).Select(_ => new MovieJob()).ToArray();
        using var token = new CancellationTokenSource();
        try
        {
            var plans = jobs.Select((job, i) =>
            {
                var video = temp.Write($"incoming/DEMO-09{i}.mp4", new string((char)('a' + i), 2 * 1024 * 1024));
                job.ResetForVideo(video, $"DEMO-09{i}");
                var organization = new OrganizationOptions(true, true);
                job.InitializeSaveConfiguration(new(NfoOnly, organization));
                return new PreparedMovieSave(job, FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, organization), job.ReviewRevision, false);
            }).ToArray();
            using var output = new OutputService(Images);
            var service = new FileOrganizationService(output);
            var result = await BatchSaveCoordinator.ExecuteAsync(plans, (item, ct) => service.ExecuteAsync(item.Plan, item.Job.Metadata, false, ct,
                new ImmediateProgress<FileTransactionProgress>(p =>
                { if (item.Job == jobs[1] && p.Stage == FileTransactionStage.CopyingMovie && p.BytesProcessed > 0) token.Cancel(); })), token.Token);
            Equal(1, result.CompletedCount); Equal(1, result.CanceledCount); Equal(1, result.NotStartedCount);
            Check(File.Exists(plans[0].Plan.TargetVideoPath) && !File.Exists(plans[0].Plan.SourceVideoPath), "First completed movie did not remain committed");
            foreach (var item in plans.Skip(1))
                Check(File.Exists(item.Plan.SourceVideoPath) && !File.Exists(item.Plan.TargetVideoPath), "Canceled/not-started movie changed");
        }
        finally { foreach (var job in jobs) job.Dispose(); }
    }

    private static async Task PostCommitWarning()
    {
        using var temp = new Temp();
        using var first = new MovieJob(); using var second = new MovieJob();
        var prepared = new[] { first, second }.Select((job, i) =>
        {
            var video = temp.Write($"DEMO-10{i}.mp4", "SYNTHETIC");
            job.ResetForVideo(video, $"DEMO-10{i}"); job.InitializeSaveConfiguration(new(NfoOnly, InPlace));
            return new PreparedMovieSave(job, FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, InPlace), job.ReviewRevision, false);
        }).ToArray();
        using var output = new OutputService(Images);
        var result = await BatchSaveCoordinator.ExecuteAsync(prepared, async (item, ct) =>
        {
            var saved = await new FileOrganizationService(output).ExecuteAsync(item.Plan, item.Job.Metadata, false, ct);
            return saved.WithAdditionalCleanupIssues([new(Path.Combine(temp.Path, "journal.json"), "Synthetic post-commit journal failure")]);
        });
        Equal(1, result.CompletedCount); Equal(0, result.FailedCount); Equal(1, result.NotStartedCount);
        Equal(MovieSaveState.Completed, first.SaveState);
        Check(File.Exists(result.Items[0].SaveResult!.Outputs.NfoPath), "Journal warning undid committed output");
    }

    private static async Task ArtworkBodyTimeout()
    {
        using var temp = new Temp();
        var video = temp.Write("DEMO-101.mp4", "SYNTHETIC");
        var nfo = temp.Write("DEMO-101.nfo", "<movie><title>KEEP NFO</title></movie>");
        var metadata = new MovieMetadata { Id = "DEMO-101", Title = "Must not commit", CoverUrl = "https://example.invalid/stall.jpg" };
        var options = new SaveOptions(true, true, false, false, false, true);
        var plan = FileOrganizationService.BuildPlan(video, metadata, options, InPlace);
        using var http = new HttpClient(new StalledImageHandler()) { Timeout = TimeSpan.FromMilliseconds(75) };
        using var output = new OutputService(Images, http);
        await Throws<InvalidDataException>(() => new FileOrganizationService(output).ExecuteAsync(plan, metadata, true).WaitAsync(TimeSpan.FromSeconds(5)));
        Equal("<movie><title>KEEP NFO</title></movie>", File.ReadAllText(nfo));
        Check(!File.Exists(Path.Combine(temp.Path, "DEMO-101-poster.jpg")), "Stalled image committed partial output");
        Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Timed-out artwork left staging");
    }

    private static async Task SampleReplacementFailure()
    {
        using var temp = new Temp();
        var video = temp.Write("DEMO-102.mp4", "SYNTHETIC");
        var sample = WriteImage(temp, "extrafanart/original.jpg", SKColors.Purple);
        var original = File.ReadAllBytes(sample);
        var metadata = new MovieMetadata { Id = "DEMO-102", Title = "Replacement", ScreenshotUrls = ["https://example.invalid/missing.jpg"] };
        var context = new LocalSaveContext(null, null, null) { LocalExtrafanartPaths = [sample], CanReplaceLocalExtrafanart = true };
        var options = new SaveOptions(true, false, false, true, false, true, ReplaceLocalExtrafanart: true);
        var plan = FileOrganizationService.BuildPlan(video, metadata, options, InPlace, context);
        using var http = new HttpClient(new ReplyHandler(HttpStatusCode.NotFound, "missing"));
        using var output = new OutputService(Images, http);
        await Throws<InvalidDataException>(() => new FileOrganizationService(output).ExecuteAsync(plan, metadata, true));
        Check(original.SequenceEqual(File.ReadAllBytes(sample)), "Failed replacement retired local original");
        Check(!File.Exists(Path.ChangeExtension(video, ".nfo")), "Failed sample replacement committed NFO");
    }

    private static async Task DuplicateGroupIsolation()
    {
        using var temp = new Temp();
        temp.Write("DEMO-103-cd1.mp4", "PART ONE");
        temp.Write("DEMO-103-cd1.mkv", "AMBIGUOUS PART ONE");
        var valid = temp.Write("DEMO-104.mp4", "UNRELATED");
        var result = await MovieInputDiscovery.DiscoverAsync([temp.Path]);
        Equal(1, result.MovieFileSets.Count);
        Check(FileSystemPathSemantics.PathsEqual(valid, result.MovieFileSets[0].PrimaryPath), "Unrelated valid movie discarded");
        Check(result.Diagnostics.Any(d => d.Message.Contains("CD1")), "Duplicate CD ambiguity was not reported");
    }

    private static async Task ArtworkSymlink()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new Temp();
        var video = temp.Write("DEMO-105.mp4", "SYNTHETIC");
        var poster = WriteImage(temp, "DEMO-105-poster.jpg", SKColors.Gold);
        var foreign = WriteImage(temp, "foreign/keep.jpg", SKColors.Teal);
        using var job = new MovieJob();
        await MovieJobLoader.LoadAsync(job, video, Images);
        File.Delete(poster); File.CreateSymbolicLink(poster, foreign);
        await Throws<IOException>(() => { FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, new(true, true), job.CreateLocalSaveContext()); return Task.CompletedTask; });
        Check(File.Exists(foreign), "Foreign symlink target was removed");
    }

    private static async Task RefreshUnsavedBaseline()
    {
        using var temp = new Temp();
        var video = temp.Write("incoming/DEMO-106.mp4", "SYNTHETIC");
        temp.Write("incoming/movie.nfo", "<movie><title>Local baseline</title><custom>KEEP UNKNOWN</custom><actor><name>Local actor</name><thumb>local.jpg</thumb></actor></movie>");
        WriteImage(temp, "incoming/fanart.jpg", SKColors.Cyan);
        using var job = new MovieJob();
        await MovieJobLoader.LoadAsync(job, video, Images);
        var online = new MovieMetadata { Id = "DEMO-106", Title = "Provider title", SourceName = "libredmm", SourceDisplayName = "LibreDMM",
            ActorsText = "Online actor", Actors = [new("Online actor", "https://example.invalid/actor-new.jpg")],
            ContentId = "provider-content", SourceUrl = "https://example.invalid/movie", ScreenshotUrls = ["https://example.invalid/sample.jpg"] };
        job.ApplyRetriedOnlineSources(online, [online], []);
        job.MetadataReview.SetManualValue(MetadataField.Title, "Unsaved manual title");
        job.Metadata.Series = "Unsaved series metadata";
        var metadata = job.Metadata;
        var review = job.MetadataReview;
        var artwork = job.ArtworkReview;
        var sources = job.SourceResults;
        var titleCandidate = review.GetSelectedCandidate(MetadataField.Title);
        var actorsCandidate = review.GetSelectedCandidate(MetadataField.Actors);
        var reviewedJson = System.Text.Json.JsonSerializer.Serialize(metadata);
        var plan = FileOrganizationService.BuildPlan(video, metadata,
            new SaveOptions(false, false, true, false, false, false), new(true, true), job.CreateLocalSaveContext());
        using var output = new OutputService(Images);
        var service = new FileOrganizationService(output);
        var saved = await service.ExecuteAsync(plan, metadata, false);
        using var fresh = new MovieJob();
        await MovieJobLoader.LoadAsync(fresh, saved.VideoPaths, Images);
        job.UpdateVideoPaths(saved.VideoPaths);
        var revision = job.ReviewRevision;
        job.RefreshLocalMetadataBaseline(fresh.LocalMetadataBundle);
        Check(job.ReviewRevision > revision, "Baseline refresh did not invalidate the old review revision");
        Check(ReferenceEquals(metadata, job.Metadata) && ReferenceEquals(review, job.MetadataReview) && ReferenceEquals(artwork, job.ArtworkReview), "Baseline refresh rebuilt current review");
        Check(ReferenceEquals(sources, job.SourceResults), "Provider candidates discarded");
        Check(ReferenceEquals(titleCandidate, job.MetadataReview.GetSelectedCandidate(MetadataField.Title)) && ReferenceEquals(actorsCandidate, job.MetadataReview.GetSelectedCandidate(MetadataField.Actors)), "Reviewed candidate selections changed");
        Equal(reviewedJson, System.Text.Json.JsonSerializer.Serialize(job.Metadata));
        Check(FileSystemPathSemantics.PathsEqual(saved.VideoPath, job.LocalMetadataBundle!.Sidecars.VideoPath), "NFO baseline still points at retired source");
        Equal("Local baseline", job.LocalSourceMetadata!.Title);
        Equal("https://example.invalid/actor-new.jpg", job.Metadata.Actors.Single().ImageUrl);
        // Artwork paths have their own refresh; the baseline method does not rewrite selections.
        job.SetLocalArtwork(fresh.LocalArtworkCandidate);
        job.SetLocalExtrafanart(fresh.LocalExtrafanartPaths);
        var nfoPlan = FileOrganizationService.BuildPlan(job.VideoPaths, job.Metadata, NfoOnly, InPlace, job.CreateLocalSaveContext());
        var nfoSaved = await service.ExecuteAsync(nfoPlan, job.Metadata, true);
        var xml = XDocument.Load(nfoSaved.Outputs.NfoPath!);
        Equal("Unsaved manual title", xml.Root!.Element("title")!.Value);
        Equal("KEEP UNKNOWN", xml.Root.Element("custom")!.Value);
        Check(xml.ToString().Contains("https://example.invalid/actor-new.jpg"), "Later NFO save lost reviewed structured actor data");
    }

    private sealed class StalledImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(new StalledStream()) };
            response.Content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(response);
        }
    }

    private static R18RequestScheduler ImmediateR18Scheduler() => new(TimeSpan.Zero, TimeSpan.Zero,
        () => DateTimeOffset.UtcNow, Task.Delay);

    private static async Task R18Response()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new InspectableHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            if (url.Contains("combined=demoactual001")) return JsonReply(Fixture("r18dev.json"));
            if (url.Contains("dvd_id=demo001")) return JsonReply("{\"content_id\":\"demoactual001\",\"title\":\"Compact fallback\"}");
            return new(HttpStatusCode.NotFound);
        }));
        using var provider = new R18DevClient(http, ImmediateR18Scheduler());
        var metadata = await provider.SearchAsync("DEMO-001");
        Equal(3, requests.Count);
        Check(requests[0].Contains("combined=demo00001") && requests[1].Contains("dvd_id=demo001") && requests[2].Contains("combined=demoactual001"), "R18 actual content-ID fallback order changed");
        Equal("Synthetic R18 title", metadata.Title); Equal("合成資料のタイトル", metadata.OriginalTitle);
        Equal("120", metadata.RuntimeMinutes); Equal("demoactual001", metadata.ContentId);
        Equal("Synthetic actor", metadata.Actors.Single().Name);
        Equal("https://example.invalid/synthetic-actor.jpg", metadata.Actors.Single().ImageUrl);
        Equal("r18dev", metadata.SourceName);
        Check(metadata.ScreenshotUrls.Count > 0, "R18 sample candidate omitted");
        Check(metadata.CoverUrl.EndsWith("demoactual001pl.jpg"), "R18 complete-cover candidate omitted");
    }

    private static async Task MultiSourceReviewRetry()
    {
        using var temp = new Temp();
        using var job = new MovieJob();
        job.ResetForVideo(temp.Write("DEMO-001.mp4", "SYNTHETIC"), "DEMO-001");
        job.MetadataReview.SetManualValue(MetadataField.Title, "Retain manual choice");
        var libreCalls = 0;
        var r18Calls = 0;
        var libreJson = Fixture("libredmm.json").TrimEnd().TrimEnd('}') + ",\"cover_image_url\":\"https://example.invalid/libre-cover.jpg\"}";
        using var libreHttp = new HttpClient(new InspectableHandler(_ => { libreCalls++; return JsonReply(libreJson); }));
        using var limitedHttp = new HttpClient(new InspectableHandler(_ =>
        {
            r18Calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(120));
            return response;
        }));
        using var libre = new LibreDmmClient(libreHttp);
        using var limited = new R18DevClient(limitedHttp, ImmediateR18Scheduler());
        job.BeginSearch();
        var partial = await MetadataSearchCoordinator.SearchAllAsync("DEMO-001", libre, limited);
        Equal(1, partial.Attempts.Count(a => a.Success));
        Check(partial.Attempts.Single(a => !a.Success).Error is MetadataSourceRateLimitException, "R18 rate limit lost its classification");
        job.ApplyRetriedOnlineSources(partial.Metadata, partial.Sources, partial.Attempts);
        Equal("Retain manual choice", job.Metadata.Title);
        Check(job.HasPartialSourceFailure, "Available provider result was discarded on partial failure");
        Check(job.SelectArtworkSource("libredmm"), "Successful source artwork was unavailable");
        var chosenCover = job.Metadata.CoverUrl;
        using var retryHttp = new HttpClient(new InspectableHandler(_ => { r18Calls++; return JsonReply(Fixture("r18dev.json")); }));
        using var recovered = new R18DevClient(retryHttp, ImmediateR18Scheduler());
        job.BeginSearch(preserveAttempts: true);
        var retried = await MetadataSearchCoordinator.RetryFailedAsync("DEMO-001", partial.Attempts, [recovered]);
        job.ApplyRetriedOnlineSources(retried.Metadata, retried.Sources, retried.Attempts);
        Equal(1, libreCalls); Equal(2, r18Calls);
        Equal("Retain manual choice", job.Metadata.Title);
        Equal(chosenCover, job.Metadata.CoverUrl);
        Equal("libredmm", job.ArtworkReview.SelectedCandidate!.Source.Name);
        Check(!job.HasFailedSources && job.SourceResults.Any(s => s.SourceName == "libredmm") && job.SourceResults.Any(s => s.SourceName == "r18dev"), "Retry lost successful source history");
        var revision = job.ReviewRevision;
        Check(job.MetadataReview.SelectCandidate(MetadataField.Actors, "r18dev"), "Recovered provider actor candidate missing");
        Equal("https://example.invalid/synthetic-actor.jpg", job.Metadata.Actors.Single().ImageUrl);
        Check(job.ReviewRevision > revision, "Source review did not invalidate existing preview revision");
    }

    private static HttpResponseMessage JsonReply(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class InspectableHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); var result = reply(request); result.RequestMessage = request; return Task.FromResult(result); }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
