using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

if (args.Length == 2 && args[0] == "--live")
{
    using var liveClient = new R18DevClient();
    var liveResult = await liveClient.SearchAsync(args[1]);
    Console.WriteLine(
        $"LIVE R18 PASS  source={liveResult.SourceName} id={liveResult.Id} " +
        $"titleJapanese={ContainsJapanese(liveResult.Title)} originalJapanese={ContainsJapanese(liveResult.OriginalTitle)} " +
        $"actor={liveResult.ActorsText} cover={liveResult.CoverUrl} screenshots={liveResult.ScreenshotUrls.Count}");
    return;
}

if (args.Length == 2 && args[0] == "--live-libredmm")
{
    using var liveClient = new LibreDmmClient();
    var liveResult = await liveClient.SearchAsync(args[1]);
    Console.WriteLine(
        $"LIVE LIBREDMM PASS  source={liveResult.SourceDisplayName} id={liveResult.Id} " +
        $"titlePresent={!string.IsNullOrWhiteSpace(liveResult.Title)} plotLength={liveResult.Plot.Length} " +
        $"screenshots={liveResult.ScreenshotUrls.Count} actorThumbs={liveResult.Actors.Count(actor => !string.IsNullOrWhiteSpace(actor.ImageUrl))} " +
        $"runtime={liveResult.RuntimeMinutes}");
    return;
}

if (args.Length == 3 && args[0] == "--live-artwork")
{
    var outputDirectory = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(outputDirectory);
    using var liveClient = new LibreDmmClient();
    using var outputService = new OutputService();
    var liveResult = await liveClient.SearchAsync(args[1]);
    var videoPath = Path.Combine(outputDirectory, $"{liveResult.Id}.mp4");
    await File.WriteAllBytesAsync(videoPath, []);
    var saveResult = await outputService.SaveAsync(
        videoPath,
        liveResult,
        new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, true));
    var posterSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(saveResult.PosterPath!));
    var fanartSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(saveResult.FanartPath!));
    Console.WriteLine(
        $"LIVE ARTWORK PASS  id={liveResult.Id} poster={posterSize.Width}x{posterSize.Height} " +
        $"fanart={fanartSize.Width}x{fanartSize.Height} fullCover={saveResult.FanartUsedFullCover} " +
        $"extrafanart={saveResult.ExtrafanartPaths.Count} nfo={File.Exists(saveResult.NfoPath)}");
    return;
}

if (args.Length == 3 && args[0] == "--live-r18-artwork")
{
    var outputDirectory = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(outputDirectory);
    using var liveClient = new R18DevClient();
    using var outputService = new OutputService();
    var liveResult = await liveClient.SearchAsync(args[1]);
    var videoPath = Path.Combine(outputDirectory, $"{liveResult.Id}.mp4");
    await File.WriteAllBytesAsync(videoPath, []);
    var saveResult = await outputService.SaveAsync(
        videoPath,
        liveResult,
        new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, true));
    var posterSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(saveResult.PosterPath!));
    var fanartSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(saveResult.FanartPath!));
    Console.WriteLine(
        $"LIVE R18 ARTWORK PASS  id={liveResult.Id} poster={posterSize.Width}x{posterSize.Height} " +
        $"fanart={fanartSize.Width}x{fanartSize.Height} fullCover={saveResult.FanartUsedFullCover} " +
        $"extrafanart={saveResult.ExtrafanartPaths.Count} nfo={File.Exists(saveResult.NfoPath)}");
    return;
}

if (args.Length == 2 && args[0] == "--image")
{
    var source = await File.ReadAllBytesAsync(args[1]);
    var poster = PosterImageProcessor.CreatePosterJpeg(source);
    var fanart = PosterImageProcessor.CreateFanartJpeg(source);
    var posterSize = PosterImageProcessor.GetDimensions(poster);
    var fanartSize = PosterImageProcessor.GetDimensions(fanart);
    Console.WriteLine(
        $"IMAGE PASS  poster={posterSize.Width}x{posterSize.Height} ({poster.Length} bytes) " +
        $"fanart={fanartSize.Width}x{fanartSize.Height} ({fanart.Length} bytes)");
    return;
}

var tests = new List<(string Name, Func<Task> Run)>
{
    ("番号识别", TestMovieIdParser),
    ("LibreDMM JSON 解析与清理", TestLibreDmmParser),
    ("多来源字段补全", TestMetadataMerge),
    ("R18.dev JSON 解析", TestR18Parser),
    ("R18.dev 实际 content_id 回退", TestR18ContentIdFallback),
    ("JAVLibrary HTML 解析", TestHtmlParser),
    ("高清海报自动裁切", TestPosterCropping),
    ("NFO 生成", TestNfoWriter),
    ("完整封套 fanart 与 Sample Images 输出", TestArtworkOutput),
    ("v0.4 文件整理计划与安全执行", TestFileOrganization),
    ("v0.4 本地运行日志", TestAppLog)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    Console.WriteLine($"\n{failures.Count} test(s) failed.");
}
else
{
    Console.WriteLine($"\nAll {tests.Count} smoke tests passed.");
}

return;

static Task TestMovieIdParser()
{
    AssertEqual("IPX-123", MovieIdParser.TryExtract("[4K] IPX-123-C.mp4"));
    AssertEqual("SSIS-001", MovieIdParser.TryExtract("ssis001_uncensored.mkv"));
    AssertEqual("FC2-PPV-1234567", MovieIdParser.TryExtract("FC2-PPV-1234567.mp4"));
    AssertEqual(null, MovieIdParser.TryExtract("vacation-1080p.mp4"));
    return Task.CompletedTask;
}

static async Task TestHtmlParser()
{
    const string html = """
        <html><head><title>IPX-123 示例标题 - JAVLibrary</title><meta name="description" content="示例简介" /></head>
        <body><div id="video_info">
          <div id="video_id"><span class="text">IPX-123</span></div>
          <div id="video_date"><span class="text">2024-02-03</span></div>
          <div id="video_length"><span class="text">120 分钟</span></div>
          <div id="video_maker"><span class="text"><a>示例片商</a></span></div>
          <div id="video_cast"><span class="star"><a>演员甲</a></span><span class="star"><a>演员乙</a></span></div>
          <div id="video_genres"><span class="genre"><a>剧情</a></span><span class="genre"><a>高清</a></span></div>
          <img id="video_jacket_img" src="//img.example.test/ipx123pl.jpg" />
        </div></body></html>
        """;

    using var client = new JavLibraryClient();
    var result = await client.ParseDetailPageAsync(html, "https://www.javlibrary.com/cn/?v=test");
    AssertEqual("IPX-123", result.Id);
    AssertEqual("示例标题", result.Title);
    AssertEqual("演员甲, 演员乙", result.ActorsText);
    AssertEqual("https://img.example.test/ipx123ps.jpg", result.PosterUrl);
}

static Task TestLibreDmmParser()
{
    const string json = """
        {
          "actresses": [{"name":"桜空もも","image_url":"https://example.test/actor.jpg"}],
          "cover_image_url":"https://pics.dmm.co.jp/mono/movie/adult/ipzz850/ipzz850pl.jpg",
          "date":"2026-06-08T17:00:01.000-07:00",
          "description":"正文第一行。正文第二行。「コンビニ受取」対象商品です。詳しくはこちらをご覧ください。",
          "directors":["U吉"],
          "genres":["中出し","単体作品"],
          "labels":["ティッシュ"],
          "makers":["アイデアポケット"],
          "normalized_id":"IPZZ-850",
          "review":4.5,
          "subtitle":"ipzz850",
          "thumbnail_image_url":"https://pics.dmm.co.jp/mono/movie/adult/ipzz850/ipzz850ps.jpg",
          "title":"日本語タイトル",
          "url":"https://www.dmm.co.jp/test",
          "volume":8400,
          "sample_image_urls":[
            "https://pics.dmm.co.jp/digital/video/ipzz00850/ipzz00850-1.jpg",
            "https://pics.dmm.co.jp/digital/video/ipzz00850/ipzz00850-2.jpg"
          ]
        }
        """;

    using var client = new LibreDmmClient();
    var result = client.ParseJson(json, "https://www.libredmm.com/movies/IPZZ-850.json", "IPZZ-850");
    AssertEqual("IPZZ-850", result.Id);
    AssertEqual("2026-06-09", result.ReleaseDate);
    AssertEqual("140", result.RuntimeMinutes);
    AssertEqual("正文第一行。正文第二行。", result.Plot);
    AssertEqual("桜空もも", result.ActorsText);
    AssertEqual("https://example.test/actor.jpg", result.Actors[0].ImageUrl);
    AssertEqual("4.5", result.Rating);
    AssertEqual("2", result.ScreenshotUrls.Count.ToString());
    AssertEqual("https://awsimgsrc.dmm.com/pics_dig/digital/video/ipzz00850/ipzz00850pl.jpg", result.CoverUrl);
    AssertEqual("https://pics.dmm.co.jp/mono/movie/adult/ipzz850/ipzz850pl.jpg", result.FallbackCoverUrl);
    AssertEqual("https://pics.dmm.co.jp/digital/video/ipzz00850/ipzz00850jp-1.jpg", result.ScreenshotUrls[0]);
    AssertEqual("LibreDMM", result.SourceDisplayName);
    return Task.CompletedTask;
}

static Task TestMetadataMerge()
{
    var primary = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "日文标题",
        Plot = "完整简介",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    var fallback = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "English title",
        Series = "系列补充",
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        ScreenshotUrls = ["https://example.test/sample.jpg"]
    };

    var merged = MetadataMerger.Merge(primary, fallback);
    AssertEqual("日文标题", merged.Title);
    AssertEqual("完整简介", merged.Plot);
    AssertEqual("系列补充", merged.Series);
    AssertEqual("1", merged.ScreenshotUrls.Count.ToString());
    AssertEqual("LibreDMM + R18.dev", merged.SourceDisplayName);
    return Task.CompletedTask;
}

static Task TestR18Parser()
{
    const string json = """
        {
          "dvd_id": "IPX-123",
          "content_id": "ipx00123",
          "title_ja": "日本語タイトル",
          "title_en": "English title",
          "release_date": "2024-02-03",
          "runtime_mins": 120,
          "jacket_full_url": "https://pics.example.test/ipx123pl.jpg",
          "maker_name_ja": "片商甲",
          "label_name_ja": "厂牌甲",
          "series_name_ja": "系列甲",
          "actresses": [{ "name_kanji": "演员甲", "name_romaji": "Actress A" }],
          "directors": [{ "name_kanji": "导演甲", "name_romaji": "Director A" }],
          "categories": [{ "name_ja": "剧情", "name_en": "Drama" }]
          ,"gallery": [{ "image_full": "https://pics.dmm.co.jp/digital/video/ipx00123/ipx00123-1.jpg" }]
        }
        """;

    using var client = new R18DevClient();
    var result = client.ParseJson(json, "https://r18.dev/test", "IPX-123");
    AssertEqual("IPX-123", result.Id);
    AssertEqual("English title", result.Title);
    AssertEqual("日本語タイトル", result.OriginalTitle);
    AssertEqual("Actress A", result.ActorsText);
    AssertEqual("Director A", result.Director);
    AssertEqual("Drama", result.GenresText);
    AssertEqual("ipx00123", result.ContentId);
    AssertEqual("https://awsimgsrc.dmm.com/dig/digital/video/ipx00123/ipx00123pl.jpg", result.CoverUrl);
    AssertEqual("https://pics.example.test/ipx123pl.jpg", result.FallbackCoverUrl);
    AssertEqual("https://pics.example.test/ipx123ps.jpg", result.PosterUrl);
    AssertEqual("r18dev", result.SourceName);
    AssertEqual("https://awsimgsrc.dmm.com/dig/digital/video/ipx00123/ipx00123jp-1.jpg", result.ScreenshotUrls[0]);

    const string compactJson = """
        {
          "content_id": "ipx00123",
          "title": "日本語タイトル",
          "release_date": "2024-02-03",
          "runtime_minutes": 120,
          "director": "导演甲",
          "maker": { "name": "片商甲" },
          "label": { "name": "厂牌甲" },
          "actresses": [{ "name": "演员甲" }],
          "categories": [{ "name": "剧情" }],
          "images": { "jacket_image": { "large": "https://pics.example.test/ipx123pl.jpg" } }
        }
        """;
    var compactResult = client.ParseJson(compactJson, "https://r18.dev/test", "IPX-123");
    AssertEqual("IPX-123", compactResult.Id);
    AssertEqual("日本語タイトル", compactResult.Title);
    AssertEqual("演员甲", compactResult.ActorsText);
    AssertEqual("120", compactResult.RuntimeMinutes);
    return Task.CompletedTask;
}

static async Task TestR18ContentIdFallback()
{
    const string compactJson = """
        {
          "content_id": "1start237",
          "title": "English compact title",
          "release_date": "2025-01-09",
          "images": { "jacket_image": { "large2": "https://pics.dmm.co.jp/mono/movie/adult/1start237/1start237pl.jpg" } }
        }
        """;
    const string detailedJson = """
        {
          "dvd_id": "START-237",
          "content_id": "1start237",
          "title_en": "English detailed title",
          "title_ja": "日本語の詳細タイトル",
          "jacket_full_url": "https://pics.dmm.co.jp/mono/movie/adult/1start237/1start237pl.jpg",
          "gallery": [
            { "image_full": "https://pics.dmm.co.jp/digital/video/1start237/1start237-1.jpg" },
            { "image_full": "https://pics.dmm.co.jp/digital/video/1start237/1start237-2.jpg" }
          ]
        }
        """;
    using var httpClient = new HttpClient(new FakeJsonHandler(new Dictionary<string, (HttpStatusCode, string)>
    {
        ["https://r18.dev/videos/vod/movies/detail/-/combined=start00237/json"] = (HttpStatusCode.NotFound, string.Empty),
        ["https://r18.dev/videos/vod/movies/detail/-/dvd_id=start237/json"] = (HttpStatusCode.OK, compactJson),
        ["https://r18.dev/videos/vod/movies/detail/-/combined=1start237/json"] = (HttpStatusCode.OK, detailedJson)
    }));
    using var client = new R18DevClient(httpClient);
    var result = await client.SearchAsync("START-237");

    AssertEqual("START-237", result.Id);
    AssertEqual("1start237", result.ContentId);
    AssertEqual("English detailed title", result.Title);
    AssertEqual("日本語の詳細タイトル", result.OriginalTitle);
    AssertEqual("2", result.ScreenshotUrls.Count.ToString());
    AssertEqual(
        "https://awsimgsrc.dmm.com/dig/mono/movie/1start237/1start237pl.jpg",
        result.CoverUrl);
    AssertEqual(
        "https://awsimgsrc.dmm.com/dig/digital/video/1start237/1start237jp-1.jpg",
        result.ScreenshotUrls[0]);
}

static Task TestPosterCropping()
{
    var landscapePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAYAAAB/qH1jAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAASSURBVBhXY0iZ+vY/MmZAFwAAgTUXKQi+42gAAAAASUVORK5CYII=");
    var poster = PosterImageProcessor.CreatePosterJpeg(landscapePng);
    var fanart = PosterImageProcessor.CreateFanartJpeg(landscapePng);
    var posterSize = PosterImageProcessor.GetDimensions(poster);
    var fanartSize = PosterImageProcessor.GetDimensions(fanart);

    AssertEqual("2", posterSize.Width.ToString());
    AssertEqual("2", posterSize.Height.ToString());
    AssertEqual("4", fanartSize.Width.ToString());
    AssertEqual("2", fanartSize.Height.ToString());
    return Task.CompletedTask;
}

static async Task TestNfoWriter()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.Tests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var path = Path.Combine(root, "IPX-123.nfo");
        var metadata = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "示例标题",
            OriginalTitle = "日本語タイトル",
            ReleaseDate = "2024-02-03",
            RuntimeMinutes = "120",
            Maker = "示例片商",
            ActorsText = "演员甲, 演员乙",
            Actors = [new ActorMetadata("演员甲", "https://images.example.test/actor-a.jpg")],
            GenresText = "剧情，高清",
            Plot = "示例简介",
            SourceUrl = "https://www.javlibrary.com/cn/?v=test"
        };

        await NfoWriter.WriteAsync(path, metadata, "IPX-123-poster.jpg", "IPX-123-fanart.jpg", false);
        var document = XDocument.Load(path);
        AssertEqual("示例标题", document.Root?.Element("title")?.Value);
        AssertEqual("2024", document.Root?.Element("year")?.Value);
        AssertEqual("2", document.Root?.Elements("actor").Count().ToString());
        AssertEqual("https://images.example.test/actor-a.jpg", document.Root?.Elements("actor").First()?.Element("thumb")?.Value);
        AssertEqual("IPX-123-poster.jpg", document.Root?.Element("thumb")?.Value);
        AssertEqual("IPX-123-fanart.jpg", document.Root?.Element("fanart")?.Element("thumb")?.Value);
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static async Task TestArtworkOutput()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.OutputTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "IPZZ-850.mp4");
        await File.WriteAllBytesAsync(videoPath, []);
        var cover = CreateJpeg(800, 538, 0x2B, 0x65, 0xA8);
        var sample1 = CreateJpeg(640, 360, 0xA8, 0x45, 0x45);
        var sample2 = CreateJpeg(800, 450, 0x45, 0xA8, 0x65);
        using var httpClient = new HttpClient(new FakeImageHandler(new Dictionary<string, byte[]>
        {
            ["/cover.jpg"] = cover,
            ["/sample1.jpg"] = sample1,
            ["/sample2.jpg"] = sample2
        }));
        using var service = new OutputService(httpClient);
        var metadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "测试影片",
            CoverUrl = "https://images.example.test/cover.jpg",
            PosterUrl = "https://images.example.test/cover.jpg",
            ScreenshotUrls =
            [
                "https://images.example.test/sample1.jpg",
                "https://images.example.test/sample2.jpg"
            ],
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };

        var result = await service.SaveAsync(
            videoPath,
            metadata,
            new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, false));
        AssertEqual("True", File.Exists(result.NfoPath).ToString());
        AssertEqual("True", File.Exists(result.PosterPath).ToString());
        AssertEqual("True", File.Exists(result.FanartPath).ToString());
        AssertEqual("2", result.ExtrafanartPaths.Count.ToString());
        AssertEqual("True", result.FanartUsedFullCover.ToString());
        var fanartSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(result.FanartPath!));
        AssertEqual("800", fanartSize.Width.ToString());
        AssertEqual("538", fanartSize.Height.ToString());
        var conflicts = OutputService.FindExistingOutputFiles(
            videoPath,
            metadata,
            new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, false));
        AssertEqual("5", conflicts.Count.ToString());
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static async Task TestFileOrganization()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.OrganizationTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var originalMovieDirectory = Path.Combine(root, "SNOS-255-UC");
        Directory.CreateDirectory(originalMovieDirectory);
        var sourcePath = Path.Combine(originalMovieDirectory, "489155.com@SNOS-255-UC.mp4");
        await File.WriteAllBytesAsync(sourcePath, [0x01, 0x02, 0x03]);
        var metadata = new MovieMetadata
        {
            Id = "snos-255",
            Title = "初始标题",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var saveOptions = new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, false);
        AssertEqual("True", saveOptions.RequiresPreview.ToString());
        AssertEqual(
            "False",
            new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, true).RequiresPreview.ToString());
        var organizationOptions = new OrganizationOptions(true, true);
        var plan = FileOrganizationService.BuildPlan(
            sourcePath,
            metadata,
            saveOptions,
            organizationOptions);

        var expectedDirectory = Path.Combine(originalMovieDirectory, "SNOS-255");
        var expectedVideoPath = Path.Combine(expectedDirectory, "SNOS-255.mp4");
        AssertEqual(expectedVideoPath, plan.TargetVideoPath);
        AssertEqual("False", plan.HasBlockingConflicts.ToString());
        AssertEqual("True", plan.Changes.Any(change => change.Kind == PlannedChangeKind.CreateFolder).ToString());
        AssertEqual("True", plan.Changes.Any(change => change.Kind == PlannedChangeKind.MoveAndRenameVideo).ToString());

        using var outputService = new OutputService();
        var organizer = new FileOrganizationService(outputService);
        var result = await organizer.ExecuteAsync(plan, metadata, false);
        AssertEqual("False", File.Exists(sourcePath).ToString());
        AssertEqual("True", File.Exists(expectedVideoPath).ToString());
        AssertEqual("True", File.Exists(Path.Combine(expectedDirectory, "SNOS-255.nfo")).ToString());
        AssertEqual("True", result.VideoMoved.ToString());

        var overwritePlan = FileOrganizationService.BuildPlan(
            expectedVideoPath,
            metadata,
            saveOptions,
            organizationOptions);
        AssertEqual("1", overwritePlan.OverwriteConflicts.Count.ToString());
        var refused = false;
        try
        {
            await organizer.ExecuteAsync(overwritePlan, metadata, false);
        }
        catch (IOException)
        {
            refused = true;
        }
        AssertEqual("True", refused.ToString());

        metadata.Title = "覆盖后的标题";
        var overwriteResult = await organizer.ExecuteAsync(overwritePlan, metadata, true);
        var nfo = XDocument.Load(overwriteResult.Outputs.NfoPath!);
        AssertEqual("覆盖后的标题", nfo.Root?.Element("title")?.Value);

        var secondSource = Path.Combine(originalMovieDirectory, "another-SNOS-255.mp4");
        await File.WriteAllBytesAsync(secondSource, [0x04]);
        var blockedPlan = FileOrganizationService.BuildPlan(
            secondSource,
            metadata,
            saveOptions,
            organizationOptions);
        AssertEqual("True", blockedPlan.HasBlockingConflicts.ToString());
        AssertEqual("True", File.Exists(secondSource).ToString());

        var rollbackSource = Path.Combine(root, "rollback-test.mp4");
        await File.WriteAllBytesAsync(rollbackSource, [0x05, 0x06]);
        var rollbackMetadata = new MovieMetadata { Id = "IPX-999", Title = "回滚测试" };
        var rollbackPlan = FileOrganizationService.BuildPlan(
            rollbackSource,
            rollbackMetadata,
            saveOptions,
            organizationOptions);
        var rollbackFailed = false;
        using (var lockedVideo = new FileStream(rollbackSource, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                await organizer.ExecuteAsync(rollbackPlan, rollbackMetadata, false);
            }
            catch (IOException)
            {
                rollbackFailed = true;
            }
        }
        AssertEqual("True", rollbackFailed.ToString());
        AssertEqual("True", File.Exists(rollbackSource).ToString());
        AssertEqual("False", File.Exists(rollbackPlan.TargetVideoPath).ToString());
        AssertEqual("False", File.Exists(Path.Combine(rollbackPlan.TargetDirectory, "IPX-999.nfo")).ToString());
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static Task TestAppLog()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.LogTests.{Guid.NewGuid():N}");
    try
    {
        AppLog.ConfigureDirectory(root);
        AppLog.Info("smoke-log-marker");
        AssertEqual("True", File.Exists(AppLog.CurrentLogPath).ToString());
        AssertEqual("True", File.ReadAllText(AppLog.CurrentLogPath).Contains("smoke-log-marker", StringComparison.Ordinal).ToString());
    }
    finally
    {
        AppLog.ConfigureDirectory(null);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    return Task.CompletedTask;
}

static byte[] CreateJpeg(int width, int height, byte red, byte green, byte blue)
{
    var stride = width * 4;
    var pixels = new byte[stride * height];
    for (var index = 0; index < pixels.Length; index += 4)
    {
        pixels[index] = blue;
        pixels[index + 1] = green;
        pixels[index + 2] = red;
        pixels[index + 3] = 0xFF;
    }

    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}

static void AssertEqual(string? expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected ?? "<null>"}', got '{actual ?? "<null>"}'.");
    }
}

static bool ContainsJapanese(string? value) =>
    (value ?? string.Empty).Any(character =>
        character is >= '\u3040' and <= '\u30ff' or >= '\u4e00' and <= '\u9fff');

internal sealed class FakeImageHandler(IReadOnlyDictionary<string, byte[]> images) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || !images.TryGetValue(request.RequestUri.AbsolutePath, out var bytes))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}

internal sealed class FakeJsonHandler(
    IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || !responses.TryGetValue(request.RequestUri.AbsoluteUri, out var response))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(response.Status)
        {
            Content = new StringContent(response.Body)
        });
    }
}
