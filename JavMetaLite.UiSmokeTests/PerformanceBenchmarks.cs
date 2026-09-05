using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    // Deliberately separate from the correctness gate: elapsed-time assertions are machine dependent.
    // Reuses the real WPF event/loader paths, with synthetic files and no live network requests.
    private static int RunPerformance(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("--performance output.json [100,500,1000] [repetitions]");
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var sizes = (args.ElementAtOrDefault(2) ?? "100,500,1000").Split(',').Select(int.Parse).ToArray();
        var repetitions = int.Parse(args.ElementAtOrDefault(3) ?? "3");
        var searchOnly = args.ElementAtOrDefault(4) == "search";
        if (sizes.Any(n => n < 1 || n > 2000) || repetitions is < 1 or > 10)
            throw new ArgumentException("Sizes must be 1..2000; repetitions 1..10.");
        var root = Path.Combine(Path.GetDirectoryName(output)!, "synthetic-fixtures");
        AppLog.ConfigureDirectory(Path.Combine(root, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var rows = new List<PerformanceRow>();
        var task = Execute();
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
        return 0;

        async Task Execute()
        {
            foreach (var count in sizes)
            {
                foreach (var artwork in searchOnly ? new[] { false } : new[] { false, true })
                {
                    // Fixture creation is intentionally outside measured intervals. Files stay for reuse.
                    var paths = searchOnly ? [] : await Task.Run(() => CreatePerformanceFixtures(root, count, artwork));
                    for (var iteration = 1; iteration <= repetitions; iteration++)
                    {
                        var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
                        window.Show();
                        window.UpdateLayout();
                        var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
                        var view = (ICollectionView)typeof(MainWindow).GetField("_movieQueueView", PrivateInstance)!.GetValue(window)!;
                        var resets = 0;
                        view.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
                        try
                        {
                            if (searchOnly)
                            {
                                ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                var attach = typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!;
                                for (var n = 1; n <= count; n++)
                                {
                                    var job = new MovieJob(); job.ResetForVideo($@"C:\Synthetic\PERF-{n:D4}.mp4", $"PERF-{n:D4}");
                                    attach.Invoke(window, [job]); queue.Add(job);
                                }
                                using var libreHandler = new RoutingHandler(); using var r18Handler = new RoutingHandler();
                                using var libreHttp = new HttpClient(libreHandler); using var r18Http = new HttpClient(r18Handler);
                                void Replace(string name, IDisposable client)
                                {
                                    var field = typeof(MainWindow).GetField(name, PrivateInstance)!;
                                    ((IDisposable)field.GetValue(window)!).Dispose(); field.SetValue(window, client);
                                }
                                Replace("_libreDmmClient", new LibreDmmClient(libreHttp));
                                Replace("_r18DevClient", new R18DevClient(r18Http));
                                var source = (ComboBox)window.FindName("SourceComboBox");
                                source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(i => i.Tag?.ToString() == "libredmm");
                                await InvokeAsync("RefreshQueueUi");
                                await Measure("search-dmm-fake2ms", async () =>
                                {
                                    ((Button)window.FindName("SearchQueueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    var timeout = Stopwatch.StartNew();
                                    while ((bool)typeof(MainWindow).GetField("_busy", PrivateInstance)!.GetValue(window)!)
                                    {
                                        if (timeout.Elapsed.TotalMinutes > 3) throw new TimeoutException("Synthetic search timeout.");
                                        await Task.Delay(5);
                                    }
                                });
                                if (libreHandler.Calls != count || r18Handler.Calls != 0 || queue.Cast<MovieJob>().Any(j => j.HasFailedSources))
                                    throw new InvalidOperationException("Synthetic search made incorrect requests.");
                                Console.WriteLine($"REQUESTS DMM={libreHandler.Calls} R18={r18Handler.Calls}");
                                continue;
                            }
                            await Measure("import", () => InvokeAsync("AddMovieFilesAsync", (object)paths));
                            if (queue.Count != count) throw new InvalidOperationException("Fixture grouping/import lost movies.");
                            await Measure("deselect", () => InvokeAsync("SetBatchSelection", false));
                            await Measure("select", () => InvokeAsync("SetBatchSelection", true));
                            await Measure("duplicate-import", () => InvokeAsync("AddMovieFilesAsync", (object)paths));
                            if (queue.Count != count) throw new InvalidOperationException("Duplicate import changed movie count.");
                            await Measure("filter", async () =>
                            {
                                var filter = (ComboBox)window.FindName("QueueFilterComboBox");
                                foreach (var index in new[] { 1, 0 }) { filter.SelectedIndex = index; await Dispatcher.Yield(DispatcherPriority.Background); }
                            });
                            await Measure("browse-48", async () =>
                            {
                                foreach (var job in queue.Cast<MovieJob>().Take(48))
                                    await InvokeAsync("ActivateMovieJobAsync", job, true);
                            });
                            await Measure("language", () =>
                            {
                                var box = (ComboBox)window.FindName("LanguageComboBox");
                                foreach (var item in box.Items.OfType<ComboBoxItem>().Where(i => i.Tag?.ToString() is "ja" or "en"))
                                    box.SelectedItem = item;
                                return Task.CompletedTask;
                            });
                        }
                        finally { window.Close(); }

                        async Task InvokeAsync(string method, params object[] parameters)
                        {
                            var result = typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(window, parameters);
                            if (result is Task pending) await pending;
                        }
                        async Task Measure(string operation, Func<Task> action)
                        {
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            var resetStart = resets;
                            var allocated = GC.GetTotalAllocatedBytes(precise: true);
                            var clock = Stopwatch.StartNew();
                            var previous = 0d;
                            var longestGap = 0d;
                            var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
                            timer.Tick += (_, _) => { var now = clock.Elapsed.TotalMilliseconds; longestGap = Math.Max(longestGap, now - previous); previous = now; };
                            timer.Start();
                            try
                            {
                                await action();
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            }
                            finally { timer.Stop(); }
                            var elapsed = clock.Elapsed.TotalMilliseconds;
                            longestGap = Math.Max(longestGap, elapsed - previous);
                            // Forced GC occurs AFTER timing and allocations, making retained memory comparable.
                            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
                            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                            var row = new PerformanceRow(count, artwork ? "nfo-artwork" : "bare", iteration,
                                operation, Math.Round(elapsed, 2), Math.Round(longestGap, 2), resets - resetStart,
                                allocatedBytes, GC.GetTotalMemory(false), Process.GetCurrentProcess().WorkingSet64);
                            rows.Add(row);
                            Console.WriteLine(JsonSerializer.Serialize(row));
                            File.WriteAllText(output, JsonSerializer.Serialize(new
                            {
                                Version = MainWindow.ApplicationVersion, Runtime = Environment.Version.ToString(),
                                CpuCount = Environment.ProcessorCount, TimestampUtc = DateTime.UtcNow,
                                Notes = "Synthetic JPEGs: cover 1600x1000, poster 800x1000, four samples 640x400. No OS cache flushing. Input-priority heartbeat gap includes its 10ms interval. Browse retains up to 48 visited movies; GC is outside timed intervals. No network.",
                                Rows = rows
                            }, new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                }
            }
        }
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed record PerformanceRow(int Count, string Scenario, int Iteration, string Operation,
        double ElapsedMs, double MaxUiGapMs, int ViewResets, long AllocatedBytes, long RetainedManagedBytes, long WorkingSetBytes);

    private static string[] CreatePerformanceFixtures(string root, int count, bool artwork)
    {
        byte[] Jpeg(int width, int height)
        {
            var pixels = new byte[width * height * 3];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);
            var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        var cover = artwork ? Jpeg(1600, 1000) : [];
        var poster = artwork ? Jpeg(800, 1000) : [];
        var sample = artwork ? Jpeg(640, 400) : [];
        var paths = new List<string>();
        for (var index = 1; index <= count; index++)
        {
            var id = $"PERF-{index:D4}";
            var directory = Path.Combine(root, artwork ? "artwork" : "bare", id);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, id + ".mp4");
            if (!File.Exists(path)) File.WriteAllBytes(path, []);
            paths.Add(path);
            if (!artwork) continue;
            if (!File.Exists(Path.Combine(directory, id + ".nfo")))
                File.WriteAllText(Path.Combine(directory, id + ".nfo"), $"<movie><id>{id}</id><title>Synthetic {id}</title><originaltitle>Fixture</originaltitle></movie>");
            foreach (var (suffix, bytes) in new[] { ("-poster.jpg", poster), ("-fanart.jpg", cover) })
                if (!File.Exists(Path.Combine(directory, id + suffix))) File.WriteAllBytes(Path.Combine(directory, id + suffix), bytes);
            var samples = Path.Combine(directory, "extrafanart");
            Directory.CreateDirectory(samples);
            for (var n = 1; n <= 4; n++)
                if (!File.Exists(Path.Combine(samples, $"fanart{n}.jpg"))) File.WriteAllBytes(Path.Combine(samples, $"fanart{n}.jpg"), sample);
        }
        return paths.ToArray();
    }
}
