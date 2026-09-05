using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal static class OutputDownloadRegressionTests
{
    public static IReadOnlyList<RegressionTestCase> All { get; } =
    [
        new("downloads", "有界并发、乱序完成、稳定去重与 50 张上限", TestOrderAndLimit),
        new("downloads", "普通保存跳过失败、坏响应及限流图片，不额外重试", TestPartialFailure),
        new("downloads", "混合本地和在线选择保持图片顺序与去重", TestMixedLocations),
        new("downloads", "单片及多 CD 全面替换部分失败保持原文件", TestReplacementFailure),
        new("downloads", "响应体下载中取消会排空请求并保留单片及多 CD 原文件", TestCancelBody),
        new("downloads", "不可恢复失败取消同批请求并阻止后续影片", TestFatalStopsBatch)
    ];

    private static string Url(int index) => $"https://downloads.invalid/{index}.jpg";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static readonly SaveOptions SamplesOnly = new(false, false, false, true, true, true);

    private static async Task TestOrderAndLimit()
    {
        using var workspace = new TestWorkspace("ordered-downloads");
        var source = workspace.WriteFile("TEST-501.mp4", [1, 2, 3]);
        var images = Enumerable.Range(0, 60).Select(i => TestImageFactory.CreateJpeg(100 + i, 80)).ToArray();
        using var handler = new SampleHandler(images);
        using var client = new HttpClient(handler);
        using var service = new OutputService(client);
        var metadata = new MovieMetadata { Id = "TEST-501", ScreenshotUrls = Enumerable.Range(0, 60).Select(Url).ToArray() };
        var result = await service.SaveAsync(source, metadata, SamplesOnly);
        AssertEx.Equal(50, handler.Calls);
        AssertEx.Equal(3, handler.Peak);
        AssertEx.Equal(0, handler.Pending);
        AssertEx.True(handler.Completed.First() != 0, "Fixture did not complete out of order.");
        // Sample 1 has the same bytes as sample 0; the first appearance owns its position.
        var expected = Enumerable.Range(0, 50).Where(i => i != 1).ToArray();
        AssertEx.Equal(expected.Length, result.ExtrafanartPaths.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            AssertEx.Equal($"fanart{i + 1}.jpg", Path.GetFileName(result.ExtrafanartPaths[i]));
            AssertEx.Equal(Hash(images[expected[i]]), AssertEx.Sha256(result.ExtrafanartPaths[i]));
        }
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestPartialFailure()
    {
        using var workspace = new TestWorkspace("partial-downloads");
        var source = workspace.WriteFile("TEST-502.mp4", [1, 2, 3]);
        var images = Enumerable.Range(0, 8).Select(i => TestImageFactory.CreateJpeg(100 + i, 80)).ToArray();
        using var handler = new SampleHandler(images, failures: true);
        using var client = new HttpClient(handler);
        using var output = new OutputService(client);
        var metadata = new MovieMetadata { Id = "TEST-502", ScreenshotUrls = Enumerable.Range(0, 8).Select(Url).ToArray() };
        var result = await output.SaveAsync(source, metadata, SamplesOnly);
        // 2=404, 3=wrong MIME, 4=tiny body, 5=429. 1 duplicates 0; 6 and 7 succeed.
        AssertEx.Equal(3, result.ExtrafanartPaths.Count);
        foreach (var pair in new[] { (0, 0), (1, 6), (2, 7) })
            AssertEx.Equal(Hash(images[pair.Item2]), AssertEx.Sha256(result.ExtrafanartPaths[pair.Item1]));
        AssertEx.Equal(8, handler.Calls);
        AssertEx.Equal(0, handler.Pending);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task TestMixedLocations()
    {
        using var workspace = new TestWorkspace("mixed-downloads");
        var source = workspace.WriteFile("TEST-503.mp4", [1, 2, 3]);
        var images = Enumerable.Range(0, 4).Select(i => TestImageFactory.CreateJpeg(100 + i, 80)).ToArray();
        var local = workspace.WriteFile("local.jpg", images[2]);
        using var handler = new SampleHandler(images);
        using var client = new HttpClient(handler);
        using var output = new OutputService(client);
        var metadata = new MovieMetadata { Id = "TEST-503", ScreenshotUrls = [local, Url(0), Url(2), Url(3)] };
        var result = await output.SaveAsync(source, metadata, SamplesOnly);
        AssertEx.Equal(3, result.ExtrafanartPaths.Count);
        var expected = new[] { 2, 0, 3 };
        for (var i = 0; i < 3; i++) AssertEx.Equal(Hash(images[expected[i]]), AssertEx.Sha256(result.ExtrafanartPaths[i]));
        AssertEx.Equal(3, handler.Calls);
        AssertEx.Equal(Hash(images[2]), AssertEx.Sha256(local));
        // Saving only local images should never touch the HTTP client.
        metadata.ScreenshotUrls = [local, local];
        result = await output.SaveAsync(source, metadata, SamplesOnly);
        AssertEx.Equal(1, result.ExtrafanartPaths.Count);
        AssertEx.Equal(3, handler.Calls);
        workspace.AssertNoTemporaryArtifacts();
    }

    private static (SavePlan Plan, MovieMetadata Metadata, Dictionary<string, string> Originals)
        Replacement(TestWorkspace workspace, bool multipart, IReadOnlyList<string> urls)
    {
        var source = workspace.WriteFile(multipart ? "TEST-504-cd1.mp4" : "TEST-504.mp4", [1, 2, 3]);
        var videos = multipart ? new[] { source, workspace.WriteFile("TEST-504-cd2.mp4", [4, 5, 6]) } : [source];
        var oldExtra = workspace.WriteFile("extrafanart/fanart1.jpg", TestImageFactory.CreateJpeg(75, 50));
        var oldExtra2 = workspace.WriteFile("extrafanart/fanart2.jpg", TestImageFactory.CreateJpeg(76, 50));
        var nfo = workspace.WriteFile(multipart ? "movie.nfo" : "TEST-504.nfo", "<movie><title>Original</title></movie>"u8.ToArray());
        var originals = videos.Concat([oldExtra, oldExtra2, nfo]).ToDictionary(p => p, AssertEx.Sha256);
        var metadata = new MovieMetadata { Id = "TEST-504", Title = "Replacement", ScreenshotUrls = urls };
        var plan = FileOrganizationService.BuildPlan(videos, metadata,
            new SaveOptions(true, false, false, true, true, true, ReplaceLocalExtrafanart: true),
            new OrganizationOptions(false, false), new LocalSaveContext(null, null, null)
            { LocalExtrafanartPaths = [oldExtra, oldExtra2], CanReplaceLocalExtrafanart = true });
        return (plan, metadata, originals);
    }

    private static async Task TestReplacementFailure()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var workspace = new TestWorkspace("parallel-replacement-failure");
            var setup = Replacement(workspace, multipart, Enumerable.Range(0, 8).Select(Url).ToArray());
            using var client = new HttpClient(new SampleHandler(Enumerable.Range(0, 8)
                .Select(i => TestImageFactory.CreateJpeg(100 + i, 80)).ToArray(), failures: true));
            using var output = new OutputService(client);
            await AssertEx.ThrowsAsync<InvalidDataException>(() => new FileOrganizationService(output)
                .ExecuteAsync(setup.Plan, setup.Metadata, true), "Partial replacement was accepted.");
            foreach (var pair in setup.Originals) AssertEx.Equal(pair.Value, AssertEx.Sha256(pair.Key));
            workspace.AssertNoTemporaryArtifacts();
        }
    }

    private static async Task TestCancelBody()
    {
        foreach (var multipart in new[] { false, true })
        {
            using var workspace = new TestWorkspace("parallel-body-cancel");
            var setup = Replacement(workspace, multipart, Enumerable.Range(0, 8).Select(Url).ToArray());
            using var handler = new GatedHandler(fatal: false);
            using var client = new HttpClient(handler);
            using var output = new OutputService(client);
            using var cancel = new CancellationTokenSource();
            var task = new FileOrganizationService(output).ExecuteAsync(setup.Plan, setup.Metadata, true, cancel.Token);
            try
            {
                await WaitForThreeAsync(handler);
                cancel.Cancel();
                await AssertEx.ThrowsAsync<OperationCanceledException>(() => task, "Cancellation was swallowed.");
                AssertEx.Equal(0, handler.Pending);
                AssertEx.Equal(3, handler.Calls);
                AssertEx.Equal(3, handler.Canceled);
                foreach (var pair in setup.Originals) AssertEx.Equal(pair.Value, AssertEx.Sha256(pair.Key));
                workspace.AssertNoTemporaryArtifacts();
            }
            finally
            {
                cancel.Cancel();
                try { await task; } catch (OperationCanceledException) { }
            }
        }
    }

    private static async Task TestFatalStopsBatch()
    {
        using var workspace = new TestWorkspace("parallel-fatal-batch");
        var setup = Replacement(workspace, false, Enumerable.Range(0, 8).Select(Url).ToArray());
        using var handler = new GatedHandler(fatal: true);
        using var client = new HttpClient(handler);
        using var output = new OutputService(client);
        var first = new MovieJob(); first.ResetForVideo(setup.Plan.SourceVideoPath, setup.Metadata.Id);
        first.ApplyOnlineSources(setup.Metadata, [setup.Metadata]);
        first.UpdateSaveConfiguration(new(SamplesOnly, new(false, false)));
        var second = new MovieJob(); second.ResetForVideo(workspace.WriteFile("NEXT-505.mp4", [9]), "NEXT-505");
        second.UpdateSaveConfiguration(new(SamplesOnly, new(false, false)));
        var secondPlan = FileOrganizationService.BuildPlan(second.VideoPath!, second.Metadata, SamplesOnly, new(false, false));
        var result = await BatchSaveCoordinator.ExecuteAsync(
            [new(first, setup.Plan, first.ReviewRevision, true), new(second, secondPlan, second.ReviewRevision, true)],
            (item, ct) => new FileOrganizationService(output).ExecuteAsync(item.Plan, item.Job.Metadata, true, ct));
        AssertEx.Equal(1, result.FailedCount);
        AssertEx.Equal(1, result.NotStartedCount);
        AssertEx.True(result.Items[0].Error is InvalidOperationException, "Fatal failure was masked by sibling cancellation.");
        AssertEx.Equal(3, handler.Calls);
        AssertEx.Equal(0, handler.Pending);
        AssertEx.Equal(2, handler.Canceled);
        foreach (var pair in setup.Originals) AssertEx.Equal(pair.Value, AssertEx.Sha256(pair.Key));
        workspace.AssertNoTemporaryArtifacts();
    }

    private static async Task WaitForThreeAsync(GatedHandler handler)
    {
        var watch = Stopwatch.StartNew();
        while (handler.Pending < 3)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Three image bodies did not start.");
            await Task.Delay(5);
        }
    }

    private sealed class SampleHandler(byte[][] images, bool failures = false) : HttpMessageHandler
    {
        private int _calls, _pending, _peak;
        public int Calls => _calls;
        public int Pending => _pending;
        public int Peak => _peak;
        public ConcurrentQueue<int> Completed { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "downloads.invalid") throw new InvalidOperationException("No live requests allowed.");
            var index = int.Parse(Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath));
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _pending);
            int peak;
            do { peak = _peak; } while (active > peak && Interlocked.CompareExchange(ref _peak, active, peak) != peak);
            try
            {
                await Task.Delay(index % 3 == 0 ? 45 : 5, cancellationToken);
                Completed.Enqueue(index);
                if (failures && index is 2 or 5)
                    return new HttpResponseMessage(index == 2 ? HttpStatusCode.NotFound : HttpStatusCode.TooManyRequests);
                var content = new ByteArrayContent(failures && index == 4 ? [0, 1] : images[index == 1 ? 0 : index]);
                if (failures && index == 3) content.Headers.ContentType = new("text/html");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
            finally { Interlocked.Decrement(ref _pending); }
        }
    }

    private sealed class GatedHandler(bool fatal) : HttpMessageHandler
    {
        private readonly bool _fatal = fatal;
        private readonly TaskCompletionSource _threeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pending, _calls, _canceled;
        public int Pending => _pending;
        public int Calls => _calls;
        public int Canceled => _canceled;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "downloads.invalid") throw new InvalidOperationException("No live requests allowed.");
            var number = Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new GatedContent(this, number) });
        }
        private sealed class GatedContent(GatedHandler owner, int number) : HttpContent
        {
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                SerializeToStreamAsync(stream, context, CancellationToken.None);
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref owner._pending) == 3) owner._threeStarted.TrySetResult();
                try
                {
                    await owner._threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                    if (owner._fatal && number == 1) throw new InvalidOperationException("Synthetic fatal body failure.");
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException) { Interlocked.Increment(ref owner._canceled); throw; }
                finally { Interlocked.Decrement(ref owner._pending); }
            }
        }
    }
}
