using System.IO;
using System.Net;
using System.Net.Http;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal static class LocalSampleSafetyRegressionTests
{
    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("localsamples", "单片及多 CD 保留超过 50 张本地图，在线下载单独限制 50 张", TestLocalImagesDoNotConsumeOnlineLimit),
        new("localsamples", "达到在线上限后仍保留后续本地选择及原始顺序", TestLocalSelectionAfterOnlineLimit),
        new("localsamples", "普通保存本地样张读取失败必须停止并保留单片及多 CD 原文件", TestLocalReadFailureAbortsSave)
    ];

    private static string Url(int index) => $"https://local-safety.invalid/{index}.jpg";
    private static readonly SaveOptions SamplesOnly = new(false, false, false, true, true, true);

    private static async Task TestLocalImagesDoNotConsumeOnlineLimit()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var workspace = new TestWorkspace("local-sample-limit");
            var videos = CreateVideos(workspace, multipart);
            var videoHashes = videos.Select(AssertEx.Sha256).ToArray();
            var local = Enumerable.Range(0, 51).Select(i => workspace.WriteFile(
                $"extrafanart/fanart{i + 1}.jpg", TestImageFactory.CreateJpeg(100 + i, 80))).ToArray();
            var localHashes = local.Select(AssertEx.Sha256).ToArray();
            using var job = new MovieJob();
            await MovieJobLoader.LoadAsync(job, videos);
            var online = new MovieMetadata
            {
                Id = "SAFE-551", Title = "Synthetic sample preservation", SourceName = "libredmm",
                ScreenshotUrls = Enumerable.Range(0, 60).Select(Url).ToArray()
            };
            job.ApplyOnlineSources(online, [online]);
            AssertEx.True(job.CanReplaceLocalExtrafanart, "Fixture did not establish online candidates.");
            var plan = FileOrganizationService.BuildPlan(job.VideoPaths, job.Metadata, SamplesOnly,
                new(false, false), job.CreateLocalSaveContext());
            var plannedSamples = plan.Changes.Count(change =>
                Path.GetFileName(Path.GetDirectoryName(change.DestinationPath)) == "extrafanart" &&
                change.Kind is PlannedChangeKind.CreateFile or PlannedChangeKind.ReplaceImage);
            AssertEx.Equal(101, plannedSamples, "Preview truncated local files or counted more than 50 online selections.");
            using var handler = new LocalSampleHandler();
            using var client = new HttpClient(handler);
            using var output = new OutputService(client);
            var result = await new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true);
            AssertEx.Equal(50, handler.SampleRequests);
            AssertEx.Equal(101, result.Outputs.ExtrafanartPaths.Count);
            for (var i = 0; i < localHashes.Length; i++)
                AssertEx.Equal(localHashes[i], AssertEx.Sha256(result.Outputs.ExtrafanartPaths[i]), "A selected local original was lost or changed.");
            for (var i = 0; i < result.VideoPaths.Count; i++)
                AssertEx.Equal(videoHashes[i], AssertEx.Sha256(result.VideoPaths[i]));
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static async Task TestLocalSelectionAfterOnlineLimit()
    {
        using var workspace = new TestWorkspace("interleaved-local-limit");
        var video = CreateVideos(workspace, false)[0];
        var first = workspace.WriteFile("first.jpg", TestImageFactory.CreateJpeg(90, 70));
        var last = workspace.WriteFile("last.jpg", TestImageFactory.CreateJpeg(91, 70));
        var metadata = new MovieMetadata
        {
            Id = "SAFE-551", ScreenshotUrls = new[] { first }.Concat(Enumerable.Range(0, 60).Select(Url)).Append(last).ToArray()
        };
        AssertEx.Equal(52, OutputService.GetExpectedOutputFiles(video, metadata, SamplesOnly).Count);
        using var handler = new LocalSampleHandler();
        using var client = new HttpClient(handler);
        using var output = new OutputService(client);
        var result = await output.SaveAsync(video, metadata, SamplesOnly);
        AssertEx.Equal(50, handler.SampleRequests);
        AssertEx.Equal(52, result.ExtrafanartPaths.Count);
        AssertEx.Equal(AssertEx.Sha256(first), AssertEx.Sha256(result.ExtrafanartPaths[0]));
        AssertEx.Equal(AssertEx.Sha256(last), AssertEx.Sha256(result.ExtrafanartPaths[^1]));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestLocalReadFailureAbortsSave()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var workspace = new TestWorkspace("locked-local-sample");
            var videos = CreateVideos(workspace, multipart);
            var local = workspace.WriteFile("extrafanart/fanart1.jpg", TestImageFactory.CreateJpeg(100, 80));
            var nfo = workspace.WriteFile(multipart ? "movie.nfo" : "SAFE-551.nfo", "<movie><title>Original title</title></movie>"u8.ToArray());
            var originals = videos.Concat([local, nfo]).ToDictionary(path => path, AssertEx.Sha256);
            using var job = new MovieJob();
            await MovieJobLoader.LoadAsync(job, videos);
            var online = new MovieMetadata
            {
                Id = "SAFE-551", Title = "Updated title", SourceName = "libredmm",
                CoverUrl = "https://local-safety.invalid/cover.jpg", ScreenshotUrls = [Url(0)]
            };
            job.ApplyOnlineSources(online, [online]);
            job.Metadata.Title = "Updated title";
            var options = SamplesOnly with { WriteNfo = true, DownloadFanart = true };
            var plan = FileOrganizationService.BuildPlan(job.VideoPaths, job.Metadata, options,
                new(false, false), job.CreateLocalSaveContext());
            using var handler = new LocalSampleHandler(local);
            using var client = new HttpClient(handler);
            using var output = new OutputService(client);
            try
            {
                await AssertEx.ThrowsAsync<IOException>(
                    () => new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true),
                    "A local read failure was skipped and the transaction retired its original.");
                AssertEx.True(handler.LocalLockAcquired, "Fixture did not lock the local sample after preflight.");
            }
            finally { handler.ReleaseLocalLock(); }
            foreach (var original in originals)
                AssertEx.Equal(original.Value, AssertEx.Sha256(original.Key));
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static string[] CreateVideos(TestWorkspace workspace, bool multipart) => multipart
        ? [workspace.WriteFile("SAFE-551-cd1.mp4", [1, 2, 3]), workspace.WriteFile("SAFE-551-cd2.mp4", [4, 5, 6])]
        : [workspace.WriteFile("SAFE-551.mp4", [1, 2, 3])];

    private sealed class LocalSampleHandler(string? lockLocalPath = null) : HttpMessageHandler
    {
        private FileStream? _held;
        public int SampleRequests { get; private set; }
        public bool LocalLockAcquired { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "local-safety.invalid")
                throw new InvalidOperationException("No live HTTP is allowed.");
            token.ThrowIfCancellationRequested();
            var cover = request.RequestUri.AbsolutePath == "/cover.jpg";
            if (cover && lockLocalPath is not null)
            {
                // Cover reads start after the organizer's first source-fingerprint check.
                _held = new FileStream(lockLocalPath, FileMode.Open, FileAccess.Read, FileShare.None);
                LocalLockAcquired = true;
            }
            if (!cover)
            {
                SampleRequests++;
                // The old code skipped the preceding failed local read; releasing here
                // lets its second fingerprint check succeed and exposes the data loss.
                ReleaseLocalLock();
            }
            var index = cover ? 0 : int.Parse(Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(TestImageFactory.CreateJpeg(300 + index, 200))
            };
            response.Content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(response);
        }

        public void ReleaseLocalLock() { _held?.Dispose(); _held = null; }
        protected override void Dispose(bool disposing) { ReleaseLocalLock(); base.Dispose(disposing); }
    }
}
