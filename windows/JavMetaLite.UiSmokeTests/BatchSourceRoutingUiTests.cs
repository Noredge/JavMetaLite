using System.Collections;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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
        var window = new MainWindow(TestPreferences());
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
            SetQueueOpen(window, true);

            var many = Add(71);
            Search("libredmm", many, changedModeDuringRun: "r18dev");
            if (libreHandler.Calls != 71 || r18Handler.Calls != 0 || javHandler.Calls != 0 ||
                many.Any(job => job.HasPartialSourceFailure || job.LastSearchAttempts.Count != 1 || job.LastSearchAttempts[0].SourceName != "libredmm"))
            {
                throw new InvalidOperationException("71-movie LibreDMM UI search contacted an unselected source or reported partial success.");
            }
            var checkedMovies = many[..2];
            foreach (var job in many.Skip(2)) job.IsSelectedForBatch = false;
            WaitForCondition(() => search.IsEnabled &&
                Equals(search.Content, LocalizationService.Get("Desk.SearchCheckedCount", 2)),
                "Completed checked movies disappeared from the batch count.");
            Search("r18dev", checkedMovies);
            if (libreHandler.Calls != 71 || r18Handler.Calls != 2 ||
                checkedMovies.Any(job => job.Metadata.Title != "R18 fixture") ||
                many.Skip(2).Any(job => job.Metadata.Title != "Libre fixture"))
                throw new InvalidOperationException("Repeat lookup did not apply R18 only to the checked movies.");
            foreach (var job in checkedMovies)
            {
                if (job.MetadataReview.GetCandidates(MetadataField.Title).Count != 2 ||
                    !job.MetadataReview.SelectCandidate(MetadataField.Title, "libredmm") || job.Metadata.Title != "Libre fixture" ||
                    !job.MetadataReview.SelectCandidate(MetadataField.Title, "r18dev") || job.Metadata.Title != "R18 fixture")
                    throw new InvalidOperationException("The previous provider was lost after a second single-source lookup.");
            }
            Search("custom", checkedMovies);
            if (libreHandler.Calls != 73 || r18Handler.Calls != 4) throw new InvalidOperationException("Custom queue did not request both sources.");
            typeof(MainWindow).GetField("_customSourceProfile", flags)!.SetValue(window,
                new MetadataSourcePreferenceProfile { TitleSource = "r18dev" });
            Search("custom", checkedMovies);
            if (libreHandler.Calls != 75 || r18Handler.Calls != 6 || checkedMovies.Any(job => job.Metadata.Title != "R18 fixture"))
            {
                throw new InvalidOperationException("Custom queue routing or preferred field selection failed.");
            }

            if (source.Items.OfType<ComboBoxItem>().Any(item => Equals(item.Tag, "manual")))
                throw new InvalidOperationException("Manual-only source is still visible.");
            // Previous R18 failures cannot be silently retried after selecting LibreDMM.
            var failed = new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null,
                new MetadataSourceRateLimitException(DateTimeOffset.UtcNow.AddMinutes(1)), 0);
            var prior = many[0].LastSearchAttempts.Single(attempt => attempt.SourceName == "libredmm");
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
            var websites = OpenManualWebsites(window);
            if (!websites.Any(item => Equals(item.Tag, "javlibrary")))
                throw new InvalidOperationException("Manual current-movie JAVLibrary lookup disappeared.");
            source.IsDropDownOpen = false;
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
            ClickWorkspaceButton(window, "EditReviewButton");
            var rating = (Button)window.FindName("RatingSourceText");
            if (!rating.IsVisible || !rating.IsEnabled || active.MetadataReview.GetCandidates(MetadataField.Rating).Count != 1)
                throw new InvalidOperationException("A prior source with a rating cannot be chosen on an empty field.");
            rating.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var choice = rating.ContextMenu.Items.OfType<MenuItem>().Single();
            choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            rating.ContextMenu.IsOpen = false;
            if (active.Metadata.Rating != "8.5" || active.Metadata.Title != "Libre fixture")
                throw new InvalidOperationException("Choosing an alternate rating changed unrelated fields.");
            ClickWorkspaceButton(window, "FinishEditButton");
            foreach (var oldMode in new[] { "manual", "javlibrary" })
            {
                typeof(MainWindow).GetMethod("ApplyPreferences", flags)!.Invoke(window,
                    [new AppPreferences { RememberSavePreferences = true, SearchSourceMode = oldMode }]);
                if ((source.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "libredmm")
                    throw new InvalidOperationException("Retired source preference did not resolve to LibreDMM.");
            }
            typeof(MainWindow).GetMethod("ApplyPreferences", flags)!.Invoke(window,
                [new AppPreferences { RememberSavePreferences = true, SearchSourceMode = "auto" }]);
            if ((source.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "libredmm")
                throw new InvalidOperationException("Legacy Auto preference did not migrate to LibreDMM.");

            var batchSearch = (Button)window.FindName("SearchQueueButton");
            var language = (ComboBox)window.FindName("LanguageComboBox");
            window.Width = 930;
            window.Height = 650;
            foreach (var code in UiLanguageCodes.Supported)
            {
                language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == code);
                foreach (var mode in new[] { "libredmm", "r18dev", "custom" })
                {
                    Select(mode);
                    window.UpdateLayout();
                    var hint = LocalizationService.Get(mode == "libredmm" ? "Main.BatchSource.DmmHint" : "Main.BatchSource.R18Hint");
                    if (batchSearch.ToolTip?.ToString()?.Contains(hint) != true)
                        throw new InvalidOperationException($"Batch source tooltip missing: {code}/{mode}.");
                }
            }
            Select("r18dev");
            SetQueueOpen(window, false);
            window.UpdateLayout();

            if (libreHandler.Calls != 76 || r18Handler.Calls != 7 || javHandler.Calls != 0)
                throw new InvalidOperationException("Source/language changes triggered unintended searches.");
            // Editing the ID must initiate just this lookup, even with a checked batch and a previous result.
            ((TextBox)window.FindName("IdTextBox")).Text = "IPX-812";
            Select("libredmm");
            r18Handler.Hold = true;
            websites = OpenManualWebsites(window);
            if (r18Handler.HeldRequest is not null || libreHandler.Calls != 76)
                throw new InvalidOperationException("Opening manual search made an automatic request.");
            var site = websites.Single(item => Equals(item.Tag, "r18dev"));
            var requested = string.Empty;
            InteractWithModal<BrowserWindow>(window, () => site.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), browser =>
            {
                if (source.IsDropDownOpen || ((StackPanel)window.FindName("ManualWebsiteChoices")).IsVisible)
                    throw new InvalidOperationException("Choosing a website did not close the source menu.");
                WaitForCondition(() => r18Handler.HeldRequest is not null, "Manual search did not request the current ID.");
                requested = r18Handler.HeldRequest!.AbsoluteUri;
                browser.Close();
            });
            WaitForCondition(() => r18Handler.HeldRequestCanceled, "Closing the browser left its lookup running.");
            if (!requested.Contains("812", StringComparison.Ordinal) || !Equals(((ComboBoxItem)source.SelectedItem).Tag, "libredmm") ||
                libreHandler.Calls != 76 || r18Handler.Calls != 8 || javHandler.Calls != 0)
                throw new InvalidOperationException("Manual search opened a chooser, reused an old ID or searched the batch.");
            Console.WriteLine("UI PASS queueSourceRouting=True requeryCheckedAcrossSources=True libre71R18Zero=True sourceSnapshot=True r18Only=True customBoth=True singleSourceParity=True manualSourceRemoved=True retryScope=True javLibraryManualOnly=True legacySourceMigration=True dmmDefault=True autoRetired=True batchHintsFourLanguages=True compactHintLayout=True manualSearchUsesCurrentId=True");
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
        public bool Hold { get; set; }
        public Uri? HeldRequest { get; private set; }
        public bool HeldRequestCanceled { get; private set; }
        public int Calls => Volatile.Read(ref _calls);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (Hold)
            {
                HeldRequest = request.RequestUri;
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                finally { HeldRequestCanceled = cancellationToken.IsCancellationRequested; }
            }
            await Task.Delay(2, cancellationToken);
            var uri = request.RequestUri!;
            var body = uri.Host switch
            {
                "www.libredmm.com" when uri.Segments is ["/", "api/", "v1/", "movies/", var code] =>
                    JsonSerializer.Serialize(new { movie = new { code, title = "Libre fixture" } }),
                "r18.dev" => "{\"title_en\":\"R18 fixture\"}",
                _ => throw new InvalidOperationException($"Unexpected queue search request: {uri}")
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
