using System.Net;
using System.Xml.Linq;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

internal static partial class Program
{
    private const string LibraryDetail = "https://www.javlibrary.com/cn/?v=synthetic";
    private static async Task ManualBrowserProviders()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new InspectableHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri; calls.Add(url);
            return JsonReply(Fixture(url.Contains("r18.dev") ? "r18dev.json" : "libredmm.json"));
        }));
        using var libre = new LibreDmmClient(http);
        using var r18 = new R18DevClient(http, ImmediateR18Scheduler());
        using var library = new JavLibraryClient(http);
        var importer = new BrowserPageImporter(libre, r18, library);
        var html = Fixture("javlibrary-detail.html");
        var captured = await importer.ImportAsync("javlibrary", LibraryDetail, html, "DEMO-001");
        Equal(0, calls.Count); Equal("Synthetic browser title", captured.Title); Equal("javlibrary", captured.SourceName);
        Equal("Synthetic actor", captured.ActorsText);
        Equal("https://example.invalid/syntheticpl.jpg", captured.CoverUrl);
        foreach (var language in new[] { "cn", "tw", "en", "ja" })
        {
            var canonical = $"https://www.javlibrary.com/{language}/javli43n4q.html";
            var current = await importer.ImportAsync("javlibrary", canonical, html, "DEMO-001");
            Equal(canonical, current.SourceUrl); Equal("DEMO-001", current.Id); Equal(0, calls.Count);
            await Throws<BrowserPageImportException>(() => importer.ImportAsync("javlibrary", canonical, html, "DEMO-002"));
        }
        var libreResult = await importer.ImportAsync("libredmm", "https://www.libredmm.com/movies/DEMO-001", "<html>Captured</html>", "DEMO-001");
        Equal("Synthetic provider title", libreResult.Title); Equal(1, calls.Count);
        var r18Result = await importer.ImportAsync("r18dev", "https://r18.dev/videos/vod/movies/detail/-/id=demoactual001/", "<div id=\"video-info\">Captured</div>", "DEMO-001");
        Equal("Synthetic R18 title", r18Result.Title); Equal(2, calls.Count);
        Check(calls.Last().Contains("combined=demoactual001/json"), "Browser import did not use authoritative R18 content ID");
    }

    private static async Task ManualBrowserGuards()
    {
        var calls = 0;
        using var http = new HttpClient(new InspectableHandler(_ => { calls++; return JsonReply(Fixture("r18dev.json")); }));
        using var libre = new LibreDmmClient(http); using var r18 = new R18DevClient(http, ImmediateR18Scheduler()); using var library = new JavLibraryClient(http);
        var importer = new BrowserPageImporter(libre, r18, library);
        var html = Fixture("javlibrary-detail.html");
        foreach (var url in new[] { "file:///tmp/private.html", "javascript:alert(1)", "https://javlibrary.com.evil.invalid/?v=x", "https://user:password@www.javlibrary.com/cn/?v=x", "https://www.javlibrary.com:444/cn/?v=x", "https://www.javlibrary.com/cn/vl_searchbyid.php?keyword=DEMO-001", "https://www.javlibrary.com/cn/main.html", "https://www.javlibrary.com/cn/javbad!.html", "https://www.javlibrary.com/cn/extra/javli43n4q.html", "https://www.javlibrary.com/cn/vl_searchbyid.php" })
            await Throws<BrowserPageImportException>(() => importer.ImportAsync("javlibrary", url, html, "DEMO-001"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("javlibrary", LibraryDetail, new string('界', BrowserPageImporter.MaximumHtmlBytes / 3 + 1), "DEMO-001"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("javlibrary", LibraryDetail, "<title>Just a moment</title><script>challenge-platform</script>", "DEMO-001"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("javlibrary", LibraryDetail, html.Replace("<span class=\"text\">DEMO-001</span>", "<span class=\"text\"></span>"), "DEMO-001"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("javlibrary", LibraryDetail, html, "DEMO-002"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("libredmm", "https://www.libredmm.com/movies/DEMO-002", html, "DEMO-001"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("r18dev", "https://r18.dev/videos/vod/movies/detail/-/id=demoactual001/", html, "DEMO-001"));
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("r18dev", "https://r18.dev/videos/vod/movies/detail/-/id=demoactual001/", "<div id=\"video-info\">Sorry, this page does not exist</div>", "DEMO-001"));
        Equal(0, calls);
        await Throws<BrowserPageImportException>(() => importer.ImportAsync("r18dev", "https://r18.dev/videos/vod/movies/detail/-/id=demoactual001/", "<div id=\"video-info\"></div>", "DEMO-002"));
        Equal(1, calls);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Throws<OperationCanceledException>(() => importer.ImportAsync("javlibrary", LibraryDetail, html, "DEMO-001", canceled.Token));
    }

    private static async Task ManualBrowserReviewRoundTrip()
    {
        using var temp = new Temp();
        var video = temp.Write("DEMO-001.mp4", "SYNTHETIC"); var nfo = temp.Write("movie.nfo", Fixture("round-trip.nfo"));
        using var job = new MovieJob(); await MovieJobLoader.LoadAsync(job, video, Images);
        job.MetadataReview.SetManualValue(MetadataField.Title, "Earlier manual title");
        var baseline = job.LocalMetadataBundle; var revision = job.ReviewRevision;
        using var http = new HttpClient(new InspectableHandler(_ => throw new InvalidOperationException("Captured library HTML must not make HTTP requests")));
        using var libre = new LibreDmmClient(http); using var r18 = new R18DevClient(http, ImmediateR18Scheduler()); using var library = new JavLibraryClient(http);
        var source = await new BrowserPageImporter(libre, r18, library).ImportAsync("javlibrary", LibraryDetail, Fixture("javlibrary-detail.html"), "DEMO-001");
        Check(job.ApplyManualWebSource(source) > 0, "Manual browser import promoted no fields");
        Equal("Synthetic browser title", job.Metadata.Title);
        Check(ReferenceEquals(baseline, job.LocalMetadataBundle), "Browser import replaced local XML baseline");
        Check(job.ReviewRevision > revision, "Browser import did not invalidate previews");
        Check(job.MetadataReview.GetCandidates(MetadataField.Title).Any(c => c.Value == "Earlier manual title"), "Explicit import lost earlier manual candidate");
        Check(job.MetadataReview.SelectCandidate(MetadataField.Title, "manual"), "Manual candidate could not be reselected");
        var plan = FileOrganizationService.BuildPlan(video, job.Metadata, NfoOnly, InPlace, job.CreateLocalSaveContext());
        using var output = new OutputService(Images);
        await new FileOrganizationService(output).ExecuteAsync(plan, job.Metadata, true);
        var xml = XDocument.Load(nfo);
        Equal("KEEP UNKNOWN XML", xml.Root!.Element("customNode")!.Element("nested")!.Value);
        Equal("Earlier manual title", xml.Root.Element("title")!.Value);
        Equal("SYNTHETIC", File.ReadAllText(video));
    }
}
