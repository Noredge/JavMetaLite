using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    // Targeted call-cost attribution, not a sampling profiler or an end-to-end speed claim.
    private static int RunSearchHotspots(string[] args)
    {
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        AppLog.ConfigureDirectory(Path.Combine(Path.GetDirectoryName(output)!, "logs"));
        var rows = new List<object>();
        var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var source = (ComboBox)window.FindName("SourceComboBox");
            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "libredmm");
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            var attach = typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!.CreateDelegate<Action<MovieJob>>(window);
            var issue = typeof(MainWindow).GetMethod("HasQueueIssue", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Func<MovieJob, bool>>();
            var retry = typeof(MainWindow).GetMethod("CanRetrySources", PrivateInstance)!.CreateDelegate<Func<MovieJob, bool>>(window);
            var refresh = typeof(MainWindow).GetMethod("RefreshQueueUiCore", PrivateInstance)!.CreateDelegate<Action<bool>>(window);
            var refreshRetry = typeof(MainWindow).GetMethod("RefreshRetryFailedSourcesUi", PrivateInstance)!.CreateDelegate<Action>(window);
            var jobs = Enumerable.Range(1, 1000).Select(index =>
            {
                var job = new MovieJob();
                job.ResetForVideo($@"C:\Synthetic\PERF-{index:D4}.mp4", $"PERF-{index:D4}");
                attach(job); queue.Add(job); return job;
            }).ToArray();
            foreach (var state in new[] { "searchable", "completed", "mixed-failures" })
            {
                if (state == "completed")
                    foreach (var job in jobs)
                    {
                        var metadata = new MovieMetadata { Id = job.Metadata.Id, Title = "Fixture", SourceName = "libredmm" };
                        job.BeginSearch();
                        job.ApplyOnlineSources(metadata, [metadata], [new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, metadata, null, 2)]);
                    }
                if (state == "mixed-failures")
                    for (var index = 0; index < jobs.Length; index += 4)
                    {
                        var error = new HttpRequestException("Synthetic provider failure");
                        jobs[index].MarkSearchFailed([new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, null, error, 0)], error);
                    }
                refresh(false);
                window.UpdateLayout();
                foreach (var operation in new[] { "issue-check", "batch-search-eligibility", "retry-eligibility", "retry-ui", "queue-ui" })
                {
                    var result = 0;
                    void Run()
                    {
                        if (operation == "queue-ui") refresh(false);
                        else if (operation == "retry-ui") refreshRetry();
                        else foreach (var job in jobs)
                            if (operation == "issue-check" ? issue(job) : operation == "retry-eligibility" ? retry(job) : job.CanBatchSearch)
                                result++;
                    }
                    Run(); // Warm the exact call path before measurement.
                    for (var iteration = 1; iteration <= 3; iteration++)
                    {
                        result = 0;
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        var started = Stopwatch.GetTimestamp();
                        for (var repeat = 0; repeat < 100; repeat++) Run();
                        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                        var row = new { State = state, Operation = operation, Count = jobs.Length, Repeats = 100, Iteration = iteration,
                            ElapsedMs = elapsed, AllocatedBytes = allocated, Result = result };
                        rows.Add(row);
                        Console.WriteLine(JsonSerializer.Serialize(row));
                    }
                }
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Version = MainWindow.ApplicationVersion, Runtime = Environment.Version.ToString(), TimestampUtc = DateTime.UtcNow,
                Notes = "100 direct delegate calls per row, 1000 synthetic jobs, 3 repetitions. UI-thread allocations only; no dispatcher pumping inside each measured interval, no actual HTTP/files. Call costs are not additive or representative call counts for an entire search. Use the separate end-to-end fake-HTTP benchmark for overall impact.",
                Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); }
        return 0;
    }
}
