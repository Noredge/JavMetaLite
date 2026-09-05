using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestSearchPredicateParity()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var source = (ComboBox)window.FindName("SourceComboBox");
            var issue = typeof(MainWindow).GetMethod("HasQueueIssue", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Func<MovieJob, bool>>();
            var retry = typeof(MainWindow).GetMethod("CanRetrySources", PrivateInstance)!
                .CreateDelegate<Func<MovieJob, bool>>(window);
            var error = new InvalidOperationException("Synthetic failure");
            var inputs = new string?[] { null, "", " ", "\t\r\n", "\u3000\u00a0", "  ipx081  ",
                "FC2 PPV 1234567", "123456_123", "ABF－193", "title [IPX-081] HD", "free form", ".", "???", "作品", " / " };
            foreach (var input in inputs)
            {
                using var job = new MovieJob();
                job.ResetForVideo(@"C:\Synthetic\IPX-081.mp4", "IPX-081");
                job.Metadata.Id = input!;
                Check(job);
                job.IsSelectedForBatch = false;
                Check(job);
                job.IsSelectedForBatch = true;
                if (string.IsNullOrWhiteSpace(MovieIdParser.Normalize(job.Metadata.Id))) continue;

                job.BeginSearch();
                Check(job);
                job.MarkSearchCanceled();
                Check(job);
                foreach (var failedSource in new[] { "libredmm", "r18dev", "R18DEV", "javlibrary", "unrecognized" })
                {
                    job.MarkSearchFailed([new MetadataSourceSearchAttempt(failedSource, failedSource, TimeSpan.Zero, null, error, 0)], error);
                    Check(job);
                }
                var result = new MovieMetadata { Id = job.Metadata.Id, Title = "Fixture", SourceName = "libredmm" };
                var success = new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, result, null, 2);
                var failed = new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null, error, 0);
                job.ApplyOnlineSources(result, [result], [success, failed]);
                Check(job);
                job.MarkSaveFailed(error, conflict: false);
                Check(job);
                job.MarkSaveFailed(error, conflict: true);
                Check(job);
                job.MarkSaveCompleted();
                Check(job);
                // ID changes while a search is active preserve attempts: eligibility must
                // still react immediately to an empty ID, without a stale validity cache.
                job.BeginSearch(preserveAttempts: true);
                job.Metadata.Id = "\u3000";
                Check(job);
                job.Metadata.Id = "ABF-193";
                Check(job);
                job.MarkSearchCanceled();
                job.Metadata.Id = "PERF-0099"; // Normal ID edit clears old source attempts.
                Check(job);
            }

            // Guard the measured hot-path improvement without fragile wall-clock limits.
            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "libredmm");
            using var eligible = new MovieJob();
            eligible.ResetForVideo(@"C:\Synthetic\PERF-0001.mp4", "PERF-0001");
            var count = 0;
            void Query()
            {
                if (issue(eligible)) count++;
                if (eligible.CanBatchSearch) count++;
                if (retry(eligible)) count++;
            }
            for (var warmup = 0; warmup < 100; warmup++) Query();
            count = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < 10000; iteration++) Query();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (count != 10000 || allocated > 16384)
                throw new InvalidOperationException($"Queue eligibility regained per-query parsing/allocation: results={count}, bytes={allocated}.");
            Console.WriteLine("UI PASS searchPredicateOldBehaviorParity=True unicodeBlankIds=True customIdFallbackPreserved=True searchCancelRetryStates=True sourceScopedRetryParity=True lowAllocationQueueChecks=True");

            void Check(MovieJob job)
            {
                // Preserve the preview.48 expressions as an independent behavior oracle.
                var idMissing = string.IsNullOrWhiteSpace(MovieIdParser.Normalize(job.Metadata.Id));
                var expectedSearch = job.IsSelectedForBatch && !idMissing && job.SearchState is
                    (MovieSearchState.Searchable or MovieSearchState.SearchFailed or MovieSearchState.SearchCanceled);
                var expectedIssue = job.LocalNfoSaveBlocked || job.HasFailedSources || idMissing ||
                    job.SearchState == MovieSearchState.SearchFailed || job.SaveState is (MovieSaveState.SaveFailed or MovieSaveState.Conflict);
                if (job.CanBatchSearch != expectedSearch || issue(job) != expectedIssue)
                    throw new InvalidOperationException($"Queue predicate behavior changed for {inputDescription(job)}.");
                foreach (var mode in new[] { "libredmm", "r18dev", "custom", "manual" })
                {
                    source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == mode);
                    string[] allowed = mode switch { "libredmm" => ["libredmm"], "r18dev" => ["r18dev"], "custom" => ["libredmm", "r18dev"], _ => [] };
                    var expectedRetry = !idMissing && job.LastSearchAttempts.Any(attempt =>
                        !attempt.Success && allowed.Contains(attempt.SourceName, StringComparer.OrdinalIgnoreCase));
                    if (retry(job) != expectedRetry)
                        throw new InvalidOperationException($"Retry scope changed for {mode}/{inputDescription(job)}.");
                }
            }
            static string inputDescription(MovieJob job) => $"'{job.Metadata.Id}'/{job.SearchState}/{job.SaveState}";
        }
        finally { window.Close(); }
    }
}
