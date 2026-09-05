using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal static class SavePerformance
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "artifacts/save-performance");
        var count = int.Parse(args.ElementAtOrDefault(1) ?? "12");
        var rounds = int.Parse(args.ElementAtOrDefault(2) ?? "3");
        var delay = int.Parse(args.ElementAtOrDefault(3) ?? "60");
        if (count is < 1 or > 100 || rounds is < 1 or > 5 || delay is < 0 or > 500)
            throw new ArgumentException("Expected count 1..100, rounds 1..5, delay 0..500 ms.");
        // A fresh generated root per execution; never overwrite a previous run or user files.
        if (Directory.Exists(root)) throw new IOException($"Benchmark output already exists: {root}");
        Directory.CreateDirectory(root);
        var images = Enumerable.Range(0, 13).Select(i => TestImageFactory.CreateJpeg(1500 + i * 10, 1000)).ToArray();
        var rows = new List<object>();
        var manifest = new SortedDictionary<string, string>();
        for (var round = 0; round <= rounds; round++)
        {
            var roundRoot = Path.Combine(root, $"round-{round}");
            Directory.CreateDirectory(roundRoot);
            AppLog.ConfigureDirectory(Path.Combine(roundRoot, "logs"));
            using var handler = new MeasuredImageHandler(images, delay);
            using var client = new HttpClient(handler);
            using var output = new OutputService(client);
            var organizer = new FileOrganizationService(output);
            var items = new List<PreparedMovieSave>();
            var videoBytes = new byte[] { 0x4A, 0x41, 0x56, 0x4D, 0x45, 0x54, 0x41 };
            for (var i = 0; i < (round == 0 ? 1 : count); i++)
            {
                var id = $"BENCH-{i + 1:000}";
                var path = Path.Combine(roundRoot, id, id + ".mp4");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, videoBytes);
                var job = new MovieJob(); job.ResetForVideo(path, id);
                var metadata = new MovieMetadata { Id = id, Title = "Synthetic save timing",
                    SourceName = "libredmm", CoverUrl = "https://save.invalid/0.jpg",
                    ScreenshotUrls = Enumerable.Range(1, 12).Select(n => $"https://save.invalid/{n}.jpg").ToArray() };
                job.ApplyOnlineSources(metadata, [metadata]);
                var saveOptions = new SaveOptions(true, true, true, true, true, false);
                var organization = new OrganizationOptions(false, false);
                job.UpdateSaveConfiguration(new(saveOptions, organization));
                var plan = FileOrganizationService.BuildPlan(path, job.Metadata, saveOptions, organization);
                items.Add(new(job, plan, job.ReviewRevision, false));
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime;
            long peakWorkingSet = process.WorkingSet64;
            long peakManaged = GC.GetTotalMemory(false);
            using var monitorStop = new CancellationTokenSource();
            var monitor = Task.Run(async () =>
            {
                while (!monitorStop.IsCancellationRequested)
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                    try { await Task.Delay(10, monitorStop.Token); }
                    catch (OperationCanceledException) { break; }
                }
            });
            var watch = Stopwatch.StartNew();
            var activeMovies = 0;
            var peakMovies = 0;
            BatchSaveResult result;
            try
            {
                result = await BatchSaveCoordinator.ExecuteAsync(items, async (item, ct) =>
                {
                    peakMovies = Math.Max(peakMovies, ++activeMovies);
                    try { return await organizer.ExecuteAsync(item.Plan, item.Job.Metadata, false, ct); }
                    finally { activeMovies--; }
                });
            }
            finally { watch.Stop(); monitorStop.Cancel(); await monitor; }
            var cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
            var allocationBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            AssertEx.Equal(items.Count, result.CompletedCount,
                string.Join("; ", result.Items.Where(i => i.Error is not null).Select(i => i.Error!.ToString())));
            AssertEx.Equal(1, peakMovies);
            AssertEx.Equal(items.Count * 13, handler.Calls);
            AssertEx.Equal(0, handler.Pending);
            foreach (var item in result.Items)
            {
                AssertEx.Equal(Convert.ToHexString(SHA256.HashData(videoBytes)), AssertEx.Sha256(item.Item.Job.VideoPath!));
                var saved = item.SaveResult!.Outputs;
                AssertEx.Equal(12, saved.ExtrafanartPaths.Count);
                AssertEx.Equal(Convert.ToHexString(SHA256.HashData(images[0])), AssertEx.Sha256(saved.FanartPath!));
                for (var n = 0; n < 12; n++)
                    AssertEx.Equal(Convert.ToHexString(SHA256.HashData(images[n + 1])), AssertEx.Sha256(saved.ExtrafanartPaths[n]));
            }
            foreach (var file in Directory.EnumerateFiles(roundRoot, "*", SearchOption.AllDirectories)
                         .Where(p => !p.Contains(Path.DirectorySeparatorChar + "logs" + Path.DirectorySeparatorChar)))
                manifest[Path.GetRelativePath(root, file).Replace('\\', '/')] = AssertEx.Sha256(file);
            AssertEx.True(!Directory.EnumerateFileSystemEntries(roundRoot, "*.tmp", SearchOption.AllDirectories).Any(), "Benchmark left transaction artifacts.");
            var stages = new Dictionary<string, double>();
            foreach (var line in File.ReadLines(AppLog.CurrentLogPath).Where(l => l.Contains("保存耗时 ")))
            {
                var area = Regex.Match(line, @"area=(\S+)").Groups[1].Value;
                foreach (Match match in Regex.Matches(line, @"(\w+Ms)=([0-9.]+)"))
                {
                    var key = area + "." + match.Groups[1].Value;
                    stages[key] = stages.GetValueOrDefault(key) + double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }
            rows.Add(new { round, warmup = round == 0, movies = items.Count, elapsedMs = watch.Elapsed.TotalMilliseconds,
                cpuMs, allocationBytes, peakWorkingSet, peakManaged, imageRequests = handler.Calls,
                peakImageRequests = handler.Peak, peakMovies, pendingAfter = handler.Pending, stages });
            Console.WriteLine($"SAVE PERF round={round} movies={items.Count} elapsedMs={watch.ElapsedMilliseconds} peakRequests={handler.Peak} pending={handler.Pending}");
        }
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new
        {
            runtime = Environment.Version.ToString(), count, rounds, delayMs = delay,
            imageWidths = "1500..1620", imageHeight = 1000, format = "JPEG",
            note = "Offline fake HTTP with deterministic latency; round 0 warmup excluded; memory sampled every 10 ms, not a hard upper bound.", rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private sealed class MeasuredImageHandler(byte[][] images, int delay) : HttpMessageHandler
    {
        private int _calls, _pending, _peak;
        public int Calls => _calls;
        public int Pending => _pending;
        public int Peak => _peak;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "save.invalid") throw new InvalidOperationException("No live network allowed.");
            var index = int.Parse(Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath));
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _pending);
            int peak;
            do { peak = _peak; } while (active > peak && Interlocked.CompareExchange(ref _peak, active, peak) != peak);
            try
            {
                // Latency differs by image so concurrent responses do not complete in input order.
                if (delay > 0) await Task.Delay(delay + index % 3 * delay / 3, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(images[index]) };
            }
            finally { Interlocked.Decrement(ref _pending); }
        }
    }
}
