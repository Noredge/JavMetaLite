using System.Collections;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestBatchSourceRoutingUi()
    {
        // Exercise the actual queue button with fake HTTP transports, not just the coordinator.
        var window = new MainWindow();
        using var libreHandler = new RoutingHandler();
        using var r18Handler = new RoutingHandler();
        using var javHandler = new RoutingHandler();
        using var libreHttp = new HttpClient(libreHandler);
        using var r18Http = new HttpClient(r18Handler);
        using var javHttp = new HttpClient(javHandler);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void ReplaceClient(string name, IDisposable replacement)
        {
            var field = typeof(MainWindow).GetField(name, flags)!;
            ((IDisposable)field.GetValue(window)!).Dispose();
            field.SetValue(window, replacement);
        }
        ReplaceClient("_libreDmmClient", new LibreDmmClient(libreHttp));
        ReplaceClient("_r18DevClient", new R18DevClient(r18Http));
        ReplaceClient("_javLibraryClient", new JavLibraryClient(javHttp));
        window.Show();
        var languageBox = (ComboBox)window.FindName("LanguageComboBox");
        var originalLanguage = languageBox.SelectedItem;
        try
        {
            var source = (ComboBox)window.FindName("SourceComboBox");
            typeof(MainWindow).GetMethod("ApplyPreferences", flags)!.Invoke(window, [AppPreferences.CreateSafeDefaults()]);
            if ((source.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "libredmm")
                throw new InvalidOperationException("Fresh preferences do not select LibreDMM.");
            var search = (Button)window.FindName("SearchQueueButton");
            var retry = (Button)window.FindName("RetryFailedSourcesButton");
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", flags)!.GetValue(window)!;
            var attach = typeof(MainWindow).GetMethod("AttachMovieJob", flags)!;
            var busy = typeof(MainWindow).GetField("_busy", flags)!;
            var index = 1;
            void Select(string mode) => source.SelectedItem = source.Items.OfType<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == mode);
            MovieJob[] Add(int count)
            {
                var added = Enumerable.Range(0, count).Select(_ =>
                {
                    var job = new MovieJob();
                    job.ResetForVideo($@"C:\Synthetic\IPX-{index:D3}.mp4", $"IPX-{index++:D3}");
                    attach.Invoke(window, [job]);
                    queue.Add(job);
                    return job;
                }).ToArray();
                typeof(MainWindow).GetMethod("RefreshQueueUi", flags)!.Invoke(window, []);
                return added;
            }
            void Search(string mode, MovieJob[] jobs, string? changedModeDuringRun = null)
            {
                Select(mode);
                if (!search.IsEnabled) throw new InvalidOperationException("Eligible source-scoped queue search is disabled.");
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (changedModeDuringRun is not null) Select(changedModeDuringRun);
                WaitForCondition(() => !(bool)busy.GetValue(window)! && jobs.All(job => job.SearchState == MovieSearchState.NeedsReview),
                    "Source-scoped queue search did not finish.");
            }

            if (source.Items.OfType<ComboBoxItem>().Any(item => item.Tag?.ToString() is "javlibrary" or "auto") || source.Items.Count != 4)
            {
                throw new InvalidOperationException("Retired automatic sources are still exposed.");
            }
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var many = Add(71);
            Search("libredmm", many, changedModeDuringRun: "r18dev");
            if (libreHandler.Calls != 71 || r18Handler.Calls != 0 || javHandler.Calls != 0 ||
                many.Any(job => job.HasPartialSourceFailure || job.LastSearchAttempts.Count != 1 || job.LastSearchAttempts[0].SourceName != "libredmm"))
            {
                throw new InvalidOperationException("71-movie LibreDMM UI search contacted an unselected source or reported partial success.");
            }
            var r18Jobs = Add(2);
            Search("r18dev", r18Jobs);
            if (libreHandler.Calls != 71 || r18Handler.Calls != 2) throw new InvalidOperationException("R18-only queue searched LibreDMM.");
            var defaultCustomJobs = Add(2);
            Search("custom", defaultCustomJobs);
            if (libreHandler.Calls != 73 || r18Handler.Calls != 4) throw new InvalidOperationException("Custom queue did not request both sources.");
            typeof(MainWindow).GetField("_customSourceProfile", flags)!.SetValue(window,
                new MetadataSourcePreferenceProfile { TitleSource = "r18dev" });
            var customJobs = Add(2);
            Search("custom", customJobs);
            if (libreHandler.Calls != 75 || r18Handler.Calls != 6 || customJobs.Any(job => job.Metadata.Title != "R18 fixture"))
            {
                throw new InvalidOperationException("Custom queue routing or preferred field selection failed.");
            }

            var manualJob = Add(1)[0];
            Select("manual");
            if (search.IsEnabled) throw new InvalidOperationException("Manual mode enables automatic queue search.");
            search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Guard even programmatic invocation.
            if (manualJob.SearchState != MovieSearchState.Searchable || libreHandler.Calls != 75 || r18Handler.Calls != 6)
                throw new InvalidOperationException("Manual mode changed a queue item or made network requests.");

            // Previous R18 failures cannot be silently retried after selecting LibreDMM.
            var failed = new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null,
                new MetadataSourceRateLimitException(DateTimeOffset.UtcNow.AddMinutes(1)), 0);
            var prior = many[0].LastSearchAttempts[0];
            many[0].ApplyOnlineSources(prior.Metadata!, [prior.Metadata!], [prior, failed]);
            Select("libredmm");
            if (retry.Visibility != Visibility.Collapsed) throw new InvalidOperationException("DMM-only mode offers automatic R18 retry.");
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (r18Handler.Calls != 6) throw new InvalidOperationException("DMM-only retry contacted R18.");
            Select("r18dev");
            if (retry.Visibility != Visibility.Visible) throw new InvalidOperationException("R18 retry is unavailable in R18 mode.");
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForCondition(() => !(bool)busy.GetValue(window)! && !many[0].HasFailedSources, "R18 failed-source UI retry did not recover.");
            if (r18Handler.Calls != 7 || libreHandler.Calls != 75) throw new InvalidOperationException("Retry repeated an already successful source.");

            var active = (MovieJob)typeof(MainWindow).GetField("_activeJob", flags)!.GetValue(window)!;
            active.Metadata.Id = "IPX-999";
            Select("manual");
            var browser = (Button)window.FindName("BrowserImportButton");
            browser.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (browser.ContextMenu?.Items.OfType<MenuItem>().Select(item => item.Tag).OfType<BrowserImportTarget>()
                .Any(target => target.SourceName == "javlibrary") != true)
                throw new InvalidOperationException("Manual current-movie JAVLibrary lookup disappeared.");
            browser.ContextMenu.IsOpen = false;
            active.ApplyManualWebSource(new MovieMetadata { Id = active.Metadata.Id, Rating = "8.5", SourceName = "javlibrary", SourceDisplayName = "JAVLibrary" });
            if (active.Metadata.Rating != "8.5" || queue.Cast<MovieJob>().Where(job => !ReferenceEquals(job, active)).Any(job => job.Metadata.Rating == "8.5"))
                throw new InvalidOperationException("Manual JAVLibrary import affected more than the current movie.");
            if (javHandler.Calls != 0) throw new InvalidOperationException("Automatic JAVLibrary HTTP request was issued.");
            Select("libredmm");
            ((Button)window.FindName("SearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForCondition(() => !(bool)busy.GetValue(window)! && active.SearchState == MovieSearchState.NeedsReview,
                "Current-movie LibreDMM search did not complete.");
            if (libreHandler.Calls != 76 || r18Handler.Calls != 7 || active.LastSearchAttempts.Count != 1)
                throw new InvalidOperationException("Single-movie and batch source routing disagree.");
            Select("manual");
            ((Button)window.FindName("SearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (libreHandler.Calls != 76 || r18Handler.Calls != 7 || javHandler.Calls != 0)
                throw new InvalidOperationException("Manual single-movie action made automatic requests.");
            typeof(MainWindow).GetMethod("ApplyPreferences", flags)!.Invoke(window,
                [new AppPreferences { RememberSavePreferences = true, SearchSourceMode = "javlibrary" }]);
            if ((source.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "manual" || search.IsEnabled)
                throw new InvalidOperationException("Legacy JAVLibrary preference did not migrate to safe manual mode.");
            typeof(MainWindow).GetMethod("ApplyPreferences", flags)!.Invoke(window,
                [new AppPreferences { RememberSavePreferences = true, SearchSourceMode = "auto" }]);
            if ((source.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "libredmm")
                throw new InvalidOperationException("Legacy Auto preference did not migrate to LibreDMM.");

            var hint = (TextBlock)window.FindName("BatchSourceHint");
            var toolbar = (Grid)window.FindName("SearchToolbarLayout");
            var selector = (Grid)window.FindName("SourceSelectorHost");
            var language = (ComboBox)window.FindName("LanguageComboBox");
            window.Width = 930;
            window.Height = 650;
            foreach (var code in UiLanguageCodes.Supported)
            {
                language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == code);
                foreach (var mode in MetadataSearchSourceModes.Supported)
                {
                    Select(mode);
                    window.UpdateLayout();
                    if (mode == "manual")
                    {
                        if (hint.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Manual mode shows an automatic batch warning.");
                        continue;
                    }
                    if (hint.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(hint.Text) ||
                        !hint.Text.Contains("LibreDMM") || !hint.Text.Contains("R18") ||
                        hint.TranslatePoint(new Point(0, 0), toolbar).Y < selector.TranslatePoint(new Point(0, selector.ActualHeight), toolbar).Y ||
                        hint.TranslatePoint(new Point(hint.ActualWidth, hint.ActualHeight), toolbar).X > toolbar.ActualWidth + 1 ||
                        hint.TranslatePoint(new Point(hint.ActualWidth, hint.ActualHeight), toolbar).Y > toolbar.ActualHeight + 1)
                        throw new InvalidOperationException($"Batch hint is missing, overlapping or clipped: {code}/{mode}.");
                    var expectedBrush = window.FindResource(mode == "libredmm" ? "MutedBrush" : "BatchSourceWarningBrush");
                    if (!ReferenceEquals(hint.Foreground, expectedBrush)) throw new InvalidOperationException("Batch hint warning emphasis is incorrect.");
                }
            }
            Select("r18dev");
            ((RadioButton)window.FindName("SingleModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            if (hint.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Single-movie mode displays batch-only warning.");
            if (libreHandler.Calls != 76 || r18Handler.Calls != 7 || javHandler.Calls != 0)
                throw new InvalidOperationException("Source/language changes triggered unintended searches.");
            Console.WriteLine("UI PASS queueSourceRouting=True libre71R18Zero=True sourceSnapshot=True r18Only=True customBoth=True singleSourceParity=True manualNoRequests=True retryScope=True javLibraryManualOnly=True legacySourceMigration=True dmmDefault=True autoRetired=True batchHintsFourLanguages=True compactHintLayout=True");
        }
        finally
        {
            languageBox.SelectedItem = originalLanguage;
            window.Close();
        }
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (request.RequestUri!.Host.Contains("javlibrary", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JAVLibrary is manual-only.");
            await Task.Delay(2, cancellationToken);
            var body = request.RequestUri.Host.Contains("libredmm", StringComparison.OrdinalIgnoreCase)
                ? "{\"title\":\"Libre fixture\"}" : "{\"title_en\":\"R18 fixture\"}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
