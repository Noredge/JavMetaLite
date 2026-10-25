using System.Diagnostics;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using JavMetaLite.RegressionTests;
using SkiaSharp;

internal static partial class Program
{
    private static Task DownloadBodyCancellation() => GatedDownloadRecovery(fatal: false);
    private static Task FatalDownloadCancellation() => GatedDownloadRecovery(fatal: true);

    private static async Task GatedDownloadRecovery(bool fatal)
    {
        foreach (var multipart in new[] { false, true })
        {
            using var temp = new Temp();
            var first = temp.Write(multipart ? "DEMO-908-cd1.mp4" : "DEMO-908.mp4", "SYNTHETIC FIRST VIDEO");
            var videos = multipart ? new[] { first, temp.Write("DEMO-908-cd2.mp4", "SYNTHETIC SECOND VIDEO") } : [first];
            var nfo = temp.Write(multipart ? "movie.nfo" : "DEMO-908.nfo", "<movie><title>Original</title><custom>KEEP</custom></movie>");
            var artwork = WriteImage(temp, "extrafanart/fanart1.jpg", SKColors.Coral);
            var originals = videos.Concat([nfo, artwork]).ToDictionary(path => path, File.ReadAllBytes);
            var metadata = new MovieMetadata { Id = "DEMO-908", Title = "Must not commit",
                ScreenshotUrls = Enumerable.Range(0, 8).Select(i => $"https://downloads.invalid/{i}.jpg").ToArray() };
            var plan = FileOrganizationService.BuildPlan(videos, metadata,
                new SaveOptions(true, false, false, true, true, false, ReplaceLocalExtrafanart: true), InPlace,
                new LocalSaveContext(null, null, null) { LocalExtrafanartPaths = [artwork], CanReplaceLocalExtrafanart = true });
            using var handler = new GatedDownloadHandler(fatal);
            using var client = new HttpClient(handler);
            using var output = new OutputService(Images, client);
            using var cancellation = new CancellationTokenSource();
            var operation = new FileOrganizationService(output).ExecuteAsync(plan, metadata, true, cancellation.Token);
            try
            {
                if (fatal)
                    await Throws<InvalidOperationException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
                else
                {
                    var timer = Stopwatch.StartNew();
                    while (handler.Pending < 3 && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(5);
                    Equal(3, handler.Pending);
                    cancellation.Cancel();
                    await Throws<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
                }
                Check(handler.SawCancelableBodyToken, "Response body did not receive a cancellation token");
                Equal(3, handler.Calls);
                Equal(0, handler.Pending);
                Equal(fatal ? 2 : 3, handler.Canceled);
                foreach (var (path, bytes) in originals)
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Canceled/fatal download changed an original file");
                Check(!Directory.EnumerateDirectories(temp.Path, ".JavMetaLite*", SearchOption.AllDirectories).Any(), "Download failure left staging");
            }
            finally
            {
                cancellation.Cancel();
                handler.Dispose(); // Release a broken fixture too, without leaving a hanging test task.
                try { await operation.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            }
        }
    }
}
