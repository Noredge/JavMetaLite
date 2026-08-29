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
    ("v0.8 启动影片参数解析", TestStartupVideoRequestResolver),
    ("v0.8 安全偏好原子存储", TestAppPreferencesStore),
    ("LibreDMM JSON 解析与清理", TestLibreDmmParser),
    ("多来源字段补全", TestMetadataMerge),
    ("v0.5 多来源搜索编排", TestMetadataSearchCoordinator),
    ("v0.5 字段候选与来源追踪", TestMetadataReviewSession),
    ("v0.5 poster 与 fanart 统一封套来源", TestArtworkCoverReviewSession),
    ("v0.6 本地与手动封套统一候选", TestLocalArtworkCoverReviewSession),
    ("R18.dev JSON 解析", TestR18Parser),
    ("R18.dev 实际 content_id 回退", TestR18ContentIdFallback),
    ("JAVLibrary HTML 解析", TestHtmlParser),
    ("高清海报自动裁切", TestPosterCropping),
    ("NFO 生成", TestNfoWriter),
    ("完整封套 fanart 与 Sample Images 输出", TestArtworkOutput),
    ("v0.6 本地完整封套输出", TestLocalCompleteCoverOutput),
    ("v0.4 文件整理计划与安全执行", TestFileOrganization),
    ("v0.6 本地 sidecar 定位", TestLocalSidecarLocator),
    ("v0.6 本地图片发现与损坏隔离", TestLocalArtworkDiscovery),
    ("v0.6 安全 NFO 只读解析", TestNfoReader),
    ("v0.6 NFO 未知 XML 往返保留", TestNfoRoundTripWriter),
    ("v0.6 本地与在线候选组合", TestLocalMetadataReviewComposition),
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

static Task TestStartupVideoRequestResolver()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.StartupRequestTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var noArguments = StartupVideoRequestResolver.Resolve([]);
        AssertEqual(StartupVideoRequestKind.None.ToString(), noArguments.Kind.ToString());

        var multipleArguments = StartupVideoRequestResolver.Resolve(["first.mp4", "second.mp4"]);
        AssertEqual(StartupVideoRequestKind.Invalid.ToString(), multipleArguments.Kind.ToString());
        AssertEqual("True", multipleArguments.ErrorMessage?.Contains("一次只能", StringComparison.Ordinal).ToString());

        var emptyArgument = StartupVideoRequestResolver.Resolve(["   "]);
        AssertEqual(StartupVideoRequestKind.Invalid.ToString(), emptyArgument.Kind.ToString());

        var directoryArgument = StartupVideoRequestResolver.Resolve([root]);
        AssertEqual(StartupVideoRequestKind.Invalid.ToString(), directoryArgument.Kind.ToString());
        AssertEqual("True", directoryArgument.ErrorMessage?.Contains("文件夹", StringComparison.Ordinal).ToString());

        var missingPath = Path.Combine(root, "不存在的影片.mp4");
        var missingArgument = StartupVideoRequestResolver.Resolve([missingPath]);
        AssertEqual(StartupVideoRequestKind.Invalid.ToString(), missingArgument.Kind.ToString());
        AssertEqual("True", missingArgument.ErrorMessage?.Contains("不存在", StringComparison.Ordinal).ToString());

        var unsupportedPath = Path.Combine(root, "SNOS-255.txt");
        File.WriteAllText(unsupportedPath, "not a movie");
        var unsupportedArgument = StartupVideoRequestResolver.Resolve([unsupportedPath]);
        AssertEqual(StartupVideoRequestKind.Invalid.ToString(), unsupportedArgument.Kind.ToString());
        AssertEqual("True", unsupportedArgument.ErrorMessage?.Contains("不支持", StringComparison.Ordinal).ToString());

        var videoPath = Path.Combine(root, "包含 空格", "SNOS-255.MKV");
        Directory.CreateDirectory(Path.GetDirectoryName(videoPath)!);
        File.WriteAllBytes(videoPath, [0x01, 0x02, 0x03]);
        var videoArgument = StartupVideoRequestResolver.Resolve([videoPath]);
        AssertEqual(StartupVideoRequestKind.Video.ToString(), videoArgument.Kind.ToString());
        AssertEqual(Path.GetFullPath(videoPath), videoArgument.VideoPath);
        AssertEqual("True", VideoFileSupport.IsSupportedExistingFile(videoArgument.VideoPath).ToString());
        AssertEqual("False", VideoFileSupport.HasSupportedExtension(unsupportedPath).ToString());
    }
    finally
    {
        Directory.Delete(root, true);
    }

    return Task.CompletedTask;
}

static Task TestAppPreferencesStore()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.PreferencesTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var store = new AppPreferencesStore(root);
        var missing = store.Load();
        AssertEqual("False", missing.Preferences.RememberSavePreferences.ToString());
        AssertEqual(OrganizationTargetMode.VideoDirectory.ToString(), missing.Preferences.TargetMode.ToString());
        AssertEqual("True", missing.Preferences.WriteNfo.ToString());
        AssertEqual("True", missing.Preferences.DownloadPoster.ToString());
        AssertEqual("True", missing.Preferences.DownloadFanart.ToString());
        AssertEqual("False", missing.Preferences.DownloadExtrafanart.ToString());

        var customRoot = Path.Combine(root, "library");
        var secondRoot = Path.Combine(root, "library-two");
        store.Save(new AppPreferences
        {
            RememberSavePreferences = true,
            TargetMode = OrganizationTargetMode.CustomRootNumberFolder,
            CustomRootDirectory = $"  {customRoot}  ",
            RecentCustomRootDirectories =
            [
                customRoot,
                secondRoot,
                Path.Combine(root, "library-three"),
                Path.Combine(root, "library-four"),
                Path.Combine(root, "library-five"),
                Path.Combine(root, "library-six"),
                "relative-path"
            ],
            RenameVideo = true,
            WriteNfo = false,
            DownloadPoster = false,
            DownloadFanart = true,
            DownloadExtrafanart = true
        });

        AssertEqual("True", File.Exists(store.SettingsPath).ToString());
        var json = File.ReadAllText(store.SettingsPath);
        AssertEqual("True", json.Contains("\"SchemaVersion\": 2", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"CustomRootNumberFolder\"", StringComparison.Ordinal).ToString());
        AssertEqual("False", json.Contains("DirectSave", StringComparison.OrdinalIgnoreCase).ToString());
        AssertEqual("0", Directory.EnumerateFiles(root, "*.tmp").Count().ToString());

        var loaded = store.Load();
        AssertEqual("True", loaded.Preferences.RememberSavePreferences.ToString());
        AssertEqual("True", loaded.CanOverwrite.ToString());
        AssertEqual(OrganizationTargetMode.CustomRootNumberFolder.ToString(), loaded.Preferences.TargetMode.ToString());
        AssertEqual(customRoot, loaded.Preferences.CustomRootDirectory);
        AssertEqual("5", loaded.Preferences.RecentCustomRootDirectories.Length.ToString());
        AssertEqual(customRoot, loaded.Preferences.RecentCustomRootDirectories[0]);
        AssertEqual(secondRoot, loaded.Preferences.RecentCustomRootDirectories[1]);
        AssertEqual("False", Directory.Exists(customRoot).ToString());
        AssertEqual("True", loaded.Preferences.RenameVideo.ToString());
        AssertEqual("False", loaded.Preferences.WriteNfo.ToString());
        AssertEqual("False", loaded.Preferences.DownloadPoster.ToString());
        AssertEqual("True", loaded.Preferences.DownloadFanart.ToString());
        AssertEqual("True", loaded.Preferences.DownloadExtrafanart.ToString());

        store.Save(new AppPreferences
        {
            RememberSavePreferences = true,
            TargetMode = OrganizationTargetMode.CustomRootNumberFolder,
            CustomRootDirectory = customRoot,
            RecentCustomRootDirectories = []
        });
        var clearedHistory = store.Load();
        AssertEqual(customRoot, clearedHistory.Preferences.CustomRootDirectory);
        AssertEqual("0", clearedHistory.Preferences.RecentCustomRootDirectories.Length.ToString());

        var v1Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            RememberSavePreferences = true,
            TargetMode = "CustomRootNumberFolder",
            CustomRootDirectory = secondRoot,
            RenameVideo = true,
            WriteNfo = true,
            DownloadPoster = true,
            DownloadFanart = true,
            DownloadExtrafanart = false
        });
        File.WriteAllText(store.SettingsPath, v1Json);
        var migrated = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migrated.Preferences.SchemaVersion.ToString());
        AssertEqual("True", migrated.CanOverwrite.ToString());
        AssertEqual(secondRoot, migrated.Preferences.CustomRootDirectory);
        AssertEqual("1", migrated.Preferences.RecentCustomRootDirectories.Length.ToString());
        AssertEqual(secondRoot, migrated.Preferences.RecentCustomRootDirectories[0]);

        File.WriteAllText(store.SettingsPath, "{ invalid json");
        var malformed = store.Load();
        AssertEqual("False", malformed.Preferences.RememberSavePreferences.ToString());
        AssertEqual("True", malformed.CanOverwrite.ToString());
        AssertEqual("True", (!string.IsNullOrWhiteSpace(malformed.Warning)).ToString());

        const string futureJson = """
            {
              "SchemaVersion": 99,
              "RememberSavePreferences": true,
              "TargetMode": "FutureLibraryProfile",
              "RenameVideo": true,
              "WriteNfo": false
            }
            """;
        File.WriteAllText(store.SettingsPath, futureJson);
        var future = store.Load();
        AssertEqual("False", future.Preferences.RememberSavePreferences.ToString());
        AssertEqual("False", future.CanOverwrite.ToString());
        AssertEqual("True", File.ReadAllText(store.SettingsPath).Contains("99", StringComparison.Ordinal).ToString());

        store.Clear();
        AssertEqual("False", File.Exists(store.SettingsPath).ToString());
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
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

static async Task TestMetadataSearchCoordinator()
{
    var logRoot = Path.Combine(Path.GetTempPath(), $"JavMetaLite.MultiSourceTests.{Guid.NewGuid():N}");
    try
    {
        AppLog.ConfigureDirectory(logRoot);
        var primaryMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "日文标题",
            Plot = "完整日文简介",
            Director = "日文导演",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var secondaryMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "English title",
            Series = "English series",
            SourceName = "r18dev",
            SourceDisplayName = "R18.dev"
        };

        using var primary = FakeMetadataProvider.Success("libredmm", "LibreDMM", primaryMetadata);
        using var secondary = FakeMetadataProvider.Success("r18dev", "R18.dev", secondaryMetadata);
        var complete = await MetadataSearchCoordinator.SearchAllAsync("IPZZ-850", primary, secondary);
        AssertEqual("1", primary.CallCount.ToString());
        AssertEqual("1", secondary.CallCount.ToString());
        AssertEqual("2", complete.Sources.Count.ToString());
        AssertEqual("2", complete.Attempts.Count.ToString());
        AssertEqual("日文标题", complete.Metadata.Title);
        AssertEqual("English series", complete.Metadata.Series);
        AssertEqual("True", complete.Attempts.All(attempt => attempt.Success).ToString());

        using var failedPrimary = FakeMetadataProvider.Failure(
            "libredmm",
            "LibreDMM",
            new HttpRequestException("primary unavailable"));
        using var survivingSecondary = FakeMetadataProvider.Success("r18dev", "R18.dev", secondaryMetadata);
        var secondaryOnly = await MetadataSearchCoordinator.SearchAllAsync(
            "IPZZ-850",
            failedPrimary,
            survivingSecondary);
        AssertEqual("R18.dev", secondaryOnly.Metadata.SourceDisplayName);
        AssertEqual("1", secondaryOnly.Sources.Count.ToString());
        AssertEqual("1", failedPrimary.CallCount.ToString());
        AssertEqual("1", survivingSecondary.CallCount.ToString());

        using var survivingPrimary = FakeMetadataProvider.Success("libredmm", "LibreDMM", primaryMetadata);
        using var failedSecondary = FakeMetadataProvider.Failure(
            "r18dev",
            "R18.dev",
            new MetadataNotFoundException("R18.dev", "IPZZ-850"));
        var primaryOnly = await MetadataSearchCoordinator.SearchAllAsync(
            "IPZZ-850",
            survivingPrimary,
            failedSecondary);
        AssertEqual("LibreDMM", primaryOnly.Metadata.SourceDisplayName);
        AssertEqual("1", primaryOnly.Sources.Count.ToString());

        using var firstFailure = FakeMetadataProvider.Failure(
            "libredmm",
            "LibreDMM",
            new HttpRequestException("first failed"));
        using var secondFailure = FakeMetadataProvider.Failure(
            "r18dev",
            "R18.dev",
            new HttpRequestException("second failed"));
        var bothFailed = false;
        try
        {
            await MetadataSearchCoordinator.SearchAllAsync("IPZZ-850", firstFailure, secondFailure);
        }
        catch (MultiSourceSearchException exception)
        {
            bothFailed = exception.Attempts.Count == 2 &&
                exception.Message.Contains("LibreDMM", StringComparison.Ordinal) &&
                exception.Message.Contains("R18.dev", StringComparison.Ordinal);
        }
        AssertEqual("True", bothFailed.ToString());

        using var mismatchedSecondary = FakeMetadataProvider.Success(
            "r18dev",
            "R18.dev",
            new MovieMetadata
            {
                Id = "START-237",
                Title = "Wrong movie",
                SourceName = "r18dev",
                SourceDisplayName = "R18.dev"
            });
        var mismatchBlocked = false;
        try
        {
            await MetadataSearchCoordinator.SearchAllAsync("IPZZ-850", primary, mismatchedSecondary);
        }
        catch (InvalidDataException)
        {
            mismatchBlocked = true;
        }
        AssertEqual("True", mismatchBlocked.ToString());

        using var singleProvider = FakeMetadataProvider.Success("libredmm", "LibreDMM", primaryMetadata);
        var singleAttempt = await MetadataSearchCoordinator.SearchSingleAsync("IPZZ-850", singleProvider);
        AssertEqual("True", singleAttempt.Success.ToString());
        AssertEqual("1", singleProvider.CallCount.ToString());

        var log = File.ReadAllText(AppLog.CurrentLogPath);
        AssertEqual("True", log.Contains("mode=multi source=libredmm", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("mode=multi source=r18dev", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("elapsedMs=", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("fields=", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("mode=single", StringComparison.Ordinal).ToString());
    }
    finally
    {
        AppLog.ConfigureDirectory(null);
        if (Directory.Exists(logRoot))
        {
            Directory.Delete(logRoot, true);
        }
    }
}

static Task TestMetadataReviewSession()
{
    var primary = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "日文标题",
        ReleaseDate = "2026-06-09",
        ActorsText = "演员甲",
        Actors = [new ActorMetadata("演员甲", "https://example.test/actor-ja.jpg")],
        Plot = "日文简介",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM",
        SourceUrl = "https://www.libredmm.com/movies/IPZZ-850"
    };
    var fallback = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "English title",
        ReleaseDate = "2026-06-09",
        Director = "Upstream director value",
        ActorsText = "Actor A",
        Actors = [new ActorMetadata("Actor A", "https://example.test/actor-en.jpg")],
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        SourceUrl = "https://r18.dev/test"
    };
    var merged = MetadataMerger.Merge(primary, fallback);

    using var review = MetadataReviewSession.Create(merged, primary, fallback);
    AssertEqual("2", review.Sources.Count.ToString());
    AssertEqual("2", review.GetCandidates(MetadataField.Title).Count.ToString());
    AssertEqual("2", review.GetCandidates(MetadataField.ReleaseDate).Count.ToString());
    AssertEqual("libredmm", review.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
    AssertEqual("r18dev", review.GetSelectedCandidate(MetadataField.Director)?.Source.Name);

    AssertEqual("True", review.SelectCandidate(MetadataField.Title, "r18dev").ToString());
    AssertEqual("English title", merged.Title);
    AssertEqual("r18dev", review.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
    AssertEqual(
        "0",
        review.GetCandidates(MetadataField.Title).Count(candidate => candidate.Source.IsManual).ToString());

    merged.Director = "手动修正导演";
    AssertEqual("manual", review.GetSelectedCandidate(MetadataField.Director)?.Source.Name);
    AssertEqual("手动修正导演", review.GetSelectedCandidate(MetadataField.Director)?.Value);
    review.SetManualValue(MetadataField.Director, "第二次修正");
    AssertEqual(
        "1",
        review.GetCandidates(MetadataField.Director).Count(candidate => candidate.Source.IsManual).ToString());
    AssertEqual("第二次修正", merged.Director);

    AssertEqual("True", review.SelectCandidate(MetadataField.Actors, "r18dev").ToString());
    AssertEqual("Actor A", merged.ActorsText);
    AssertEqual("1", merged.Actors.Count.ToString());
    AssertEqual("https://example.test/actor-en.jpg", merged.Actors[0].ImageUrl);

    primary.Title = "来源对象后来被修改";
    AssertEqual("日文标题", review.GetCandidates(MetadataField.Title)[0].Value);
    review.SetManualValue(MetadataField.Plot, string.Empty);
    AssertEqual("manual", review.GetSelectedCandidate(MetadataField.Plot)?.Source.Name);
    AssertEqual(string.Empty, merged.Plot);
    return Task.CompletedTask;
}

static Task TestArtworkCoverReviewSession()
{
    var primary = new MovieMetadata
    {
        Id = "IPZZ-850",
        CoverUrl = "https://images.example.test/libre-cover.jpg",
        FallbackCoverUrl = "https://images.example.test/libre-fallback.jpg",
        PosterUrl = "https://images.example.test/libre-poster.jpg",
        ScreenshotUrls = ["https://images.example.test/libre-sample.jpg"],
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    var fallback = new MovieMetadata
    {
        Id = "IPZZ-850",
        CoverUrl = "https://images.example.test/r18-cover.jpg",
        PosterUrl = "https://images.example.test/r18-poster.jpg",
        ScreenshotUrls = ["https://images.example.test/r18-sample.jpg"],
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev"
    };
    var merged = MetadataMerger.Merge(primary, fallback);
    var review = ArtworkCoverReviewSession.Create(merged, primary, fallback);

    AssertEqual("2", review.Candidates.Count.ToString());
    AssertEqual("libredmm", review.SelectedCandidate?.Source.Name);
    AssertEqual("True", review.SelectSource("r18dev").ToString());
    AssertEqual("r18dev", review.SelectedCandidate?.Source.Name);
    AssertEqual("https://images.example.test/r18-cover.jpg", merged.CoverUrl);
    AssertEqual("https://images.example.test/r18-poster.jpg", merged.PosterUrl);
    AssertEqual("https://images.example.test/libre-sample.jpg", merged.ScreenshotUrls[0]);

    fallback.CoverUrl = "https://images.example.test/changed-after-review.jpg";
    AssertEqual("https://images.example.test/r18-cover.jpg", review.SelectedCandidate?.CoverUrl);
    AssertEqual("False", review.SelectSource("missing").ToString());
    return Task.CompletedTask;
}

static Task TestLocalArtworkCoverReviewSession()
{
    var metadata = new MovieMetadata
    {
        Id = "SNOS-255",
        CoverUrl = "https://images.example.test/online-cover.jpg",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    var online = new MovieMetadata
    {
        Id = metadata.Id,
        CoverUrl = metadata.CoverUrl,
        SourceName = metadata.SourceName,
        SourceDisplayName = metadata.SourceDisplayName
    };
    var localPair = ArtworkCoverCandidate.CreateSidecarPair(
        new MetadataCandidateSource("local-images", "本地图片", "C:\\Movies\\SNOS-255"),
        "C:\\Movies\\SNOS-255\\SNOS-255-poster.jpg",
        null);
    var manual = ArtworkCoverCandidate.CreateCompleteCover(
        new MetadataCandidateSource("manual-cover", "手动封套", "C:\\Pictures\\cover.jpg"),
        "C:\\Pictures\\cover.jpg");
    var review = ArtworkCoverReviewSession.CreateWithAdditionalCandidates(
        metadata,
        [localPair, manual],
        "local-images",
        online);

    AssertEqual("3", review.Candidates.Count.ToString());
    AssertEqual("local-images", review.SelectedCandidate?.Source.Name);
    AssertEqual("True", review.SelectedCandidate?.HasPoster.ToString());
    AssertEqual("False", review.SelectedCandidate?.HasFanart.ToString());
    AssertEqual(string.Empty, metadata.CoverUrl);
    AssertEqual(string.Empty, metadata.PosterUrl);

    AssertEqual("True", review.SelectSource("manual-cover").ToString());
    AssertEqual(Path.GetFullPath("C:\\Pictures\\cover.jpg"), metadata.CoverUrl);
    AssertEqual("True", review.SelectedCandidate?.HasPoster.ToString());
    AssertEqual("True", review.SelectedCandidate?.HasFanart.ToString());

    AssertEqual("True", review.SelectSource("libredmm").ToString());
    AssertEqual("https://images.example.test/online-cover.jpg", metadata.CoverUrl);
    AssertEqual("True", review.SelectSource("local-images").ToString());
    AssertEqual(string.Empty, metadata.CoverUrl);
    AssertEqual("False", review.SelectSource("missing").ToString());
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

static async Task TestLocalCompleteCoverOutput()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.LocalCoverOutputTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "SNOS-255.mp4");
        var coverPath = Path.Combine(root, "manual-full-cover.jpg");
        var videoBytes = new byte[] { 0x53, 0x4E, 0x4F, 0x53, 0x02, 0x55 };
        var coverBytes = CreateJpeg(800, 538, 0x4B, 0x72, 0xA5);
        await File.WriteAllBytesAsync(videoPath, videoBytes);
        await File.WriteAllBytesAsync(coverPath, coverBytes);
        var metadata = new MovieMetadata
        {
            Id = "SNOS-255",
            Title = "本地封套输出测试",
            CoverUrl = coverPath,
            SourceName = "manual-cover",
            SourceDisplayName = "手动封套"
        };

        using var service = new OutputService();
        var result = await service.SaveAsync(
            videoPath,
            metadata,
            new JavMetaLite.Core.Models.SaveOptions(true, true, true, false, false));

        AssertEqual("True", File.Exists(result.NfoPath).ToString());
        AssertEqual("True", File.Exists(result.PosterPath).ToString());
        AssertEqual("True", File.Exists(result.FanartPath).ToString());
        AssertEqual("True", result.FanartUsedFullCover.ToString());
        var posterSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(result.PosterPath!));
        var fanartSize = PosterImageProcessor.GetDimensions(await File.ReadAllBytesAsync(result.FanartPath!));
        AssertEqual("400", posterSize.Width.ToString());
        AssertEqual("538", posterSize.Height.ToString());
        AssertEqual("800", fanartSize.Width.ToString());
        AssertEqual("538", fanartSize.Height.ToString());
        AssertEqual(Convert.ToHexString(videoBytes), Convert.ToHexString(await File.ReadAllBytesAsync(videoPath)));
        AssertEqual(Convert.ToHexString(coverBytes), Convert.ToHexString(await File.ReadAllBytesAsync(coverPath)));
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

static async Task TestLocalSidecarLocator()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.SidecarTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "SNOS-255.mp4");
        var nfoPath = Path.Combine(root, "SNOS-255.NFO");
        var posterPath = Path.Combine(root, "SNOS-255-POSTER.png");
        var fanartPath = Path.Combine(root, "SNOS-255-fanart.jpg");
        await File.WriteAllBytesAsync(videoPath, [0x01, 0x02, 0x03]);
        await File.WriteAllTextAsync(nfoPath, "<movie />");
        await File.WriteAllBytesAsync(posterPath, [0x04]);
        await File.WriteAllBytesAsync(fanartPath, [0x05]);
        await File.WriteAllTextAsync(Path.Combine(root, "unrelated.nfo"), "<movie />");

        var before = Directory.EnumerateFiles(root)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var sidecars = LocalSidecarLocator.Locate(videoPath);
        var after = Directory.EnumerateFiles(root)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        AssertEqual(Path.GetFullPath(videoPath), sidecars.VideoPath);
        AssertEqual(Path.GetFullPath(nfoPath), sidecars.NfoPath);
        AssertEqual(Path.GetFullPath(posterPath), sidecars.PosterPath);
        AssertEqual(Path.GetFullPath(fanartPath), sidecars.FanartPath);
        AssertEqual("True", sidecars.HasNfo.ToString());
        AssertEqual("True", sidecars.HasArtwork.ToString());
        AssertEqual(before.Count.ToString(), after.Count.ToString());
        foreach (var (path, bytes) in before)
        {
            AssertEqual(Convert.ToHexString(bytes), Convert.ToHexString(after[path]));
        }

        File.Delete(nfoPath);
        File.Delete(posterPath);
        File.Delete(fanartPath);
        var videoOnly = LocalSidecarLocator.Locate(videoPath);
        AssertEqual(null, videoOnly.NfoPath);
        AssertEqual(null, videoOnly.PosterPath);
        AssertEqual(null, videoOnly.FanartPath);
        AssertEqual("False", videoOnly.HasNfo.ToString());
        AssertEqual("False", videoOnly.HasArtwork.ToString());

        await AssertThrowsAsync<FileNotFoundException>(() =>
            Task.FromResult(LocalSidecarLocator.Locate(Path.Combine(root, "missing.mp4"))));
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static async Task TestLocalArtworkDiscovery()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.LocalArtworkTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "START-237.mp4");
        var posterPath = Path.Combine(root, "START-237-poster.jpg");
        var fanartPath = Path.Combine(root, "START-237-fanart.png");
        var videoBytes = new byte[] { 0x53, 0x54, 0x41, 0x52, 0x54, 0x02, 0x37 };
        await File.WriteAllBytesAsync(videoPath, videoBytes);
        await File.WriteAllBytesAsync(posterPath, CreateJpeg(420, 600, 0xA4, 0x5B, 0x39));
        await File.WriteAllBytesAsync(fanartPath, CreateJpeg(800, 538, 0x39, 0x5B, 0xA4));

        var complete = await LocalArtworkDiscovery.DiscoverAsync(
            new LocalSidecarPaths(videoPath, null, posterPath, fanartPath));
        AssertEqual("0", complete.Diagnostics.Count.ToString());
        AssertEqual("local-images", complete.Candidate?.Source.Name);
        AssertEqual("True", complete.Candidate?.IsSidecarPair.ToString());
        AssertEqual("True", complete.Candidate?.HasPoster.ToString());
        AssertEqual("True", complete.Candidate?.HasFanart.ToString());
        AssertEqual(Path.GetFullPath(posterPath), complete.Candidate?.LocalPosterPath);
        AssertEqual(Path.GetFullPath(fanartPath), complete.Candidate?.LocalFanartPath);

        await File.WriteAllBytesAsync(posterPath, [0x00, 0x01, 0x02]);
        var partial = await LocalArtworkDiscovery.DiscoverAsync(
            new LocalSidecarPaths(videoPath, null, posterPath, fanartPath));
        AssertEqual("1", partial.Diagnostics.Count.ToString());
        AssertEqual("True", partial.Diagnostics[0].Contains("poster", StringComparison.Ordinal).ToString());
        AssertEqual("False", partial.Candidate?.HasPoster.ToString());
        AssertEqual("True", partial.Candidate?.HasFanart.ToString());
        AssertEqual(string.Empty, partial.Candidate?.LocalPosterPath);

        await File.WriteAllBytesAsync(fanartPath, [0x03, 0x04, 0x05]);
        var invalid = await LocalArtworkDiscovery.DiscoverAsync(
            new LocalSidecarPaths(videoPath, null, posterPath, fanartPath));
        AssertEqual("True", (invalid.Candidate is null).ToString());
        AssertEqual("2", invalid.Diagnostics.Count.ToString());
        AssertEqual(Convert.ToHexString(videoBytes), Convert.ToHexString(await File.ReadAllBytesAsync(videoPath)));
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static async Task TestNfoReader()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.NfoReaderTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "SNOS-255.mp4");
        var nfoPath = Path.Combine(root, "SNOS-255.nfo");
        var posterPath = Path.Combine(root, "SNOS-255-poster.jpg");
        var fanartPath = Path.Combine(root, "SNOS-255-fanart.jpg");
        await File.WriteAllBytesAsync(videoPath, [0x10, 0x20, 0x30]);
        await File.WriteAllBytesAsync(posterPath, [0x40]);
        await File.WriteAllBytesAsync(fanartPath, [0x50]);
        const string validNfo = """
            <?xml version="1.0" encoding="utf-8"?>
            <!--keep-comment-->
            <movie custom="keep">
              <title>示例标题</title>
              <originaltitle>Original title</originaltitle>
              <id>SNOS-255</id>
              <uniqueid type="jav" default="true">snos00255</uniqueid>
              <premiered>2026-06-23</premiered>
              <releasedate>2026-06-22</releasedate>
              <runtime>120</runtime>
              <studio>S1</studio>
              <director>导演甲</director>
              <director>导演乙</director>
              <plot>完整简介</plot>
              <rating>4.5</rating>
              <genre>剧情</genre>
              <genre>4K</genre>
              <genre>剧情</genre>
              <actor><name>演员甲</name><thumb>https://example.test/a.jpg</thumb></actor>
              <actor><name>演员乙</name></actor>
              <actor><name>演员甲</name></actor>
              <tag>Label: S1 NO.1 STYLE</tag>
              <tag>Series: Example Series</tag>
              <tag>Custom tag</tag>
              <website>https://example.test/SNOS-255</website>
              <unknown answer="42"><child>nested</child></unknown>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, validNfo);

        var before = Directory.EnumerateFiles(root)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath));
        var after = Directory.EnumerateFiles(root)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        AssertEqual("SNOS-255", bundle.Metadata.Id);
        AssertEqual("snos00255", bundle.Metadata.ContentId);
        AssertEqual("示例标题", bundle.Metadata.Title);
        AssertEqual("Original title", bundle.Metadata.OriginalTitle);
        AssertEqual("2026-06-23", bundle.Metadata.ReleaseDate);
        AssertEqual("120", bundle.Metadata.RuntimeMinutes);
        AssertEqual("S1", bundle.Metadata.Maker);
        AssertEqual("导演甲, 导演乙", bundle.Metadata.Director);
        AssertEqual("完整简介", bundle.Metadata.Plot);
        AssertEqual("4.5", bundle.Metadata.Rating);
        AssertEqual("剧情, 4K", bundle.Metadata.GenresText);
        AssertEqual("演员甲, 演员乙", bundle.Metadata.ActorsText);
        AssertEqual("2", bundle.Metadata.Actors.Count.ToString());
        AssertEqual("https://example.test/a.jpg", bundle.Metadata.Actors[0].ImageUrl);
        AssertEqual("S1 NO.1 STYLE", bundle.Metadata.Label);
        AssertEqual("Example Series", bundle.Metadata.Series);
        AssertEqual("https://example.test/SNOS-255", bundle.Metadata.SourceUrl);
        AssertEqual("local-nfo", bundle.Metadata.SourceName);
        AssertEqual("本地 NFO", bundle.Metadata.SourceDisplayName);
        AssertEqual("True", bundle.HasUnknownXml.ToString());
        AssertEqual("示例标题", bundle.SourceSnapshot.GetValue(MetadataField.Title));
        AssertEqual("https://example.test/a.jpg", bundle.SourceSnapshot.Actors[0].ImageUrl);
        AssertEqual("0", bundle.Diagnostics.Count.ToString());
        AssertEqual(Path.GetFullPath(posterPath), bundle.Sidecars.PosterPath);
        AssertEqual(Path.GetFullPath(fanartPath), bundle.Sidecars.FanartPath);

        var firstClone = bundle.CloneOriginalDocument();
        AssertEqual("keep", firstClone.Root?.Attribute("custom")?.Value);
        AssertEqual("42", firstClone.Root?.Element("unknown")?.Attribute("answer")?.Value);
        AssertEqual("nested", firstClone.Root?.Element("unknown")?.Element("child")?.Value);
        AssertEqual("True", firstClone.DescendantNodes().OfType<XComment>().Any().ToString());
        firstClone.Root?.Element("unknown")?.Remove();
        var secondClone = bundle.CloneOriginalDocument();
        AssertEqual("42", secondClone.Root?.Element("unknown")?.Attribute("answer")?.Value);

        AssertEqual(before.Count.ToString(), after.Count.ToString());
        foreach (var (path, bytes) in before)
        {
            AssertEqual(Convert.ToHexString(bytes), Convert.ToHexString(after[path]));
        }

        await File.WriteAllTextAsync(nfoPath, """
            <movie>
              <title>标准 NFO</title>
              <id>SNOS-255</id>
              <uniqueid type="jav" default="true">snos00255</uniqueid>
              <premiered>2026-06-23</premiered>
              <actor><name>演员甲</name><thumb>https://example.test/a.jpg</thumb></actor>
              <tag>Label: S1</tag>
              <thumb aspect="poster">SNOS-255-poster.jpg</thumb>
              <fanart><thumb>SNOS-255-fanart.jpg</thumb></fanart>
            </movie>
            """);
        var standard = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath));
        AssertEqual("False", standard.HasUnknownXml.ToString());

        await File.WriteAllTextAsync(nfoPath, "<movie><title>Only title</title><custom /></movie>");
        var incomplete = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath));
        AssertEqual("Only title", incomplete.Metadata.Title);
        AssertEqual("1", incomplete.Diagnostics.Count.ToString());
        AssertEqual("True", incomplete.HasUnknownXml.ToString());

        await File.WriteAllTextAsync(nfoPath, "<tvshow><title>Wrong root</title></tvshow>");
        await AssertThrowsAsync<InvalidDataException>(() =>
            NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath)));

        await File.WriteAllTextAsync(nfoPath, "<movie><title>Broken</movie>");
        await AssertThrowsAsync<InvalidDataException>(() =>
            NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath)));

        var secretPath = Path.Combine(root, "secret.txt");
        await File.WriteAllTextAsync(secretPath, "MUST-NOT-BE-READ");
        var maliciousNfo = $"""
            <!DOCTYPE movie [<!ENTITY xxe SYSTEM "{new Uri(secretPath).AbsoluteUri}">]>
            <movie><title>&xxe;</title></movie>
            """;
        await File.WriteAllTextAsync(nfoPath, maliciousNfo);
        var securityFailure = await AssertThrowsAsync<InvalidDataException>(() =>
            NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath)));
        AssertEqual("False", securityFailure.Message.Contains("MUST-NOT-BE-READ", StringComparison.Ordinal).ToString());

        await File.WriteAllBytesAsync(nfoPath, new byte[NfoReader.MaximumNfoBytes + 1]);
        await AssertThrowsAsync<InvalidDataException>(() =>
            NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath)));
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static async Task TestNfoRoundTripWriter()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.NfoRoundTripTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "IPX-321.mp4");
        var nfoPath = Path.Combine(root, "IPX-321.nfo");
        var outputPath = Path.Combine(root, "updated.nfo");
        await File.WriteAllBytesAsync(videoPath, [0x49, 0x50, 0x58]);
        const string originalXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <!--keep-comment-->
            <movie custom="root-keep">
              <title origin="keep">旧标题</title>
              <id>IPX-321</id>
              <uniqueid type="jav" default="true">ipx00321</uniqueid>
              <uniqueid type="tmdb">999</uniqueid>
              <premiered>2024-01-02</premiered>
              <releasedate>2024-01-01</releasedate>
              <director rank="1">旧导演</director>
              <genre>剧情</genre>
              <actor custom="actor-keep">
                <name>演员甲</name>
                <thumb>https://example.test/old.jpg</thumb>
                <role>Lead</role>
              </actor>
              <tag>Label: 旧厂牌</tag>
              <tag custom="tag-keep">Custom tag</tag>
              <thumb aspect="landscape">landscape.jpg</thumb>
              <thumb aspect="poster" custom="poster-keep">old-poster.jpg</thumb>
              <fanart custom="fanart-keep"><thumb>old-fanart.jpg</thumb><other>keep</other></fanart>
              <unknown answer="42"><child>nested</child></unknown>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, originalXml);
        var originalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(nfoPath)));
        var bundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(videoPath));
        var editable = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata).Metadata;

        AssertEqual(
            "False",
            NfoRoundTripWriter.HasChanges(bundle, editable, false, null, false, null).ToString());

        editable.Title = "新标题";
        editable.ReleaseDate = "2025-02-03";
        editable.Director = "新导演, 第二导演";
        editable.GenresText = "剧情, 4K";
        editable.ActorsText = "演员甲, 演员乙";
        editable.Actors =
        [
            new ActorMetadata("演员甲", "https://example.test/new.jpg"),
            new ActorMetadata("演员乙", "https://example.test/b.jpg")
        ];
        editable.Label = string.Empty;
        editable.Series = "示例系列";
        editable.SourceUrl = "https://example.test/IPX-321";

        AssertEqual(
            "True",
            NfoRoundTripWriter.HasChanges(
                bundle,
                editable,
                true,
                "IPX-321-poster.jpg",
                true,
                "IPX-321-fanart.jpg").ToString());
        await NfoRoundTripWriter.WriteAsync(
            outputPath,
            bundle,
            editable,
            true,
            "IPX-321-poster.jpg",
            true,
            "IPX-321-fanart.jpg",
            false);

        var updated = XDocument.Load(outputPath, LoadOptions.PreserveWhitespace);
        var movie = updated.Root!;
        AssertEqual("root-keep", movie.Attribute("custom")?.Value);
        AssertEqual("True", updated.DescendantNodes().OfType<XComment>().Any().ToString());
        AssertEqual("42", movie.Element("unknown")?.Attribute("answer")?.Value);
        AssertEqual("nested", movie.Element("unknown")?.Element("child")?.Value);
        AssertEqual("新标题", movie.Element("title")?.Value);
        AssertEqual("keep", movie.Element("title")?.Attribute("origin")?.Value);
        AssertEqual("2025-02-03", movie.Element("premiered")?.Value);
        AssertEqual("2025-02-03", movie.Element("releasedate")?.Value);
        AssertEqual("2025", movie.Element("year")?.Value);
        AssertEqual("2", movie.Elements("director").Count().ToString());
        AssertEqual("1", movie.Elements("director").First().Attribute("rank")?.Value);
        AssertEqual("2", movie.Elements("genre").Count().ToString());
        AssertEqual("999", movie.Elements("uniqueid").First(element =>
            element.Attribute("type")?.Value == "tmdb").Value);
        AssertEqual("Custom tag", movie.Elements("tag").Single(element =>
            element.Attribute("custom")?.Value == "tag-keep").Value);
        AssertEqual("False", movie.Elements("tag").Any(element =>
            element.Value.StartsWith("Label:", StringComparison.OrdinalIgnoreCase)).ToString());
        AssertEqual("Series: 示例系列", movie.Elements("tag").Single(element =>
            element.Value.StartsWith("Series:", StringComparison.OrdinalIgnoreCase)).Value);
        var retainedActor = movie.Elements("actor").First(element => element.Element("name")?.Value == "演员甲");
        AssertEqual("actor-keep", retainedActor.Attribute("custom")?.Value);
        AssertEqual("Lead", retainedActor.Element("role")?.Value);
        AssertEqual("https://example.test/new.jpg", retainedActor.Element("thumb")?.Value);
        AssertEqual("landscape.jpg", movie.Elements("thumb").Single(element =>
            element.Attribute("aspect")?.Value == "landscape").Value);
        var poster = movie.Elements("thumb").Single(element => element.Attribute("aspect")?.Value == "poster");
        AssertEqual("poster-keep", poster.Attribute("custom")?.Value);
        AssertEqual("IPX-321-poster.jpg", poster.Value);
        var fanart = movie.Element("fanart")!;
        AssertEqual("fanart-keep", fanart.Attribute("custom")?.Value);
        AssertEqual("keep", fanart.Element("other")?.Value);
        AssertEqual("IPX-321-fanart.jpg", fanart.Element("thumb")?.Value);
        AssertEqual("https://example.test/IPX-321", movie.Element("website")?.Value);
        AssertEqual(
            originalHash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(nfoPath))));
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static Task TestLocalMetadataReviewComposition()
{
    var local = new MovieMetadata
    {
        Id = "SNOS-255",
        Title = "本地标题",
        ReleaseDate = "2026-06-22",
        Series = "本地独有系列",
        ActorsText = "本地演员",
        Actors = [new ActorMetadata("本地演员", "https://local.example/actor.jpg")],
        SourceName = "local-nfo",
        SourceDisplayName = "本地 NFO"
    };
    var libre = new MovieMetadata
    {
        Id = "SNOS-255",
        Title = "在线日文标题",
        ReleaseDate = "2026-06-22",
        Director = "在线导演",
        Plot = "在线简介",
        ActorsText = "在线演员",
        Actors = [new ActorMetadata("在线演员", "https://libre.example/actor.jpg")],
        CoverUrl = "https://libre.example/cover.jpg",
        ScreenshotUrls = ["https://libre.example/scene.jpg"],
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    var r18 = new MovieMetadata
    {
        Id = "SNOS-255",
        Title = "Online English title",
        Director = "Online director",
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev"
    };

    var onlinePreferred = MetadataMerger.Merge(libre, r18);
    var composition = LocalMetadataReviewComposer.ComposeWithOnline(
        local,
        onlinePreferred,
        [libre, r18]);
    AssertEqual("在线日文标题", composition.Metadata.Title);
    AssertEqual("在线导演", composition.Metadata.Director);
    AssertEqual("在线简介", composition.Metadata.Plot);
    AssertEqual("在线演员", composition.Metadata.ActorsText);
    AssertEqual("1", composition.Metadata.Actors.Count.ToString());
    AssertEqual("在线演员", composition.Metadata.Actors[0].Name);
    AssertEqual("https://libre.example/actor.jpg", composition.Metadata.Actors[0].ImageUrl);
    AssertEqual("本地独有系列", composition.Metadata.Series);
    AssertEqual("https://libre.example/cover.jpg", composition.Metadata.CoverUrl);
    AssertEqual("1", composition.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual("3", composition.Sources.Count.ToString());

    local.Title = "修改外部本地对象";
    libre.Title = "修改外部在线对象";
    using (var review = MetadataReviewSession.Create(
        composition.Metadata,
        composition.Sources.ToArray()))
    {
        AssertEqual("3", review.GetCandidates(MetadataField.Title).Count.ToString());
        AssertEqual("libredmm", review.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
        AssertEqual("libredmm", review.GetSelectedCandidate(MetadataField.ReleaseDate)?.Source.Name);
        AssertEqual("libredmm", review.GetSelectedCandidate(MetadataField.Director)?.Source.Name);
        AssertEqual("local-nfo", review.GetSelectedCandidate(MetadataField.Series)?.Source.Name);
        AssertEqual(
            "本地标题",
            review.GetCandidates(MetadataField.Title)
                .Single(candidate => candidate.Source.Name == "local-nfo").Value);

        review.SetManualValue(MetadataField.Title, "手动修正");
        AssertEqual("manual", review.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
        AssertEqual("True", review.SelectCandidate(MetadataField.Title, "libredmm").ToString());
        AssertEqual("在线日文标题", composition.Metadata.Title);
        AssertEqual("True", review.SelectCandidate(MetadataField.Title, "manual").ToString());
        AssertEqual("手动修正", composition.Metadata.Title);

        AssertEqual("True", review.SelectCandidate(MetadataField.Actors, "libredmm").ToString());
        AssertEqual("在线演员", composition.Metadata.ActorsText);
        AssertEqual("在线演员", composition.Metadata.Actors[0].Name);
        AssertEqual("https://libre.example/actor.jpg", composition.Metadata.Actors[0].ImageUrl);
    }

    var refreshedLibre = new MovieMetadata
    {
        Id = "SNOS-255",
        Title = "刷新后的在线标题",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    var refreshed = LocalMetadataReviewComposer.ComposeWithOnline(local, refreshedLibre, [refreshedLibre]);
    using var refreshedReview = MetadataReviewSession.Create(refreshed.Metadata, refreshed.Sources.ToArray());
    AssertEqual(
        "False",
        refreshedReview.GetCandidates(MetadataField.Title)
            .Any(candidate => candidate.Value == "在线日文标题" || candidate.Value == "手动修正")
            .ToString());

    return Task.CompletedTask;
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

static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
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

internal sealed class FakeMetadataProvider(
    string name,
    string displayName,
    Func<string, CancellationToken, Task<MovieMetadata>> search) : IMetadataProvider
{
    public string Name { get; } = name;

    public string DisplayName { get; } = displayName;

    public int CallCount { get; private set; }

    public static FakeMetadataProvider Success(string name, string displayName, MovieMetadata metadata) =>
        new(name, displayName, (_, _) => Task.FromResult(metadata));

    public static FakeMetadataProvider Failure(string name, string displayName, Exception exception) =>
        new(name, displayName, (_, _) => Task.FromException<MovieMetadata>(exception));

    public Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return search(rawId, cancellationToken);
    }

    public void Dispose()
    {
    }
}
