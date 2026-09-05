using System.IO;
using System.Diagnostics;
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
        new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, true, true));
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
        new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, true, true));
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
    ("Preview 6 CD 分段发现与队列分组", TestMovieFileSet),
    ("v0.8 启动影片参数解析", TestStartupVideoRequestResolver),
    ("v1.1 影片文件与番号文件夹输入解析", TestVideoInputPathResolver),
    ("Preview 13 统一文件与文件夹发现", TestMovieFileDiscovery),
    ("v1.1 四语言、跨盘校验与保存偏好原子存储", TestAppPreferencesStore),
    ("LibreDMM JSON 解析与清理", TestLibreDmmParser),
    ("多来源字段补全", TestMetadataMerge),
    ("Preview 21 R18.dev 手动查询真实 ID 解析与页面状态", TestBrowserImportRouting),
    ("v0.5 多来源搜索编排", TestMetadataSearchCoordinator),
    ("失败来源重试保留当前审核选择", TestRetryPreservesReviewSelections),
    ("Phase 3 受控批量搜索", TestBatchMetadataSearchCoordinator),
    ("Preview 46 默认DMM与旧来源迁移", BatchSourceRoutingTests.DefaultSource),
    ("Preview 46 71片来源选择矩阵", BatchSourceRoutingTests.SourceMatrix),
    ("Preview 45 单来源失败与定向重试", BatchSourceRoutingTests.SingleSourceFailureAndRetry),
    ("Preview 45 重试来源范围与JAVLibrary手动资料保留", BatchSourceRoutingTests.ScopedRetryAndManualImport),
    ("Preview 45 单来源批量取消", BatchSourceRoutingTests.SingleSourceCancellation),
    ("Preview 2 选择、revision 与顺序保存", TestReviewRevisionAndBatchSave),
    ("Preview 11 自定义字段来源与候选追踪", TestMetadataReviewSession),
    ("Preview 21 手动网页资料优先采用并保留候选", TestBrowserCandidateImport),
    ("Phase 1 单影片工作上下文隔离与重置", TestMovieJobIsolationAndReset),
    ("v0.5 poster 与 fanart 统一封套来源", TestArtworkCoverReviewSession),
    ("Preview 41 按分辨率选择整套图片来源", TestArtworkResolutionSelection),
    ("v0.6 本地与手动封套统一候选", TestLocalArtworkCoverReviewSession),
    ("R18.dev JSON 解析", TestR18Parser),
    ("Preview 17 R18.dev 正常网页后台导入", TestR18BrowserImport),
    ("R18.dev 实际 content_id 回退", TestR18ContentIdFallback),
    ("Preview 44 R18 71片调度与429恢复", R18RateLimitTests.LargeQueueScheduling),
    ("Preview 44 R18 429分类、共享冷却与重试预算", R18RateLimitTests.RateLimitClassificationAndBudget),
    ("Preview 44 71片部分失败、定向重试与审核保留", R18RateLimitTests.PartialQueueRetryAndReview),
    ("Preview 44 R18等待、网络超时与取消", R18RateLimitTests.WaitingTimeoutAndCancellation),
    ("Preview 44 重试取消保留可用来源", R18RateLimitTests.PartialRetryCancellation),
    ("JAVLibrary HTML 解析", TestHtmlParser),
    ("高清海报自动裁切", TestPosterCropping),
    ("NFO 生成", TestNfoWriter),
    ("完整封套 fanart 与 Sample Images 输出", TestArtworkOutput),
    ("v1.1 无 Sample Images 时自动跳过", TestMissingSampleImagesAreSkipped),
    ("v0.6 本地完整封套输出", TestLocalCompleteCoverOutput),
    ("v0.4 文件整理计划与安全执行", TestFileOrganization),
    ("Preview 6 多 CD 共用搜刮输出与整理", TestMultipartFileOrganization),
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
    AssertEqual("IPX-123", MovieIdParser.TryExtract("IPX-123-CD2.mp4"));
    AssertEqual("SSIS-001", MovieIdParser.TryExtract("ssis001_uncensored.mkv"));
    AssertEqual("FC2-PPV-1234567", MovieIdParser.TryExtract("FC2-PPV-1234567.mp4"));
    AssertEqual(null, MovieIdParser.TryExtract("vacation-1080p.mp4"));
    return Task.CompletedTask;
}

static async Task TestMovieFileSet()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.MultipartDiscovery.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var cd1 = Path.Combine(root, "IPX-123-CD1.mp4");
        var cd2 = Path.Combine(root, "IPX-123_cd2.mkv");
        var other = Path.Combine(root, "SSIS-001.mp4");
        await File.WriteAllBytesAsync(cd1, [0x01]);
        await File.WriteAllBytesAsync(cd2, [0x02]);
        await File.WriteAllBytesAsync(other, [0x03]);
        await File.WriteAllTextAsync(
            Path.Combine(root, "IPX-123.nfo"),
            "<movie><id>IPX-123</id><title>Shared metadata</title><tag>Series: Legacy</tag></movie>");

        var discovered = MovieFileSet.Discover(cd2);
        AssertEqual("2", discovered.VideoPaths.Count.ToString());
        AssertEqual(Path.GetFullPath(cd1), discovered.VideoPaths[0]);
        AssertEqual(Path.GetFullPath(cd2), discovered.VideoPaths[1]);
        AssertEqual("IPX-123", discovered.MovieBaseName);

        var groups = MovieFileSet.Group([cd2, other]);
        AssertEqual("2", groups.Count.ToString());
        AssertEqual("True", groups.Any(group => group.VideoPaths.Count == 2).ToString());

        using var job = new MovieJob();
        var load = await MovieJobLoader.LoadAsync(job, cd2);
        AssertEqual(LocalMetadataLoadStatus.Loaded.ToString(), load.MetadataStatus.ToString());
        AssertEqual("2", job.VideoPartCount.ToString());
        AssertEqual("IPX-123", job.Metadata.Id);
        AssertEqual(string.Empty, job.Metadata.Series);
        AssertEqual("True", job.LocalMetadataBundle?.HasUnknownXml.ToString());
        AssertEqual(Path.Combine(root, "IPX-123.nfo"), job.LocalMetadataBundle?.Sidecars.NfoPath);
    }
    finally
    {
        Directory.Delete(root, true);
    }
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

static Task TestVideoInputPathResolver()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.VideoInput.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var directVideo = Path.Combine(root, "IPX-123.mp4");
        File.WriteAllBytes(directVideo, [0x01]);
        var direct = VideoFileSupport.ResolveInputPath(directVideo);
        AssertEqual(VideoInputPathStatus.Success.ToString(), direct.Status.ToString());
        AssertEqual(Path.GetFullPath(directVideo), direct.VideoPath);

        var numberFolder = Path.Combine(root, "SONE-855");
        Directory.CreateDirectory(numberFolder);
        var folderVideo = Path.Combine(numberFolder, "SONE-855.mkv");
        File.WriteAllBytes(folderVideo, [0x02]);
        File.WriteAllText(Path.Combine(numberFolder, "SONE-855.nfo"), "<movie />");
        var folder = VideoFileSupport.ResolveInputPath(numberFolder);
        AssertEqual(VideoInputPathStatus.Success.ToString(), folder.Status.ToString());
        AssertEqual(Path.GetFullPath(folderVideo), folder.VideoPath);

        var emptyFolder = Path.Combine(root, "FNS-121");
        Directory.CreateDirectory(emptyFolder);
        Directory.CreateDirectory(Path.Combine(emptyFolder, "nested"));
        File.WriteAllBytes(Path.Combine(emptyFolder, "nested", "FNS-121.mp4"), [0x03]);
        AssertEqual(
            VideoInputPathStatus.FolderHasNoVideo.ToString(),
            VideoFileSupport.ResolveInputPath(emptyFolder).Status.ToString());

        File.WriteAllBytes(Path.Combine(numberFolder, "extra.avi"), [0x04]);
        AssertEqual(
            VideoInputPathStatus.FolderHasMultipleVideos.ToString(),
            VideoFileSupport.ResolveInputPath(numberFolder).Status.ToString());
        AssertEqual(
            VideoInputPathStatus.UnsupportedPath.ToString(),
            VideoFileSupport.ResolveInputPath(Path.Combine(root, "missing")).Status.ToString());
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task TestAppPreferencesStore()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.PreferencesTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var store = new AppPreferencesStore(root);
        var missing = store.Load();
        AssertEqual(UiLanguageCodes.System, missing.Preferences.UiLanguage);
        AssertEqual("False", missing.Preferences.RememberSavePreferences.ToString());
        AssertEqual(MetadataSearchSourceModes.LibreDmm, missing.Preferences.SearchSourceMode);
        AssertEqual("4", MetadataSearchSourceModes.Supported.Count.ToString());
        AssertEqual(MetadataSearchSourceModes.Manual, MetadataSearchSourceModes.Normalize(" JAVLIBRARY "));
        AssertEqual("False", missing.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", missing.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", missing.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), missing.Preferences.CrossVolumeVerification.ToString());
        AssertEqual(OrganizationTargetMode.VideoDirectory.ToString(), missing.Preferences.TargetMode.ToString());
        AssertEqual("True", missing.Preferences.WriteNfo.ToString());
        AssertEqual("True", missing.Preferences.IncludeIdInTitle.ToString());
        AssertEqual("True", missing.Preferences.DownloadPoster.ToString());
        AssertEqual("True", missing.Preferences.DownloadFanart.ToString());
        AssertEqual("False", missing.Preferences.DownloadExtrafanart.ToString());
        AssertEqual("False", missing.Preferences.ReplaceLocalExtrafanart.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, missing.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, missing.Preferences.CustomSourceProfile.PlotSource);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, missing.Preferences.CustomSourceProfile.ArtworkSource);

        var customRoot = Path.Combine(root, "library");
        var secondRoot = Path.Combine(root, "library-two");
        store.Save(new AppPreferences
        {
            UiLanguage = UiLanguageCodes.Japanese,
            RememberSavePreferences = true,
            SearchSourceMode = MetadataSearchSourceModes.R18Dev,
            SkipSavePreview = true,
            CrossVolumeVerification = CrossVolumeVerificationMode.FileSizeOnly,
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
            IncludeIdInTitle = false,
            DownloadPoster = false,
            DownloadFanart = true,
            DownloadExtrafanart = true,
            ReplaceLocalExtrafanart = true,
            CustomSourceProfile = new MetadataSourcePreferenceProfile
            {
                TitleSource = MetadataSourcePreferenceProfile.LibreDmm,
                PlotSource = MetadataSourcePreferenceProfile.R18Dev,
                ArtworkSource = MetadataSourcePreferenceProfile.BestArtworkResolution
            }
        });

        AssertEqual("True", File.Exists(store.SettingsPath).ToString());
        var json = File.ReadAllText(store.SettingsPath);
        AssertEqual("True", json.Contains("\"SchemaVersion\": 12", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"UiLanguage\": \"ja\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"SearchSourceMode\": \"r18dev\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"CustomRootNumberFolder\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"SkipSavePreview\": true", StringComparison.Ordinal).ToString());
        AssertEqual("False", json.Contains("\"DirectSaveOverwrite\"", StringComparison.Ordinal).ToString());
        AssertEqual("False", json.Contains("\"SkipBatchSavePreview\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"IncludeIdInTitle\": false", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"ReplaceLocalExtrafanart\": true", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"TitleSource\": \"libredmm\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"PlotSource\": \"r18dev\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"ArtworkSource\": \"best-artwork-resolution\"", StringComparison.Ordinal).ToString());
        AssertEqual("True", json.Contains("\"CrossVolumeVerification\": \"FileSizeOnly\"", StringComparison.Ordinal).ToString());
        AssertEqual("0", Directory.EnumerateFiles(root, "*.tmp").Count().ToString());

        var loaded = store.Load();
        AssertEqual("True", loaded.Preferences.RememberSavePreferences.ToString());
        AssertEqual(UiLanguageCodes.Japanese, loaded.Preferences.UiLanguage);
        AssertEqual(MetadataSearchSourceModes.R18Dev, loaded.Preferences.SearchSourceMode);
        AssertEqual("True", loaded.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", loaded.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", loaded.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual(CrossVolumeVerificationMode.FileSizeOnly.ToString(), loaded.Preferences.CrossVolumeVerification.ToString());
        AssertEqual("True", loaded.CanOverwrite.ToString());
        AssertEqual(OrganizationTargetMode.CustomRootNumberFolder.ToString(), loaded.Preferences.TargetMode.ToString());
        AssertEqual(customRoot, loaded.Preferences.CustomRootDirectory);
        AssertEqual("5", loaded.Preferences.RecentCustomRootDirectories.Length.ToString());
        AssertEqual(customRoot, loaded.Preferences.RecentCustomRootDirectories[0]);
        AssertEqual(secondRoot, loaded.Preferences.RecentCustomRootDirectories[1]);
        AssertEqual("False", Directory.Exists(customRoot).ToString());
        AssertEqual("True", loaded.Preferences.RenameVideo.ToString());
        AssertEqual("False", loaded.Preferences.WriteNfo.ToString());
        AssertEqual("False", loaded.Preferences.IncludeIdInTitle.ToString());
        AssertEqual("False", loaded.Preferences.DownloadPoster.ToString());
        AssertEqual("True", loaded.Preferences.DownloadFanart.ToString());
        AssertEqual("True", loaded.Preferences.DownloadExtrafanart.ToString());
        AssertEqual("True", loaded.Preferences.ReplaceLocalExtrafanart.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, loaded.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, loaded.Preferences.CustomSourceProfile.PlotSource);
        AssertEqual(MetadataSourcePreferenceProfile.BestArtworkResolution, loaded.Preferences.CustomSourceProfile.ArtworkSource);

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
        AssertEqual("False", migrated.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migrated.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", migrated.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), migrated.Preferences.CrossVolumeVerification.ToString());
        AssertEqual(UiLanguageCodes.SimplifiedChinese, migrated.Preferences.UiLanguage);

        var v2Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 2,
            RememberSavePreferences = true,
            TargetMode = "CustomRootNumberFolder",
            CustomRootDirectory = customRoot,
            RecentCustomRootDirectories = new[] { customRoot },
            RenameVideo = true,
            WriteNfo = true,
            DownloadPoster = true,
            DownloadFanart = true,
            DownloadExtrafanart = false
        });
        File.WriteAllText(store.SettingsPath, v2Json);
        var migratedV2 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedV2.Preferences.SchemaVersion.ToString());
        AssertEqual("True", migratedV2.CanOverwrite.ToString());
        AssertEqual("False", migratedV2.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migratedV2.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", migratedV2.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), migratedV2.Preferences.CrossVolumeVerification.ToString());
        AssertEqual(UiLanguageCodes.SimplifiedChinese, migratedV2.Preferences.UiLanguage);

        var v3Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 3,
            RememberSavePreferences = true,
            DirectSaveOverwrite = true,
            TargetMode = "VideoDirectory"
        });
        File.WriteAllText(store.SettingsPath, v3Json);
        var migratedV3 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedV3.Preferences.SchemaVersion.ToString());
        AssertEqual(UiLanguageCodes.SimplifiedChinese, migratedV3.Preferences.UiLanguage);
        AssertEqual("True", migratedV3.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migratedV3.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", migratedV3.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), migratedV3.Preferences.CrossVolumeVerification.ToString());

        var v4Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 4,
            UiLanguage = "en",
            RememberSavePreferences = true,
            DirectSaveOverwrite = false,
            TargetMode = "CustomRootNumberFolder",
            CustomRootDirectory = customRoot
        });
        File.WriteAllText(store.SettingsPath, v4Json);
        var migratedV4 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedV4.Preferences.SchemaVersion.ToString());
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), migratedV4.Preferences.CrossVolumeVerification.ToString());
        AssertEqual("False", migratedV4.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migratedV4.Preferences.SkipBatchSavePreview.ToString());

        var v5Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 5,
            UiLanguage = "en",
            RememberSavePreferences = true,
            DirectSaveOverwrite = true,
            TargetMode = "VideoDirectory"
        });
        File.WriteAllText(store.SettingsPath, v5Json);
        var migratedV5 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedV5.Preferences.SchemaVersion.ToString());
        AssertEqual("True", migratedV5.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migratedV5.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual("True", migratedV5.Preferences.IncludeIdInTitle.ToString());

        var v6Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 6,
            UiLanguage = "en",
            RememberSavePreferences = true,
            DirectSaveOverwrite = true,
            SkipBatchSavePreview = true,
            TargetMode = "VideoDirectory"
        });
        File.WriteAllText(store.SettingsPath, v6Json);
        var migratedV6 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedV6.Preferences.SchemaVersion.ToString());
        AssertEqual("True", migratedV6.Preferences.SkipSavePreview.ToString());
        AssertEqual("True", migratedV6.Preferences.IncludeIdInTitle.ToString());

        var v7Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 7,
            UiLanguage = "en",
            RememberSavePreferences = true,
            IncludeIdInTitle = false,
            TargetMode = "VideoDirectory"
        });
        File.WriteAllText(store.SettingsPath, v7Json);
        var migratedV7 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedV7.Preferences.SchemaVersion.ToString());
        AssertEqual("False", migratedV7.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migratedV7.Preferences.IncludeIdInTitle.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, migratedV7.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, migratedV7.Preferences.CustomSourceProfile.PlotSource);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, migratedV7.Preferences.CustomSourceProfile.ArtworkSource);

        var preview11V8Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 8,
            UiLanguage = "en",
            RememberSavePreferences = true,
            CustomSourceProfile = new
            {
                TitleSource = MetadataSourcePreferenceProfile.R18Dev
            }
        });
        File.WriteAllText(store.SettingsPath, preview11V8Json);
        var migratedPreview11V8 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedPreview11V8.Preferences.SchemaVersion.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, migratedPreview11V8.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, migratedPreview11V8.Preferences.CustomSourceProfile.ArtworkSource);

        var customizedV8Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 8,
            UiLanguage = "en",
            RememberSavePreferences = true,
            CustomSourceProfile = new
            {
                TitleSource = MetadataSourcePreferenceProfile.R18Dev,
                PlotSource = MetadataSourcePreferenceProfile.R18Dev
            }
        });
        File.WriteAllText(store.SettingsPath, customizedV8Json);
        var migratedCustomizedV8 = store.Load();
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, migratedCustomizedV8.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, migratedCustomizedV8.Preferences.CustomSourceProfile.PlotSource);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, migratedCustomizedV8.Preferences.CustomSourceProfile.ArtworkSource);

        var preview13V9Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 9,
            UiLanguage = "en",
            RememberSavePreferences = true,
            DirectSaveOverwrite = false,
            SkipBatchSavePreview = true
        });
        File.WriteAllText(store.SettingsPath, preview13V9Json);
        var migratedPreview13V9 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedPreview13V9.Preferences.SchemaVersion.ToString());
        AssertEqual("True", migratedPreview13V9.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", migratedPreview13V9.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", migratedPreview13V9.Preferences.SkipBatchSavePreview.ToString());

        var preview21V10Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 10,
            UiLanguage = "en",
            RememberSavePreferences = true,
            TargetMode = "VideoDirectory"
        });
        File.WriteAllText(store.SettingsPath, preview21V10Json);
        var migratedPreview21V10 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedPreview21V10.Preferences.SchemaVersion.ToString());
        AssertEqual(MetadataSearchSourceModes.LibreDmm, migratedPreview21V10.Preferences.SearchSourceMode);
        AssertEqual("False", migratedPreview21V10.Preferences.ReplaceLocalExtrafanart.ToString());

        var preview31V11Json = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 11,
            UiLanguage = "en",
            RememberSavePreferences = true,
            DownloadExtrafanart = true,
            ReplaceLocalExtrafanart = true
        });
        File.WriteAllText(store.SettingsPath, preview31V11Json);
        var migratedPreview31V11 = store.Load();
        AssertEqual(AppPreferences.CurrentSchemaVersion.ToString(), migratedPreview31V11.Preferences.SchemaVersion.ToString());
        AssertEqual("True", migratedPreview31V11.Preferences.DownloadExtrafanart.ToString());
        AssertEqual("False", migratedPreview31V11.Preferences.ReplaceLocalExtrafanart.ToString());

        foreach (var sourceMode in MetadataSearchSourceModes.Supported)
        {
            store.Save(new AppPreferences
            {
                RememberSavePreferences = true,
                SearchSourceMode = sourceMode
            });
            AssertEqual(sourceMode, store.Load().Preferences.SearchSourceMode);
        }

        store.Save(new AppPreferences
        {
            RememberSavePreferences = true,
            SearchSourceMode = "unsupported-source"
        });
        AssertEqual(MetadataSearchSourceModes.LibreDmm, store.Load().Preferences.SearchSourceMode);

        File.WriteAllText(store.SettingsPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = AppPreferences.CurrentSchemaVersion,
            RememberSavePreferences = true,
            SearchSourceMode = "javlibrary"
        }));
        AssertEqual(MetadataSearchSourceModes.Manual, store.Load().Preferences.SearchSourceMode);

        foreach (var legacySource in new[] { "auto", " AUTO " })
        {
            File.WriteAllText(store.SettingsPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                SchemaVersion = AppPreferences.CurrentSchemaVersion,
                RememberSavePreferences = true,
                SearchSourceMode = legacySource,
                CustomSourceProfile = new MetadataSourcePreferenceProfile { TitleSource = "r18dev" }
            }));
            AssertEqual(MetadataSearchSourceModes.LibreDmm, store.Load().Preferences.SearchSourceMode);
            AssertEqual("r18dev", store.Load().Preferences.CustomSourceProfile.TitleSource);
        }

        File.WriteAllText(store.SettingsPath, "{ invalid json");
        var malformed = store.Load();
        AssertEqual("False", malformed.Preferences.RememberSavePreferences.ToString());
        AssertEqual("False", malformed.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", malformed.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", malformed.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual("True", malformed.Preferences.IncludeIdInTitle.ToString());
        AssertEqual("False", malformed.Preferences.ReplaceLocalExtrafanart.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, malformed.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), malformed.Preferences.CrossVolumeVerification.ToString());
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
        AssertEqual("False", future.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", future.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", future.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual("True", future.Preferences.IncludeIdInTitle.ToString());
        AssertEqual("False", future.Preferences.ReplaceLocalExtrafanart.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, future.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), future.Preferences.CrossVolumeVerification.ToString());
        AssertEqual("False", future.CanOverwrite.ToString());
        AssertEqual("True", File.ReadAllText(store.SettingsPath).Contains("99", StringComparison.Ordinal).ToString());

        store.Save(new AppPreferences
        {
            UiLanguage = UiLanguageCodes.English,
            RememberSavePreferences = false,
            SearchSourceMode = MetadataSearchSourceModes.Manual,
            SkipSavePreview = true,
            CrossVolumeVerification = CrossVolumeVerificationMode.FileSizeOnly,
            CustomSourceProfile = new MetadataSourcePreferenceProfile
            {
                TitleSource = MetadataSourcePreferenceProfile.LibreDmm,
                PlotSource = MetadataSourcePreferenceProfile.R18Dev,
                ArtworkSource = MetadataSourcePreferenceProfile.R18Dev
            }
        });
        AssertEqual("True", File.Exists(store.SettingsPath).ToString());
        var disabledMemory = store.Load();
        AssertEqual(UiLanguageCodes.English, disabledMemory.Preferences.UiLanguage);
        AssertEqual("False", disabledMemory.Preferences.RememberSavePreferences.ToString());
        AssertEqual(MetadataSearchSourceModes.LibreDmm, disabledMemory.Preferences.SearchSourceMode);
        AssertEqual("False", disabledMemory.Preferences.SkipSavePreview.ToString());
        AssertEqual("False", disabledMemory.Preferences.DirectSaveOverwrite.ToString());
        AssertEqual("False", disabledMemory.Preferences.SkipBatchSavePreview.ToString());
        AssertEqual("True", disabledMemory.Preferences.IncludeIdInTitle.ToString());
        AssertEqual("False", disabledMemory.Preferences.ReplaceLocalExtrafanart.ToString());
        AssertEqual(CrossVolumeVerificationMode.FullSha256.ToString(), disabledMemory.Preferences.CrossVolumeVerification.ToString());
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, disabledMemory.Preferences.CustomSourceProfile.TitleSource);
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, disabledMemory.Preferences.CustomSourceProfile.PlotSource);
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, disabledMemory.Preferences.CustomSourceProfile.ArtworkSource);
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
          <div id="video_series"><span class="text"><a>不应搜刮的系列</a></span></div>
          <div id="video_review"><span class="text"><span class="score">(8.90)</span></span></div>
          <div id="video_rating"><span class="text"><span class="score">(6.00)</span></span></div>
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
    AssertEqual("8.9", result.Rating);
    AssertEqual("JAVLibrary", result.SourceDisplayName);
    AssertEqual(string.Empty, result.Series);
    AssertEqual("https://img.example.test/ipx123ps.jpg", result.PosterUrl);

    const string legacyRatingHtml = """
        <html><head><title>IPX-124 旧评分结构 - JAVLibrary</title></head>
        <body><div id="video_info">
          <div id="video_id"><span class="text">IPX-124</span></div>
          <div id="video_rating"><span class="text">评分 <span class="score">(7.50)</span></span></div>
        </div></body></html>
        """;
    var legacyResult = await client.ParseDetailPageAsync(
        legacyRatingHtml,
        "https://www.javlibrary.com/cn/?v=legacy");
    AssertEqual("7.5", legacyResult.Rating);
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
    AssertEqual(string.Empty, merged.Series);
    AssertEqual("1", merged.ScreenshotUrls.Count.ToString());
    AssertEqual("LibreDMM + R18.dev", merged.SourceDisplayName);
    return Task.CompletedTask;
}

static async Task TestBrowserImportRouting()
{
    var libre = new MovieMetadata
    {
        Id = "IPZZ-850",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM",
        SourceUrl = "https://www.libredmm.com/movies/IPZZ-850"
    };
    var r18 = new MovieMetadata
    {
        Id = "IPZZ-850",
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        SourceUrl = "https://r18.dev/videos/vod/movies/detail/-/combined=ipzz00850/json"
    };

    var targets = BrowserImportRouting.BuildTargets("IPZZ-850", [libre, r18]);
    AssertEqual("3", targets.Count.ToString());
    AssertEqual("https://www.libredmm.com/movies/IPZZ-850", targets[0].InitialUrl);
    AssertEqual("https://r18.dev/videos/vod/movies/detail/-/id=ipzz00850/", targets[1].InitialUrl);
    AssertEqual("https://www.javlibrary.com/cn/vl_searchbyid.php?keyword=IPZZ-850", targets[2].InitialUrl);
    AssertEqual("True", targets[0].HasExistingResult.ToString());
    AssertEqual("False", targets[2].HasExistingResult.ToString());
    AssertEqual("IPZZ-850", targets[1].RequestedMovieId);

    var actualContentIdResult = new MovieMetadata
    {
        Id = "START-237",
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        ContentId = "1start237",
        SourceUrl = "https://r18.dev/videos/vod/movies/detail/-/dvd_id=start237/json"
    };
    AssertEqual(
        "https://r18.dev/videos/vod/movies/detail/-/id=1start237/",
        BrowserImportRouting.BuildTarget("r18dev", "START-237", [actualContentIdResult]).InitialUrl);
    AssertEqual(
        R18DevClient.HomePageUrl,
        BrowserImportRouting.BuildTarget("r18dev", "FNS-121", []).InitialUrl);

    var unresolvedAbf = BrowserImportRouting.BuildTarget("r18dev", "ABF-193", []);
    AssertEqual("True", BrowserImportRouting.RequiresProviderResolution(unresolvedAbf).ToString());
    using var r18Resolver = FakeMetadataProvider.Success(
        "r18dev",
        "R18.dev",
        new MovieMetadata
        {
            Id = "ABF-193",
            ContentId = "118abf193",
            SourceName = "r18dev",
            SourceDisplayName = "R18.dev",
            SourceUrl = "https://r18.dev/videos/vod/movies/detail/-/combined=118abf193/json"
        });
    var resolvedAbf = await BrowserImportRouting.ResolveWithProviderAsync(unresolvedAbf, r18Resolver);
    AssertEqual("1", r18Resolver.CallCount.ToString());
    AssertEqual(
        "https://r18.dev/videos/vod/movies/detail/-/id=118abf193/",
        resolvedAbf.InitialUrl);
    AssertEqual("False", BrowserImportRouting.RequiresProviderResolution(resolvedAbf).ToString());

    using var unusedResolver = FakeMetadataProvider.Failure(
        "r18dev",
        "R18.dev",
        new InvalidOperationException("已有详情页时不应再次请求来源。 "));
    var retainedResolvedTarget = await BrowserImportRouting.ResolveWithProviderAsync(resolvedAbf, unusedResolver);
    AssertEqual(resolvedAbf.InitialUrl, retainedResolvedTarget.InitialUrl);
    AssertEqual("0", unusedResolver.CallCount.ToString());

    using var incompleteResolver = FakeMetadataProvider.Success(
        "r18dev",
        "R18.dev",
        new MovieMetadata
        {
            Id = "ABF-193",
            Title = "缺少 content_id 的结果",
            SourceName = "r18dev",
            SourceDisplayName = "R18.dev"
        });
    await AssertThrowsAsync<InvalidDataException>(() =>
        BrowserImportRouting.ResolveWithProviderAsync(unresolvedAbf, incompleteResolver));

    var unsafeSource = new MovieMetadata
    {
        Id = "IPZZ-850",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM",
        SourceUrl = "https://www.libredmm.com.example.test/movies/IPZZ-850"
    };
    var safeFallback = BrowserImportRouting.BuildTarget("libredmm", "IPZZ-850", [unsafeSource]);
    AssertEqual("https://www.libredmm.com/movies/IPZZ-850", safeFallback.InitialUrl);
    var searchResultUrl = new MovieMetadata
    {
        Id = "IPZZ-850",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM",
        SourceUrl = "https://www.libredmm.com/search?q=IPZZ-850&format=json"
    };
    AssertEqual(
        "https://www.libredmm.com/movies/IPZZ-850",
        BrowserImportRouting.BuildTarget("libredmm", "IPZZ-850", [searchResultUrl]).InitialUrl);
    AssertEqual("https://r18.dev/videos/vod/movies/detail/-/dvd_id=ipzz850/json",
        R18DevClient.BuildSearchUrl("IPZZ-850"));
    AssertEqual("https://r18.dev/videos/vod/movies/detail/-/id=ipzz00850/",
        R18DevClient.BuildDetailPageUrl("IPZZ-850"));
    AssertEqual("True", BrowserImportRouting.IsExpectedDetailPage(
        "r18dev",
        "https://r18.dev/videos/vod/movies/detail/-/id=ipzz00850/").ToString());
    AssertEqual("False", BrowserImportRouting.IsExpectedDetailPage(
        "r18dev",
        "https://r18.dev/videos/vod/movies/detail/-/combined=ipzz00850/json").ToString());
    AssertEqual(
        "https://r18.dev/videos/vod/movies/detail/-/combined=ipzz00850/json",
        BrowserImportRouting.BuildR18JsonUrl(
            "https://r18.dev/videos/vod/movies/detail/-/id=ipzz00850/"));
    AssertEqual(null, BrowserImportRouting.BuildR18JsonUrl(
        "https://r18.dev.example.test/videos/vod/movies/detail/-/id=ipzz00850/"));
    AssertEqual("True", BrowserImportRouting.ContainsR18NotFoundMessage(
        "Sorry, this page does not exist. Why not return to our homepage?").ToString());
    AssertEqual("False", BrowserImportRouting.ContainsR18NotFoundMessage(
        "Sorry, you have been blocked. You are unable to access r18.dev.").ToString());
    AssertEqual(
        BrowserImportPageState.Ready.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/videos/vod/movies/detail/-/id=ipzz00850/",
            true,
            200,
            true,
            true,
            false).ToString());
    AssertEqual(
        BrowserImportPageState.NotFound.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/videos/vod/movies/detail/-/id=fns00121/",
            true,
            404,
            false,
            false,
            false).ToString());
    AssertEqual(
        BrowserImportPageState.NotFound.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/videos/vod/movies/detail/-/id=fns00121/",
            true,
            200,
            false,
            true,
            true).ToString());
    AssertEqual(
        BrowserImportPageState.Unavailable.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/videos/vod/movies/detail/-/id=fns00121/",
            true,
            503,
            false,
            false,
            true).ToString());
    AssertEqual(
        BrowserImportPageState.Unavailable.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/videos/vod/movies/detail/-/id=fns00121/",
            false,
            0,
            false,
            false,
            false).ToString());
    AssertEqual(
        BrowserImportPageState.ManualSearchRequired.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/",
            true,
            200,
            false,
            true,
            false).ToString());
    AssertEqual(
        BrowserImportPageState.Unavailable.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev/",
            true,
            200,
            false,
            false,
            false).ToString());
    AssertEqual(
        BrowserImportPageState.ManualSearchRequired.ToString(),
        BrowserImportRouting.ClassifyR18Page(
            "https://r18.dev.example.test/missing",
            true,
            404,
            false,
            false,
            false).ToString());
    AssertEqual("False", BrowserImportRouting.IsExpectedDetailPage(
        "libredmm",
        "https://www.libredmm.com.example.test/movies/IPZZ-850").ToString());
    AssertEqual("IPZZ-850", BrowserImportRouting.TryExtractMovieId(
        "libredmm",
        "https://www.libredmm.com/movies/IPZZ-850.json"));
}

static Task TestBrowserCandidateImport()
{
    using var job = new MovieJob();
    job.ResetForVideo("C:\\Movies\\IPZZ-850.mp4", "IPZZ-850");
    var libre = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "LibreDMM 标题",
        Plot = "LibreDMM 简介",
        CoverUrl = "https://images.example.test/libre.jpg",
        ScreenshotUrls =
        [
            "https://images.example.test/sample-1.jpg",
            "https://images.example.test/sample-2.jpg"
        ],
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM",
        SourceUrl = "https://www.libredmm.com/movies/IPZZ-850"
    };
    var r18 = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "R18 title",
        CoverUrl = "https://images.example.test/r18.jpg",
        ScreenshotUrls = ["https://images.example.test/sample-3.jpg"],
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        SourceUrl = "https://r18.dev/videos/vod/movies/detail/-/combined=ipzz00850/json"
    };
    job.ApplyOnlineSources(MetadataMerger.Merge(libre, r18), [libre, r18]);
    AssertEqual("3", job.AvailableScreenshotUrls.Count.ToString());
    job.Metadata.ScreenshotUrls = ["https://images.example.test/sample-1.jpg"];
    AssertEqual("3", job.AvailableScreenshotUrls.Count.ToString());
    job.Metadata.Title = "用户已确认标题";
    if (!job.MetadataReview.SelectCandidate(MetadataField.Title, "libredmm"))
    {
        throw new InvalidOperationException("测试前无法暂时切回 LibreDMM 标题。 ");
    }
    if (!job.SelectArtworkSource("r18dev"))
    {
        throw new InvalidOperationException("测试前无法选择 R18.dev 封套。 ");
    }

    var javLibrary = new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "JAVLibrary 标题",
        Director = "网页导演",
        Rating = "8.9",
        CoverUrl = "https://images.example.test/javlibrary.jpg",
        ScreenshotUrls = ["https://images.example.test/sample-4.jpg"],
        SourceName = "javlibrary",
        SourceDisplayName = "JAVLibrary",
        SourceUrl = "https://www.javlibrary.com/cn/?v=jav-test"
    };
    var promotedCount = job.ApplyManualWebSource(javLibrary);

    AssertEqual("3", promotedCount.ToString());
    AssertEqual("JAVLibrary 标题", job.Metadata.Title);
    AssertEqual("javlibrary", job.MetadataReview.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
    AssertEqual("网页导演", job.Metadata.Director);
    AssertEqual("javlibrary", job.MetadataReview.GetSelectedCandidate(MetadataField.Director)?.Source.Name);
    AssertEqual("8.9", job.Metadata.Rating);
    AssertEqual("javlibrary", job.MetadataReview.GetSelectedCandidate(MetadataField.Rating)?.Source.Name);
    AssertEqual("3", job.SourceResults.Count.ToString());
    AssertEqual("True", job.MetadataReview.GetCandidates(MetadataField.Title)
        .Any(candidate => candidate.Source.Name == "javlibrary").ToString());
    AssertEqual("True", job.MetadataReview.GetCandidates(MetadataField.Title)
        .Any(candidate => candidate.Source.IsManual && candidate.Value == "用户已确认标题").ToString());
    AssertEqual("javlibrary", job.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual("https://images.example.test/javlibrary.jpg", job.Metadata.CoverUrl);
    AssertEqual("4", job.AvailableScreenshotUrls.Count.ToString());

    try
    {
        job.ApplyManualWebSource(new MovieMetadata
        {
            Id = "SONE-999",
            Title = "错误影片",
            ScreenshotUrls = ["https://images.example.test/should-not-be-added.jpg"],
            SourceName = "javlibrary",
            SourceDisplayName = "JAVLibrary"
        });
        throw new InvalidOperationException("错误番号网页资料未被拒绝。 ");
    }
    catch (InvalidDataException)
    {
    }

    AssertEqual("3", job.SourceResults.Count.ToString());
    AssertEqual("JAVLibrary 标题", job.Metadata.Title);
    AssertEqual("4", job.AvailableScreenshotUrls.Count.ToString());

    using var emptyJob = new MovieJob();
    emptyJob.ResetForVideo("C:\\Movies\\IPZZ-850.mkv", "IPZZ-850");
    emptyJob.ApplyManualWebSource(new MovieMetadata
    {
        Id = "IPZZ-850",
        Title = "浏览器标题",
        ContentId = "ipzz00850",
        ScreenshotUrls = ["https://images.example.test/sample-1.jpg"],
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        SourceUrl = "https://r18.dev/videos/vod/movies/detail/-/combined=ipzz00850/json"
    });
    AssertEqual("浏览器标题", emptyJob.Metadata.Title);
    AssertEqual("ipzz00850", emptyJob.Metadata.ContentId);
    AssertEqual("1", emptyJob.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual("r18dev", emptyJob.Metadata.SourceName);
    AssertEqual("https://r18.dev/videos/vod/movies/detail/-/combined=ipzz00850/json",
        emptyJob.Metadata.SourceUrl);

    using var localJob = new MovieJob();
    localJob.ResetForVideo("C:\\Movies\\ABF-193.mp4", "ABF-193");
    var localNfo = new MovieMetadata
    {
        Id = "ABF-193",
        Title = "本地 NFO 标题",
        Director = "本地导演",
        Plot = "网页未提供时保留的本地简介",
        CoverUrl = "C:\\Movies\\ABF-193-poster.jpg",
        SourceName = "local-nfo",
        SourceDisplayName = "本地 NFO"
    };
    localJob.ApplyMetadata(localNfo, [localNfo]);
    var selectedFromWeb = localJob.ApplyManualWebSource(new MovieMetadata
    {
        Id = "ABF-193",
        Title = "ABF-193 Web title",
        Director = "Web director",
        CoverUrl = "https://images.example.test/abf193.jpg",
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev",
        SourceUrl = "https://r18.dev/videos/vod/movies/detail/-/combined=118abf193/json"
    });
    AssertEqual("2", selectedFromWeb.ToString());
    AssertEqual("ABF-193 Web title", localJob.Metadata.Title);
    AssertEqual("r18dev", localJob.MetadataReview.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
    AssertEqual("Web director", localJob.Metadata.Director);
    AssertEqual("网页未提供时保留的本地简介", localJob.Metadata.Plot);
    AssertEqual("local-nfo", localJob.MetadataReview.GetSelectedCandidate(MetadataField.Plot)?.Source.Name);
    AssertEqual("r18dev", localJob.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual("True", localJob.MetadataReview.SelectCandidate(MetadataField.Title, "local-nfo").ToString());
    AssertEqual("本地 NFO 标题", localJob.Metadata.Title);
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
        AssertEqual(string.Empty, complete.Metadata.Series);
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

        using var timeoutPrimary = FakeMetadataProvider.Success("libredmm", "LibreDMM", primaryMetadata);
        using var timeoutSecondary = FakeMetadataProvider.WaitForCancellation("r18dev", "R18.dev");
        var timeoutStopwatch = Stopwatch.StartNew();
        var primaryAfterTimeout = await MetadataSearchCoordinator.SearchAllAsync(
            "IPZZ-850",
            timeoutPrimary,
            timeoutSecondary,
            providerTimeout: TimeSpan.FromMilliseconds(100));
        timeoutStopwatch.Stop();
        AssertEqual("LibreDMM", primaryAfterTimeout.Metadata.SourceDisplayName);
        AssertEqual("1", primaryAfterTimeout.Sources.Count.ToString());
        AssertEqual("True", (primaryAfterTimeout.Attempts[1].Error is MetadataSourceTimeoutException).ToString());
        AssertEqual("True", (timeoutStopwatch.Elapsed < TimeSpan.FromSeconds(2)).ToString());

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
        catch (MultiSourceMergeException)
        {
            mismatchBlocked = true;
        }
        AssertEqual("True", mismatchBlocked.ToString());

        using var singleProvider = FakeMetadataProvider.Success("libredmm", "LibreDMM", primaryMetadata);
        var singleAttempt = await MetadataSearchCoordinator.SearchSingleAsync("IPZZ-850", singleProvider);
        AssertEqual("True", singleAttempt.Success.ToString());
        AssertEqual("1", singleProvider.CallCount.ToString());

        using var retryProvider = FakeMetadataProvider.Failure(
            "r18dev",
            "R18.dev",
            new MetadataNotFoundException("R18.dev", "IPZZ-850"));
        var retryAttempt = await MetadataSearchCoordinator.SearchAttemptAsync("IPZZ-850", retryProvider);
        AssertEqual("False", retryAttempt.Success.ToString());
        AssertEqual("R18.dev", retryAttempt.SourceDisplayName);
        AssertEqual("True", (retryAttempt.Error is MetadataNotFoundException).ToString());
        AssertEqual("1", retryProvider.CallCount.ToString());

        var previousAttempts = new[]
        {
            new MetadataSourceSearchAttempt(
                "libredmm",
                "LibreDMM",
                TimeSpan.FromMilliseconds(25),
                primaryMetadata,
                null,
                4),
            retryAttempt
        };
        using var recoveredProvider = FakeMetadataProvider.Success(
            "r18dev",
            "R18.dev",
            secondaryMetadata);
        var recoveredResult = await MetadataSearchCoordinator.RetryFailedAsync(
            "IPZZ-850",
            previousAttempts,
            [recoveredProvider]);
        AssertEqual("1", recoveredProvider.CallCount.ToString());
        AssertEqual("2", recoveredResult.Sources.Count.ToString());
        AssertEqual("True", recoveredResult.Attempts.All(attempt => attempt.Success).ToString());
        AssertEqual("日文标题", recoveredResult.Metadata.Title);

        using var stillFailingProvider = FakeMetadataProvider.Failure(
            "r18dev",
            "R18.dev",
            new HttpRequestException("still unavailable"));
        var partialRetryResult = await MetadataSearchCoordinator.RetryFailedAsync(
            "IPZZ-850",
            previousAttempts,
            [stillFailingProvider]);
        AssertEqual("1", stillFailingProvider.CallCount.ToString());
        AssertEqual("1", partialRetryResult.Sources.Count.ToString());
        AssertEqual("1", partialRetryResult.Attempts.Count(attempt => !attempt.Success).ToString());

        var log = File.ReadAllText(AppLog.CurrentLogPath);
        AssertEqual("True", log.Contains("mode=multi source=libredmm", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("mode=multi source=r18dev", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("elapsedMs=", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("fields=", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("mode=single", StringComparison.Ordinal).ToString());
        AssertEqual("True", log.Contains("mode=retry", StringComparison.Ordinal).ToString());
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

    var customMerged = MetadataMerger.Merge(primary, fallback);
    using (var customReview = MetadataReviewSession.Create(customMerged, primary, fallback))
    {
        var applied = MetadataSourcePreferenceApplier.Apply(
            customReview,
            new MetadataSourcePreferenceProfile
            {
                TitleSource = MetadataSourcePreferenceProfile.R18Dev,
                DirectorSource = MetadataSourcePreferenceProfile.LibreDmm
            });
        AssertEqual("True", (applied > 0).ToString());
        AssertEqual("English title", customMerged.Title);
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, customReview.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
        AssertEqual("日文简介", customMerged.Plot);
        AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, customReview.GetSelectedCandidate(MetadataField.Plot)?.Source.Name);
        AssertEqual("Upstream director value", customMerged.Director);
        AssertEqual(MetadataSourcePreferenceProfile.R18Dev, customReview.GetSelectedCandidate(MetadataField.Director)?.Source.Name);
    }
    var normalizedProfile = MetadataSourcePreferenceProfile.Normalize(new MetadataSourcePreferenceProfile
    {
        TitleSource = "unsupported",
        RatingSource = "R18DEV"
    });
    AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, normalizedProfile.TitleSource);
    AssertEqual(MetadataSourcePreferenceProfile.R18Dev, normalizedProfile.RatingSource);

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

static async Task TestBatchMetadataSearchCoordinator()
{
    static MovieJob CreateJob(string id)
    {
        var job = new MovieJob();
        job.ResetForVideo($@"C:\Synthetic\{id}.mp4", id);
        return job;
    }

    static MovieMetadata CreateMetadata(string id, string sourceName, string displayName) => new()
    {
        Id = id,
        Title = $"{displayName} {id}",
        Plot = $"{sourceName} plot",
        CoverUrl = $"https://{sourceName}.example.test/{id}.jpg",
        ScreenshotUrls = [$"https://{sourceName}.example.test/{id}-sample.jpg"],
        SourceName = sourceName,
        SourceDisplayName = displayName
    };

    var jobs = Enumerable.Range(1, 5)
        .Select(index => CreateJob($"IPX-{200 + index}"))
        .ToArray();
    jobs[0].Metadata.Title = "批次前手动标题";
    using var primary = new TrackingMetadataProvider(
        "libredmm",
        "LibreDMM",
        async (id, cancellationToken) =>
        {
            await Task.Delay(35, cancellationToken);
            return CreateMetadata(id, "libredmm", "LibreDMM");
        });
    using var secondary = new TrackingMetadataProvider(
        "r18dev",
        "R18.dev",
        async (id, cancellationToken) =>
        {
            await Task.Delay(35, cancellationToken);
            return CreateMetadata(id, "r18dev", "R18.dev");
        });
    var success = await BatchMetadataSearchCoordinator.SearchAsync(jobs, primary, secondary, sourceMode: MetadataSearchSourceModes.Custom);
    AssertEqual("5", success.SucceededCount.ToString());
    AssertEqual("0", success.FailedCount.ToString());
    AssertEqual("True", (primary.MaxConcurrentCalls <= 2).ToString());
    AssertEqual("True", (secondary.MaxConcurrentCalls <= 2).ToString());
    AssertEqual("True", jobs.All(job => job.SearchState is MovieSearchState.NeedsReview).ToString());
    AssertEqual("True", jobs.All(job => job.LastSearchAttempts.Count == 2).ToString());
    AssertEqual(
        "True",
        jobs[0].MetadataReview.GetCandidates(MetadataField.Title)
            .Any(candidate => candidate.Source.IsManual && candidate.Value == "批次前手动标题")
            .ToString());

    var customJobs = new[] { CreateJob("IPX-250"), CreateJob("IPX-251") };
    var custom = await BatchMetadataSearchCoordinator.SearchAsync(
        customJobs,
        primary,
        secondary,
        sourceMode: MetadataSearchSourceModes.Custom,
        sourceProfile: new MetadataSourcePreferenceProfile
        {
            TitleSource = MetadataSourcePreferenceProfile.R18Dev,
            PlotSource = MetadataSourcePreferenceProfile.LibreDmm,
            ArtworkSource = MetadataSourcePreferenceProfile.R18Dev
        });
    AssertEqual("2", custom.SucceededCount.ToString());
    AssertEqual(
        "True",
        customJobs.All(job => job.Metadata.Title.StartsWith("R18.dev", StringComparison.Ordinal)).ToString());
    AssertEqual(
        "True",
        customJobs.All(job => job.MetadataReview.GetSelectedCandidate(MetadataField.Title)?.Source.Name ==
                              MetadataSourcePreferenceProfile.R18Dev).ToString());
    AssertEqual(
        "True",
        customJobs.All(job => job.Metadata.Plot == "libredmm plot").ToString());
    AssertEqual(
        "True",
        customJobs.All(job => job.MetadataReview.GetSelectedCandidate(MetadataField.Plot)?.Source.Name ==
                              MetadataSourcePreferenceProfile.LibreDmm).ToString());
    AssertEqual(
        "True",
        customJobs.All(job => job.ArtworkReview.SelectedCandidate?.Source.Name ==
                              MetadataSourcePreferenceProfile.R18Dev).ToString());
    AssertEqual(
        "True",
        customJobs.All(job => job.Metadata.CoverUrl.Contains("r18dev.example.test", StringComparison.Ordinal)).ToString());

    var bestResolutionJobs = new[] { CreateJob("IPX-253"), CreateJob("IPX-254") };
    var resolverCalls = 0;
    var bestResolution = await BatchMetadataSearchCoordinator.SearchAsync(
        bestResolutionJobs,
        primary,
        secondary,
        sourceMode: MetadataSearchSourceModes.Custom,
        sourceProfile: new MetadataSourcePreferenceProfile
        {
            ArtworkSource = MetadataSourcePreferenceProfile.BestArtworkResolution
        },
        bestArtworkSourceResolver: (sources, _) =>
        {
            Interlocked.Increment(ref resolverCalls);
            AssertEqual("2", sources.Count.ToString());
            return Task.FromResult<string?>(MetadataSourcePreferenceProfile.R18Dev);
        });
    AssertEqual("2", bestResolution.SucceededCount.ToString());
    AssertEqual("2", resolverCalls.ToString());
    AssertEqual(
        "True",
        bestResolutionJobs.All(job =>
            job.ArtworkReview.SelectedCandidate?.Source.Name == MetadataSourcePreferenceProfile.R18Dev &&
            job.Metadata.CoverUrl.Contains("r18dev.example.test", StringComparison.Ordinal) &&
            job.Metadata.ScreenshotUrls.Count == 1 &&
            job.Metadata.ScreenshotUrls[0].Contains("r18dev.example.test", StringComparison.Ordinal) &&
            job.AvailableScreenshotUrls.Count == 2 &&
            job.CanReplaceLocalExtrafanart).ToString());

    var artworkFallbackJob = CreateJob("IPX-252");
    var noArtworkPrimary = CreateMetadata("IPX-252", "libredmm", "LibreDMM");
    noArtworkPrimary.CoverUrl = string.Empty;
    var artworkFallback = CreateMetadata("IPX-252", "r18dev", "R18.dev");
    artworkFallbackJob.BeginSearch();
    artworkFallbackJob.ApplyOnlineSources(
        MetadataMerger.Merge(noArtworkPrimary, artworkFallback),
        [noArtworkPrimary, artworkFallback],
        sourceProfile: new MetadataSourcePreferenceProfile());
    AssertEqual(
        MetadataSourcePreferenceProfile.R18Dev,
        artworkFallbackJob.ArtworkReview.SelectedCandidate?.Source.Name);

    var mixedJobs = new[]
    {
        CreateJob("IPX-301"),
        CreateJob("IPX-302"),
        CreateJob("IPX-303")
    };
    using var mixedPrimary = new TrackingMetadataProvider(
        "libredmm",
        "LibreDMM",
        (id, _) => id == "IPX-302"
            ? Task.FromException<MovieMetadata>(new HttpRequestException("primary failed"))
            : Task.FromResult(CreateMetadata(id, "libredmm", "LibreDMM")));
    using var mixedSecondary = new TrackingMetadataProvider(
        "r18dev",
        "R18.dev",
        (id, _) => id switch
        {
            "IPX-302" => Task.FromException<MovieMetadata>(new HttpRequestException("secondary failed")),
            "IPX-303" => Task.FromResult(CreateMetadata("IPX-999", "r18dev", "R18.dev")),
            _ => Task.FromResult(CreateMetadata(id, "r18dev", "R18.dev"))
        });
    var mixed = await BatchMetadataSearchCoordinator.SearchAsync(mixedJobs, mixedPrimary, mixedSecondary, sourceMode: MetadataSearchSourceModes.Custom);
    AssertEqual("1", mixed.SucceededCount.ToString());
    AssertEqual("2", mixed.FailedCount.ToString());
    AssertEqual("NeedsReview", mixedJobs[0].SearchState.ToString());
    AssertEqual("SearchFailed", mixedJobs[1].SearchState.ToString());
    AssertEqual("SearchFailed", mixedJobs[2].SearchState.ToString());
    AssertEqual("2", mixedJobs[1].LastSearchAttempts.Count.ToString());
    AssertEqual("2", mixedJobs[2].LastSearchAttempts.Count.ToString());
    AssertEqual("True", (mixedJobs[2].LastSearchError is MultiSourceMergeException).ToString());

    var excluded = CreateJob("IPX-399");
    excluded.IsSelectedForBatch = false;
    var needsId = CreateJob("IPX-400");
    needsId.Metadata.Id = string.Empty;
    var noCandidates = await BatchMetadataSearchCoordinator.SearchAsync(
        new[] { excluded, needsId },
        mixedPrimary,
        mixedSecondary);
    AssertEqual("0", noCandidates.Items.Count.ToString());
    AssertEqual("Searchable", excluded.SearchState.ToString());
    AssertEqual("NeedsId", needsId.SearchState.ToString());

    var cancelJobs = Enumerable.Range(1, 4)
        .Select(index => CreateJob($"IPX-{400 + index}"))
        .ToArray();
    var enteredCalls = 0;
    var firstTwoJobsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<MovieMetadata> WaitForCancel(string id, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref enteredCalls) == 4)
        {
            firstTwoJobsEntered.TrySetResult();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return CreateMetadata(id, "never", "Never");
    }

    using var cancelPrimary = new TrackingMetadataProvider("libredmm", "LibreDMM", WaitForCancel);
    using var cancelSecondary = new TrackingMetadataProvider("r18dev", "R18.dev", WaitForCancel);
    using var cancellation = new CancellationTokenSource();
    var cancelTask = BatchMetadataSearchCoordinator.SearchAsync(
        cancelJobs,
        cancelPrimary,
        cancelSecondary,
        cancellation.Token, sourceMode: MetadataSearchSourceModes.Custom);
    await firstTwoJobsEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    var canceled = await cancelTask;
    AssertEqual("2", canceled.CanceledCount.ToString());
    AssertEqual("2", canceled.NotStartedCount.ToString());
    AssertEqual("2", cancelJobs.Count(job => job.SearchState is MovieSearchState.SearchCanceled).ToString());
    AssertEqual("2", cancelJobs.Count(job => job.SearchState is MovieSearchState.Searchable).ToString());

    foreach (var job in jobs.Concat(customJobs).Concat(bestResolutionJobs).Concat(mixedJobs).Concat(cancelJobs)
                 .Append(artworkFallbackJob).Append(excluded).Append(needsId))
    {
        job.Dispose();
    }
}

static async Task TestReviewRevisionAndBatchSave()
{
    static MovieSaveConfiguration CreateConfiguration(bool poster = false) => new(
        new JavMetaLite.Core.Models.SaveOptions(true, poster, false, false, true, false),
        new OrganizationOptions(false, false));

    static MovieJob CreateSelectedJob(string id)
    {
        var job = new MovieJob();
        job.ResetForVideo($@"C:\Synthetic\{id}.mp4", id);
        job.InitializeSaveConfiguration(CreateConfiguration());
        return job;
    }

    static SavePlan CreatePlan(MovieJob job) => new(
        job.VideoPath!,
        job.VideoPath!,
        Path.GetDirectoryName(job.VideoPath!)!,
        job.Metadata.Id,
        job.SaveConfiguration!.SaveOptions,
        job.SaveConfiguration.OrganizationOptions,
        [],
        [],
        []);

    static PreparedMovieSave Prepare(MovieJob job) => new(
        job,
        CreatePlan(job),
        job.ReviewRevision,
        false);

    var revisionJob = CreateSelectedJob("IPX-501");
    var previewRevision = revisionJob.ReviewRevision;
    AssertEqual("True", revisionJob.IsSelectedForBatch.ToString());
    revisionJob.Metadata.Title = "审核后修改";
    AssertEqual("True", (revisionJob.ReviewRevision > previewRevision).ToString());
    AssertEqual("True", revisionJob.IsSelectedForBatch.ToString());
    AssertEqual("Idle", revisionJob.SaveState.ToString());
    revisionJob.UpdateSaveConfiguration(CreateConfiguration(poster: true));
    AssertEqual("True", revisionJob.IsSelectedForBatch.ToString());
    AssertEqual("Idle", revisionJob.SaveState.ToString());

    var first = CreateSelectedJob("IPX-511");
    var second = CreateSelectedJob("IPX-512");
    var third = CreateSelectedJob("IPX-513");
    var executeCount = 0;
    var batchEvents = new List<string>();
    var batch = await BatchSaveCoordinator.ExecuteAsync(
        new[] { Prepare(first), Prepare(second), Prepare(third) },
        (item, _) =>
        {
            executeCount++;
            batchEvents.Add($"execute:{item.Job.Metadata.Id}");
            if (ReferenceEquals(item.Job, second))
            {
                return Task.FromException<OrganizedSaveResult>(
                    new IOException("synthetic target conflict"));
            }

            return Task.FromResult(new OrganizedSaveResult(
                new SaveResult(null, null, null, [], false),
                item.Job.VideoPath!,
                false));
        },
        CancellationToken.None,
        item =>
        {
            batchEvents.Add($"finished:{item.Item.Job.Metadata.Id}:{item.Status}");
            return Task.CompletedTask;
        });
    AssertEqual("2", executeCount.ToString());
    AssertEqual("1", batch.CompletedCount.ToString());
    AssertEqual("1", batch.FailedCount.ToString());
    AssertEqual("1", batch.NotStartedCount.ToString());
    AssertEqual("Completed", first.SaveState.ToString());
    AssertEqual("False", first.IsSelectedForBatch.ToString());
    AssertEqual("Conflict", second.SaveState.ToString());
    AssertEqual("Idle", third.SaveState.ToString());
    AssertEqual("True", third.IsSelectedForBatch.ToString());
    AssertEqual(
        "execute:IPX-511|finished:IPX-511:Completed|execute:IPX-512|finished:IPX-512:Failed",
        string.Join('|', batchEvents));

    first.Metadata.Title = "保存后继续编辑";
    AssertEqual("Idle", first.SaveState.ToString());
    AssertEqual("False", first.IsSelectedForBatch.ToString());

    var stale = CreateSelectedJob("IPX-521");
    var staleItem = Prepare(stale);
    stale.Metadata.Plot = "预览后变化";
    var staleExecuteCount = 0;
    var staleResult = await BatchSaveCoordinator.ExecuteAsync(
        new[] { staleItem },
        (_, _) =>
        {
            staleExecuteCount++;
            throw new InvalidOperationException("不应执行 stale plan");
        });
    AssertEqual("0", staleExecuteCount.ToString());
    AssertEqual("1", staleResult.FailedCount.ToString());
    AssertEqual("Conflict", stale.SaveState.ToString());

    var canceledJob = CreateSelectedJob("IPX-531");
    using var cancellation = new CancellationTokenSource();
    var canceledResult = await BatchSaveCoordinator.ExecuteAsync(
        new[] { Prepare(canceledJob) },
        (_, cancellationToken) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<OrganizedSaveResult>(cancellationToken);
        },
        cancellation.Token);
    AssertEqual("1", canceledResult.CanceledCount.ToString());
    AssertEqual("SaveCanceled", canceledJob.SaveState.ToString());

    foreach (var job in new[] { revisionJob, first, second, third, stale, canceledJob })
    {
        job.Dispose();
    }
}

static async Task TestMovieFileDiscovery()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.DiscoveryTests.{Guid.NewGuid():N}");
    var nested = Path.Combine(root, "nested");
    var multipart = Path.Combine(nested, "multipart");
    Directory.CreateDirectory(multipart);
    try
    {
        var topVideo = Path.Combine(root, "IPX-101.mp4");
        var nestedVideo = Path.Combine(nested, "SONE-202.mkv");
        var cd1 = Path.Combine(multipart, "OFJE-303-CD1.mp4");
        var cd2 = Path.Combine(multipart, "OFJE-303-CD2.mp4");
        await File.WriteAllBytesAsync(topVideo, [0x01]);
        await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "ignored");
        await File.WriteAllBytesAsync(nestedVideo, [0x02]);
        await File.WriteAllBytesAsync(Path.Combine(nested, "cover.jpg"), [0x03]);
        await File.WriteAllBytesAsync(cd1, [0x04]);
        await File.WriteAllBytesAsync(cd2, [0x05]);

        var topOnly = await MovieFileDiscovery.DiscoverAsync(root, includeSubdirectories: false);
        AssertEqual(Path.GetFullPath(root), topOnly.RootPath);
        AssertEqual("1", topOnly.VideoPaths.Count.ToString());
        AssertEqual(Path.GetFullPath(topVideo), topOnly.VideoPaths[0]);
        AssertEqual("1", topOnly.IgnoredFileCount.ToString());
        AssertEqual("0", topOnly.Diagnostics.Count.ToString());

        var recursive = await MovieFileDiscovery.DiscoverAsync(root, includeSubdirectories: true);
        AssertEqual("4", recursive.VideoPaths.Count.ToString());
        AssertEqual("2", recursive.IgnoredFileCount.ToString());
        AssertEqual("True", recursive.VideoPaths.Contains(Path.GetFullPath(topVideo)).ToString());
        AssertEqual("True", recursive.VideoPaths.Contains(Path.GetFullPath(nestedVideo)).ToString());
        AssertEqual("0", recursive.SkippedDirectoryCount.ToString());
        AssertEqual("0", recursive.Diagnostics.Count.ToString());

        var unified = await MovieInputDiscovery.DiscoverAsync(
            [root, nested, topVideo],
            includeSubdirectories: true);
        AssertEqual("3", unified.InputPaths.Count.ToString());
        AssertEqual("3", unified.MovieFileSets.Count.ToString());
        AssertEqual("4", unified.VideoPaths.Count.ToString());
        AssertEqual("2", unified.IgnoredFileCount.ToString());
        AssertEqual("0", unified.Diagnostics.Count.ToString());
        var multipartSet = unified.MovieFileSets.Single(fileSet =>
            fileSet.MovieBaseName.Equals("OFJE-303", StringComparison.OrdinalIgnoreCase));
        AssertEqual("2", multipartSet.Parts.Count.ToString());
        AssertEqual(Path.GetFullPath(cd1), multipartSet.PrimaryPath);

        var unifiedTopOnly = await MovieInputDiscovery.DiscoverAsync([root], includeSubdirectories: false);
        AssertEqual("1", unifiedTopOnly.MovieFileSets.Count.ToString());
        AssertEqual(Path.GetFullPath(topVideo), unifiedTopOnly.MovieFileSets[0].PrimaryPath);

        var missingMixedInput = await MovieInputDiscovery.DiscoverAsync(
            [topVideo, Path.Combine(root, "missing")],
            includeSubdirectories: true);
        AssertEqual("1", missingMixedInput.MovieFileSets.Count.ToString());
        AssertEqual("1", missingMixedInput.Diagnostics.Count.ToString());

        await AssertThrowsAsync<DirectoryNotFoundException>(() =>
            MovieFileDiscovery.DiscoverAsync(Path.Combine(root, "missing"), true));
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static Task TestMovieJobIsolationAndReset()
{
    using var first = new MovieJob();
    using var second = new MovieJob();
    first.ResetForVideo("C:\\Movies\\IPX-123.mp4", "IPX-123");
    second.ResetForVideo("C:\\Movies\\SONE-456.mp4", "SONE-456");

    var primary = new MovieMetadata
    {
        Id = "IPX-123",
        Title = "在线标题",
        Director = "在线导演",
        CoverUrl = "https://images.example.test/ipx-cover.jpg",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    var fallback = new MovieMetadata
    {
        Id = "IPX-123",
        Title = "English title",
        SourceName = "r18dev",
        SourceDisplayName = "R18.dev"
    };
    first.ApplyMetadata(MetadataMerger.Merge(primary, fallback), [primary, fallback]);
    var localExtraPath = "C:\\Movies\\extrafanart\\fanart1.jpg";
    var localPosterPath = "C:\\Movies\\IPX-123-poster.jpg";
    var localFanartPath = "C:\\Movies\\IPX-123-fanart.jpg";
    first.SetLocalExtrafanart([localExtraPath]);
    first.SetLocalArtwork(ArtworkCoverCandidate.CreateSidecarPair(
        new MetadataCandidateSource("local-images", "本地图片", "C:\\Movies"),
        localPosterPath,
        localFanartPath));
    first.Metadata.Title = "搜索前手动标题";
    AssertEqual(Path.GetFullPath(localExtraPath), first.Metadata.ScreenshotUrls[0]);
    AssertEqual(Path.GetFullPath(localExtraPath), first.AvailableScreenshotUrls[0]);
    AssertEqual(Path.GetFullPath(localExtraPath), first.CreateLocalSaveContext().LocalExtrafanartPaths[0]);
    first.RemoveLocalArtworkLocations([localExtraPath, localPosterPath]);
    AssertEqual("0", first.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual("0", first.AvailableScreenshotUrls.Count.ToString());
    AssertEqual("0", first.LocalExtrafanartPaths.Count.ToString());
    AssertEqual("False", first.LocalArtworkCandidate?.HasPoster.ToString());
    AssertEqual("True", first.LocalArtworkCandidate?.HasFanart.ToString());

    AssertEqual("SONE-456", second.Metadata.Id);
    AssertEqual(string.Empty, second.Metadata.Title);
    AssertEqual("0", second.MetadataReview.GetCandidates(MetadataField.Title).Count.ToString());

    var manualArtwork = ArtworkCoverCandidate.CreateCompleteCover(
        new MetadataCandidateSource("manual-cover", "手动封套", "C:\\Pictures\\cover.jpg"),
        "C:\\Pictures\\cover.jpg");
    first.SetManualArtwork(manualArtwork);
    AssertEqual("manual-cover", first.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual("manual-cover", first.CreateLocalSaveContext().SelectedArtwork?.Source.Name);
    AssertEqual("0", second.ArtworkReview.Candidates.Count.ToString());

    var replacedMetadata = first.Metadata;
    var replacedReview = first.MetadataReview;
    var forwardedChanges = 0;
    first.MetadataPropertyChanged += (_, _) => forwardedChanges++;

    var refreshedOnline = new MovieMetadata
    {
        Id = "IPX-123",
        Title = "刷新后的在线标题",
        Director = "刷新后的在线导演",
        CoverUrl = "https://images.example.test/ipx-refreshed.jpg",
        SourceName = "libredmm",
        SourceDisplayName = "LibreDMM"
    };
    first.ApplyOnlineSources(refreshedOnline, [refreshedOnline]);
    AssertEqual("刷新后的在线标题", first.Metadata.Title);
    AssertEqual("0", first.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual(
        "1",
        first.MetadataReview.GetCandidates(MetadataField.Title)
            .Count(candidate => candidate.Source.IsManual)
            .ToString());
    AssertEqual("搜索前手动标题", first.MetadataReview.GetCandidates(MetadataField.Title)
        .Single(candidate => candidate.Source.IsManual).Value);
    AssertEqual("libredmm", first.MetadataReview.GetSelectedCandidate(MetadataField.Title)?.Source.Name);

    var changesAfterReplacement = forwardedChanges;
    replacedMetadata.Title = "不应再转发的旧 metadata";
    AssertEqual(changesAfterReplacement.ToString(), forwardedChanges.ToString());
    try
    {
        replacedReview.SetManualValue(MetadataField.Title, "旧审核会话");
        throw new InvalidOperationException("被替换的 MetadataReviewSession 仍然可以修改状态。 ");
    }
    catch (ObjectDisposedException)
    {
    }

    first.BeginLocalNfoRead();
    AssertEqual("True", first.LocalNfoSaveBlocked.ToString());
    first.ResetForVideo("C:\\Movies\\IPX-124.mp4", "IPX-124");
    AssertEqual("IPX-124", first.Metadata.Id);
    AssertEqual(string.Empty, first.Metadata.Title);
    AssertEqual("False", first.LocalNfoSaveBlocked.ToString());
    AssertEqual("0", first.SourceResults.Count.ToString());
    AssertEqual("0", first.ArtworkReview.Candidates.Count.ToString());
    AssertEqual("0", first.LocalExtrafanartPaths.Count.ToString());
    AssertEqual(null, first.ManualArtworkCandidate?.Source.Name);
    AssertEqual(null, first.PreferredArtworkSourceName);
    AssertEqual(null, first.CreateLocalSaveContext().MetadataBundle?.Sidecars.NfoPath);
    AssertEqual("SONE-456", second.Metadata.Id);
    return Task.CompletedTask;
}

static async Task TestRetryPreservesReviewSelections()
{
    var primary = new MovieMetadata
    {
        Id = "IPX-669", Title = "Source title", Maker = "Source maker",
        CoverUrl = "https://images.example.test/cover.jpg",
        ScreenshotUrls = ["https://images.example.test/one.jpg", "https://images.example.test/two.jpg"],
        SourceName = "libredmm", SourceDisplayName = "LibreDMM"
    };
    var recovered = new MovieMetadata
    {
        Id = primary.Id, Title = "Recovered title", Maker = "Recovered maker", Plot = "New plot",
        CoverUrl = "https://images.example.test/recovered-cover.jpg",
        ScreenshotUrls = ["https://images.example.test/recovered.jpg"],
        SourceName = "r18dev", SourceDisplayName = "R18.dev"
    };
    using var job = new MovieJob();
    job.ResetForVideo(@"C:\Movies\IPX-669.mp4", primary.Id);
    var attempts = new[]
    {
        new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, primary, null, 2),
        new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null, new IOException("offline"), 0)
    };
    job.ApplyOnlineSources(primary, [primary], attempts);
    job.MetadataReview.SetManualValue(MetadataField.Title, "My reviewed title");
    job.MetadataReview.SetManualValue(MetadataField.Director, ""); // Intentional blank is also a choice.
    job.MetadataReview.SelectCandidate(MetadataField.Maker, "libredmm"); // Explicit source selection is pinned; automatic fallback is not.
    job.Metadata.ScreenshotUrls = [primary.ScreenshotUrls[0]];
    using var provider = FakeMetadataProvider.Success("r18dev", "R18.dev", recovered);
    var result = await MetadataSearchCoordinator.RetryFailedAsync(primary.Id, attempts, [provider]);
    job.BeginSearch();
    job.ApplyRetriedOnlineSources(result.Metadata, result.Sources, result.Attempts,
        new MetadataSourcePreferenceProfile { TitleSource = "r18dev", MakerSource = "r18dev", ArtworkSource = "r18dev" });
    AssertEqual("My reviewed title", job.Metadata.Title);
    AssertEqual("manual", job.MetadataReview.GetSelectedCandidate(MetadataField.Title)?.Source.Name);
    AssertEqual("manual", job.MetadataReview.GetSelectedCandidate(MetadataField.Director)?.Source.Name);
    AssertEqual("Source maker", job.Metadata.Maker);
    AssertEqual("libredmm", job.MetadataReview.GetSelectedCandidate(MetadataField.Maker)?.Source.Name);
    AssertEqual("New plot", job.Metadata.Plot);
    AssertEqual("libredmm", job.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual("1", job.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual("3", job.AvailableScreenshotUrls.Count.ToString());
    AssertEqual("Source title", primary.Title);
    AssertEqual("2", primary.ScreenshotUrls.Count.ToString());
    AssertEqual("True", job.MetadataReview.GetCandidates(MetadataField.Title).Any(c => c.Source.Name == "r18dev").ToString());

    // A later retry must not restore samples explicitly deselected by the user.
    job.Metadata.ScreenshotUrls = [];
    job.MetadataReview.SetManualValue(MetadataField.Actors, "Reviewed actor");
    job.Metadata.Actors = [new ActorMetadata("Reviewed actor", "https://images.example.test/actor.jpg")];
    job.SetManualArtwork(ArtworkCoverCandidate.CreateCompleteCover(
        new MetadataCandidateSource("manual-cover", "Manual cover", @"C:\Pictures\reviewed.jpg"),
        @"C:\Pictures\reviewed.jpg"));
    job.BeginSearch();
    job.ApplyRetriedOnlineSources(result.Metadata, result.Sources, result.Attempts);
    AssertEqual("0", job.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual("manual-cover", job.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual("manual", job.MetadataReview.GetSelectedCandidate(MetadataField.Actors)?.Source.Name);
    AssertEqual("https://images.example.test/actor.jpg", job.Metadata.Actors.Single().ImageUrl);
    AssertEqual("True", job.CanReplaceLocalExtrafanart.ToString());

    job.ApplyManualWebSource(new MovieMetadata
    {
        Id = primary.Id, Rating = "8.5", SourceName = "javlibrary", SourceDisplayName = "JAVLibrary"
    });
    job.BeginSearch();
    job.ApplyRetriedOnlineSources(result.Metadata, result.Sources, result.Attempts);
    AssertEqual("8.5", job.Metadata.Rating);
    AssertEqual("javlibrary", job.MetadataReview.GetSelectedCandidate(MetadataField.Rating)?.Source.Name);

    // With no previous successful source, recovery should fill blank fields and images.
    using var emptyJob = new MovieJob();
    emptyJob.ResetForVideo(@"C:\Movies\IPX-669.mp4", primary.Id);
    emptyJob.MetadataReview.SetManualValue(MetadataField.Title, "Keep this title");
    emptyJob.BeginSearch();
    emptyJob.ApplyRetriedOnlineSources(recovered, [recovered], result.Attempts);
    AssertEqual("Keep this title", emptyJob.Metadata.Title);
    AssertEqual("Recovered maker", emptyJob.Metadata.Maker);
    AssertEqual("r18dev", emptyJob.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual(recovered.ScreenshotUrls[0], emptyJob.Metadata.ScreenshotUrls.Single());
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
    AssertEqual(string.Empty, result.Series);
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

static async Task TestArtworkResolutionSelection()
{
    var libre = new MovieMetadata
    {
        Id = "IPZZ-850",
        CoverUrl = "https://libre.example/cover.jpg",
        ScreenshotUrls = ["https://libre.example/sample.jpg"],
        SourceName = MetadataSourcePreferenceProfile.LibreDmm,
        SourceDisplayName = "LibreDMM"
    };
    var r18 = new MovieMetadata
    {
        Id = "IPZZ-850",
        CoverUrl = "https://r18.example/cover.jpg",
        ScreenshotUrls = ["https://r18.example/sample.jpg"],
        SourceName = MetadataSourcePreferenceProfile.R18Dev,
        SourceDisplayName = "R18.dev"
    };
    var dimensions = new Dictionary<string, ArtworkPixelSize>(StringComparer.OrdinalIgnoreCase)
    {
        [libre.CoverUrl] = new(800, 539),
        [r18.CoverUrl] = new(2184, 1468)
    };
    var selected = await ArtworkResolutionSelector.SelectBestAsync(
        [libre, r18],
        (location, _) => Task.FromResult(dimensions[location]));
    AssertEqual(MetadataSourcePreferenceProfile.R18Dev, selected?.SourceName);
    AssertEqual("2184", selected?.Size.Width.ToString());
    AssertEqual("1468", selected?.Size.Height.ToString());

    dimensions[r18.CoverUrl] = dimensions[libre.CoverUrl];
    var tie = await ArtworkResolutionSelector.SelectBestAsync(
        [libre, r18],
        (location, _) => Task.FromResult(dimensions[location]));
    AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, tie?.SourceName);

    var fallback = await ArtworkResolutionSelector.SelectBestAsync(
        [libre, r18],
        (location, _) => location == r18.CoverUrl
            ? Task.FromException<ArtworkPixelSize>(new InvalidDataException("synthetic invalid image"))
            : Task.FromResult(dimensions[location]));
    AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, fallback?.SourceName);

    var singleSourceProbeCount = 0;
    var single = await ArtworkResolutionSelector.SelectBestAsync(
        [r18],
        (_, _) =>
        {
            singleSourceProbeCount++;
            return Task.FromResult(new ArtworkPixelSize(1, 1));
        });
    AssertEqual(MetadataSourcePreferenceProfile.R18Dev, single?.SourceName);
    AssertEqual("0", singleSourceProbeCount.ToString());

    using var job = new MovieJob();
    job.ResetForVideo(@"C:\Movies\IPZZ-850.mp4", "IPZZ-850");
    var localExtra = @"C:\Movies\extrafanart\fanart1.jpg";
    job.SetLocalExtrafanart([localExtra]);
    job.BeginSearch();
    job.ApplyOnlineSources(
        MetadataMerger.Merge(libre, r18),
        [libre, r18],
        sourceProfile: new MetadataSourcePreferenceProfile
        {
            ArtworkSource = MetadataSourcePreferenceProfile.BestArtworkResolution
        },
        resolvedArtworkBundleSource: MetadataSourcePreferenceProfile.R18Dev);
    AssertEqual(MetadataSourcePreferenceProfile.R18Dev, job.ArtworkReview.SelectedCandidate?.Source.Name);
    AssertEqual("2", job.Metadata.ScreenshotUrls.Count.ToString());
    AssertEqual(Path.GetFullPath(localExtra), job.Metadata.ScreenshotUrls[0]);
    AssertEqual(r18.ScreenshotUrls[0], job.Metadata.ScreenshotUrls[1]);
    AssertEqual("3", job.AvailableScreenshotUrls.Count.ToString());
    AssertEqual("True", job.CanReplaceLocalExtrafanart.ToString());
    AssertEqual("True", job.CreateLocalSaveContext().CanReplaceLocalExtrafanart.ToString());

    job.Metadata.Id = "IPZZ-851";
    AssertEqual("False", job.CanReplaceLocalExtrafanart.ToString());
    AssertEqual("False", job.CreateLocalSaveContext().CanReplaceLocalExtrafanart.ToString());

    var normalized = MetadataSourcePreferenceProfile.Normalize(new MetadataSourcePreferenceProfile
    {
        TitleSource = MetadataSourcePreferenceProfile.BestArtworkResolution,
        ArtworkSource = MetadataSourcePreferenceProfile.BestArtworkResolution
    });
    AssertEqual(MetadataSourcePreferenceProfile.LibreDmm, normalized.TitleSource);
    AssertEqual(MetadataSourcePreferenceProfile.BestArtworkResolution, normalized.ArtworkSource);
}

static async Task TestR18BrowserImport()
{
    const string json = """
        {
          "dvd_id": "IPX-081",
          "content_id": "ipx00081",
          "title_en": "English browser title",
          "title_ja": "日本語ブラウザタイトル",
          "release_date": "2018-01-13"
        }
        """;
    const string detailPageUrl = "https://r18.dev/videos/vod/movies/detail/-/id=ipx00081/";
    const string jsonUrl = "https://r18.dev/videos/vod/movies/detail/-/combined=ipx00081/json";
    using var httpClient = new HttpClient(new FakeJsonHandler(new Dictionary<string, (HttpStatusCode, string)>
    {
        [jsonUrl] = (HttpStatusCode.OK, json)
    }));
    using var client = new R18DevClient(httpClient);

    var result = await client.ImportDetailPageAsync(detailPageUrl, "IPX-081");
    AssertEqual("IPX-081", result.Id);
    AssertEqual("English browser title", result.Title);
    AssertEqual("日本語ブラウザタイトル", result.OriginalTitle);
    AssertEqual(jsonUrl, result.SourceUrl);

    await AssertThrowsAsync<InvalidDataException>(() => client.ImportDetailPageAsync(
        jsonUrl,
        "IPX-081"));
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

    const string abfCompactJson = """
        {
          "content_id": "118abf193",
          "title": "ABF compact title"
        }
        """;
    const string abfDetailedJson = """
        {
          "dvd_id": "ABF-193",
          "content_id": "118abf193",
          "title_en": "ABF-193 detailed title",
          "title_ja": "ABF-193 日本語タイトル"
        }
        """;
    using var abfHttpClient = new HttpClient(new FakeJsonHandler(new Dictionary<string, (HttpStatusCode, string)>
    {
        ["https://r18.dev/videos/vod/movies/detail/-/combined=abf00193/json"] =
            (HttpStatusCode.NotFound, string.Empty),
        ["https://r18.dev/videos/vod/movies/detail/-/dvd_id=abf193/json"] =
            (HttpStatusCode.OK, abfCompactJson),
        ["https://r18.dev/videos/vod/movies/detail/-/combined=118abf193/json"] =
            (HttpStatusCode.OK, abfDetailedJson)
    }));
    using var abfClient = new R18DevClient(abfHttpClient);
    var abfResult = await abfClient.SearchAsync("ABF-193");
    AssertEqual("ABF-193", abfResult.Id);
    AssertEqual("118abf193", abfResult.ContentId);
    AssertEqual("ABF-193 detailed title", abfResult.Title);
    AssertEqual(
        "https://r18.dev/videos/vod/movies/detail/-/id=118abf193/",
        BrowserImportRouting.BuildTarget("r18dev", "ABF-193", [abfResult]).InitialUrl);
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
            Series = "不应写入的新系列",
            SourceUrl = "https://www.javlibrary.com/cn/?v=test",
            ContentId = "ipx00123",
            SourceName = "JAVLibrary"
        };

        await NfoWriter.WriteAsync(
            path,
            metadata,
            "IPX-123-poster.jpg",
            "IPX-123-fanart.jpg",
            includeIdInTitle: true,
            overwrite: false);
        var document = XDocument.Load(path);
        AssertEqual("IPX-123 · 示例标题", document.Root?.Element("title")?.Value);
        AssertEqual("日本語タイトル", document.Root?.Element("originaltitle")?.Value);
        AssertEqual("IPX-123", document.Root?.Element("id")?.Value);
        var canonicalUniqueId = document.Root?.Elements("uniqueid").Single(element =>
            element.Attribute("type")?.Value == "javnumber");
        AssertEqual("IPX-123", canonicalUniqueId?.Value);
        AssertEqual("true", canonicalUniqueId?.Attribute("default")?.Value);
        AssertEqual("ipx00123", document.Root?.Elements("uniqueid").Single(element =>
            element.Attribute("type")?.Value == "javlibrary").Value);
        AssertEqual("2024", document.Root?.Element("year")?.Value);
        AssertEqual("2", document.Root?.Elements("actor").Count().ToString());
        AssertEqual("https://images.example.test/actor-a.jpg", document.Root?.Elements("actor").First()?.Element("thumb")?.Value);
        AssertEqual("IPX-123-poster.jpg", document.Root?.Element("thumb")?.Value);
        AssertEqual("IPX-123-fanart.jpg", document.Root?.Element("fanart")?.Element("thumb")?.Value);
        AssertEqual("False", document.Root?.Elements("tag").Any(element =>
            element.Value.StartsWith("Series:", StringComparison.OrdinalIgnoreCase)).ToString());
        AssertEqual("IPX-123 · 示例标题", JellyfinTitleFormatter.Format("IPX-123", "示例标题", true));
        AssertEqual("IPX-123 · 示例标题", JellyfinTitleFormatter.Format("IPX-123", "IPX-123 · 示例标题", true));
        AssertEqual("示例标题", JellyfinTitleFormatter.Format("IPX-123", "IPX-123 · 示例标题", false));
        AssertEqual("IPX-123", JellyfinTitleFormatter.Format("IPX-123", string.Empty, true));

        var cleanTitlePath = Path.Combine(root, "IPX-123-clean.nfo");
        metadata.Title = "IPX-123 · 示例标题";
        await NfoWriter.WriteAsync(
            cleanTitlePath,
            metadata,
            null,
            null,
            includeIdInTitle: false,
            overwrite: false);
        AssertEqual("示例标题", XDocument.Load(cleanTitlePath).Root?.Element("title")?.Value);
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
            new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, true, false));
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
            new JavMetaLite.Core.Models.SaveOptions(true, true, true, true, true, false));
        AssertEqual("5", conflicts.Count.ToString());
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static async Task TestMissingSampleImagesAreSkipped()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.NoSampleImagesTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var videoPath = Path.Combine(root, "START-585.mp4");
        await File.WriteAllBytesAsync(videoPath, [0x53, 0x54, 0x41, 0x52, 0x54]);
        var metadata = new MovieMetadata
        {
            Id = "START-585",
            Title = "无独立剧照测试",
            ScreenshotUrls = []
        };

        var options = new JavMetaLite.Core.Models.SaveOptions(false, false, false, true, true, false);
        using var service = new OutputService();
        var result = await service.SaveAsync(
            videoPath,
            metadata,
            options);

        AssertEqual("0", result.ExtrafanartPaths.Count.ToString());
        AssertEqual("False", Directory.Exists(Path.Combine(root, "extrafanart")).ToString());
        AssertEqual("True", File.Exists(videoPath).ToString());

        var organizationService = new FileOrganizationService(service);
        var plan = FileOrganizationService.BuildPlan(
            videoPath,
            metadata,
            options,
            new OrganizationOptions(false, false));
        var organized = await organizationService.ExecuteAsync(plan, metadata, allowOverwrite: false);
        AssertEqual("0", organized.Outputs.ExtrafanartPaths.Count.ToString());
        AssertEqual(videoPath, organized.VideoPath);
        AssertEqual("False", organized.VideoMoved.ToString());
        AssertEqual("True", File.Exists(videoPath).ToString());
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
            new JavMetaLite.Core.Models.SaveOptions(true, true, true, false, true, false));

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
        var saveOptions = new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, true, false);
        AssertEqual("True", saveOptions.RequiresPreview.ToString());
        AssertEqual(
            "False",
            new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, true, true).RequiresPreview.ToString());
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
        AssertEqual("SNOS-255 · 覆盖后的标题", nfo.Root?.Element("title")?.Value);

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

static async Task TestMultipartFileOrganization()
{
    var root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.MultipartOrganization.{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var sourceDirectory = Path.Combine(root, "incoming");
        Directory.CreateDirectory(sourceDirectory);
        var cd1 = Path.Combine(sourceDirectory, "download@IPX-123-CD1.mp4");
        var cd2 = Path.Combine(sourceDirectory, "download@IPX-123_cd2.mkv");
        await File.WriteAllBytesAsync(cd1, [0x01, 0x02]);
        await File.WriteAllBytesAsync(cd2, [0x03, 0x04, 0x05]);
        var metadata = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "多分段测试",
            Series = "不应新写入"
        };
        var saveOptions = new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, true, false);
        var automaticFolderPlan = FileOrganizationService.BuildPlan(
            [cd2, cd1],
            metadata,
            saveOptions,
            new OrganizationOptions(false, false));
        AssertEqual(Path.Combine(sourceDirectory, "IPX-123"), automaticFolderPlan.TargetDirectory);
        AssertEqual(
            OrganizationTargetMode.SourceNumberFolder.ToString(),
            automaticFolderPlan.OrganizationOptions.TargetMode.ToString());
        AssertEqual(OutputNamingMode.MovieFolder.ToString(), automaticFolderPlan.OutputNamingMode.ToString());

        var plan = FileOrganizationService.BuildPlan(
            [cd2, cd1],
            metadata,
            saveOptions,
            new OrganizationOptions(true, true));

        var targetDirectory = Path.Combine(sourceDirectory, "IPX-123");
        var targetCd1 = Path.Combine(targetDirectory, "IPX-123-cd1.mp4");
        var targetCd2 = Path.Combine(targetDirectory, "IPX-123-cd2.mkv");
        AssertEqual("2", plan.VideoTransfers.Count.ToString());
        AssertEqual(targetCd1, plan.VideoTransfers[0].TargetPath);
        AssertEqual(targetCd2, plan.VideoTransfers[1].TargetPath);
        AssertEqual(Path.Combine(targetDirectory, "IPX-123.mp4"), plan.OutputAnchorPath);
        AssertEqual("2", plan.Changes.Count(change =>
            change.Kind == PlannedChangeKind.MoveAndRenameVideo).ToString());

        using var outputService = new OutputService();
        var result = await new FileOrganizationService(outputService)
            .ExecuteAsync(plan, metadata, false);
        AssertEqual("2", result.VideoPaths.Count.ToString());
        AssertEqual(targetCd1, result.VideoPaths[0]);
        AssertEqual(targetCd2, result.VideoPaths[1]);
        AssertEqual("False", File.Exists(cd1).ToString());
        AssertEqual("False", File.Exists(cd2).ToString());
        AssertEqual("True", File.Exists(targetCd1).ToString());
        AssertEqual("True", File.Exists(targetCd2).ToString());
        var nfoPath = Path.Combine(targetDirectory, "movie.nfo");
        AssertEqual("True", File.Exists(nfoPath).ToString());
        AssertEqual("False", File.Exists(Path.Combine(targetDirectory, "IPX-123.nfo")).ToString());
        AssertEqual("False", File.Exists(Path.Combine(targetDirectory, "IPX-123-cd1.nfo")).ToString());
        AssertEqual("False", File.Exists(Path.Combine(targetDirectory, "IPX-123-cd2.nfo")).ToString());
        var nfo = XDocument.Load(nfoPath);
        AssertEqual("False", nfo.Root?.Elements("tag").Any(element =>
            element.Value.StartsWith("Series:", StringComparison.OrdinalIgnoreCase)).ToString());
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
        var extrafanartDirectory = Path.Combine(root, "extrafanart");
        Directory.CreateDirectory(extrafanartDirectory);
        var extra2Path = Path.Combine(extrafanartDirectory, "fanart2.PNG");
        var extra10Path = Path.Combine(extrafanartDirectory, "fanart10.jpg");
        await File.WriteAllBytesAsync(extra10Path, [0x10]);
        await File.WriteAllBytesAsync(extra2Path, [0x02]);
        await File.WriteAllTextAsync(Path.Combine(extrafanartDirectory, "notes.txt"), "ignore");

        var before = Directory.EnumerateFiles(root)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var sidecars = LocalSidecarLocator.Locate(videoPath);
        var after = Directory.EnumerateFiles(root)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        AssertEqual(Path.GetFullPath(videoPath), sidecars.VideoPath);
        AssertEqual(Path.GetFullPath(nfoPath), sidecars.NfoPath);
        AssertEqual(Path.GetFullPath(posterPath), sidecars.PosterPath);
        AssertEqual(Path.GetFullPath(fanartPath), sidecars.FanartPath);
        AssertEqual("2", sidecars.ExtrafanartPaths.Count.ToString());
        AssertEqual(Path.GetFullPath(extra2Path), sidecars.ExtrafanartPaths[0]);
        AssertEqual(Path.GetFullPath(extra10Path), sidecars.ExtrafanartPaths[1]);
        AssertEqual("True", sidecars.HasNfo.ToString());
        AssertEqual("True", sidecars.HasArtwork.ToString());
        AssertEqual(before.Count.ToString(), after.Count.ToString());
        foreach (var (path, bytes) in before)
        {
            AssertEqual(Convert.ToHexString(bytes), Convert.ToHexString(after[path]));
        }

        var movieNfoPath = Path.Combine(root, "movie.nfo");
        var folderPosterPath = Path.Combine(root, "poster.webp");
        var folderFanartPath = Path.Combine(root, "fanart.jpeg");
        await File.WriteAllTextAsync(movieNfoPath, "<movie><title>Folder sidecars</title></movie>");
        await File.WriteAllBytesAsync(folderPosterPath, [0x06]);
        await File.WriteAllBytesAsync(folderFanartPath, [0x07]);
        var folderSidecars = LocalSidecarLocator.Locate(videoPath, "SNOS-255", true);
        AssertEqual(Path.GetFullPath(movieNfoPath), folderSidecars.NfoPath);
        AssertEqual(Path.GetFullPath(folderPosterPath), folderSidecars.PosterPath);
        AssertEqual(Path.GetFullPath(folderFanartPath), folderSidecars.FanartPath);

        File.Delete(nfoPath);
        File.Delete(posterPath);
        File.Delete(fanartPath);
        File.Delete(movieNfoPath);
        File.Delete(folderPosterPath);
        File.Delete(folderFanartPath);
        Directory.Delete(extrafanartDirectory, true);
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
        var extraDirectory = Path.Combine(root, "extrafanart");
        var extra1Path = Path.Combine(extraDirectory, "fanart1.jpg");
        var invalidExtraPath = Path.Combine(extraDirectory, "fanart2.jpg");
        var videoBytes = new byte[] { 0x53, 0x54, 0x41, 0x52, 0x54, 0x02, 0x37 };
        await File.WriteAllBytesAsync(videoPath, videoBytes);
        await File.WriteAllBytesAsync(posterPath, CreateJpeg(420, 600, 0xA4, 0x5B, 0x39));
        await File.WriteAllBytesAsync(fanartPath, CreateJpeg(800, 538, 0x39, 0x5B, 0xA4));
        Directory.CreateDirectory(extraDirectory);
        await File.WriteAllBytesAsync(extra1Path, CreateJpeg(800, 450, 0x22, 0x66, 0x88));
        await File.WriteAllBytesAsync(invalidExtraPath, [0x01, 0x02]);

        var sidecars = new LocalSidecarPaths(videoPath, null, posterPath, fanartPath)
        {
            ExtrafanartPaths = [extra1Path, invalidExtraPath]
        };

        var complete = await LocalArtworkDiscovery.DiscoverAsync(sidecars);
        AssertEqual("1", complete.Diagnostics.Count.ToString());
        AssertEqual("True", complete.Diagnostics[0].Contains("extrafanart", StringComparison.Ordinal).ToString());
        AssertEqual("1", complete.ExtrafanartPaths.Count.ToString());
        AssertEqual(Path.GetFullPath(extra1Path), complete.ExtrafanartPaths[0]);
        AssertEqual("local-images", complete.Candidate?.Source.Name);
        AssertEqual("True", complete.Candidate?.IsSidecarPair.ToString());
        AssertEqual("True", complete.Candidate?.HasPoster.ToString());
        AssertEqual("True", complete.Candidate?.HasFanart.ToString());
        AssertEqual(Path.GetFullPath(posterPath), complete.Candidate?.LocalPosterPath);
        AssertEqual(Path.GetFullPath(fanartPath), complete.Candidate?.LocalFanartPath);

        await File.WriteAllBytesAsync(posterPath, [0x00, 0x01, 0x02]);
        var partial = await LocalArtworkDiscovery.DiscoverAsync(sidecars);
        AssertEqual("2", partial.Diagnostics.Count.ToString());
        AssertEqual("True", partial.Diagnostics.Any(message => message.Contains("poster", StringComparison.Ordinal)).ToString());
        AssertEqual("False", partial.Candidate?.HasPoster.ToString());
        AssertEqual("True", partial.Candidate?.HasFanart.ToString());
        AssertEqual(string.Empty, partial.Candidate?.LocalPosterPath);

        await File.WriteAllBytesAsync(fanartPath, [0x03, 0x04, 0x05]);
        var invalid = await LocalArtworkDiscovery.DiscoverAsync(sidecars);
        AssertEqual("True", (invalid.Candidate is null).ToString());
        AssertEqual("3", invalid.Diagnostics.Count.ToString());
        AssertEqual("1", invalid.ExtrafanartPaths.Count.ToString());
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
        AssertEqual(string.Empty, bundle.Metadata.Series);
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
              <title origin="keep">IPX-321 · 旧标题</title>
              <id>IPX-321</id>
              <uniqueid type="javnumber" default="true">IPX-321</uniqueid>
              <uniqueid type="jav">ipx00321</uniqueid>
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
              <tag>Series: Legacy Series</tag>
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
            NfoRoundTripWriter.HasChanges(bundle, editable, false, null, false, null, true).ToString());

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

        var cleanTitleDocument = NfoRoundTripWriter.CreateUpdatedDocument(
            bundle,
            editable,
            updatePosterReference: false,
            posterFileName: null,
            updateFanartReference: false,
            fanartFileName: null,
            includeIdInTitle: false);
        AssertEqual("新标题", cleanTitleDocument.Root?.Element("title")?.Value);

        AssertEqual(
            "True",
            NfoRoundTripWriter.HasChanges(
                bundle,
                editable,
                true,
                "IPX-321-poster.jpg",
                true,
                "IPX-321-fanart.jpg",
                true).ToString());
        await NfoRoundTripWriter.WriteAsync(
            outputPath,
            bundle,
            editable,
            true,
            "IPX-321-poster.jpg",
            true,
            "IPX-321-fanart.jpg",
            true,
            false);

        var updated = XDocument.Load(outputPath, LoadOptions.PreserveWhitespace);
        var movie = updated.Root!;
        AssertEqual("root-keep", movie.Attribute("custom")?.Value);
        AssertEqual("True", updated.DescendantNodes().OfType<XComment>().Any().ToString());
        AssertEqual("42", movie.Element("unknown")?.Attribute("answer")?.Value);
        AssertEqual("nested", movie.Element("unknown")?.Element("child")?.Value);
        AssertEqual("IPX-321 · 新标题", movie.Element("title")?.Value);
        AssertEqual("keep", movie.Element("title")?.Attribute("origin")?.Value);
        AssertEqual("2025-02-03", movie.Element("premiered")?.Value);
        AssertEqual("2025-02-03", movie.Element("releasedate")?.Value);
        AssertEqual("2025", movie.Element("year")?.Value);
        AssertEqual("2", movie.Elements("director").Count().ToString());
        AssertEqual("1", movie.Elements("director").First().Attribute("rank")?.Value);
        AssertEqual("2", movie.Elements("genre").Count().ToString());
        AssertEqual("999", movie.Elements("uniqueid").First(element =>
            element.Attribute("type")?.Value == "tmdb").Value);
        AssertEqual("IPX-321", movie.Elements("uniqueid").Single(element =>
            element.Attribute("type")?.Value == "javnumber").Value);
        AssertEqual("Custom tag", movie.Elements("tag").Single(element =>
            element.Attribute("custom")?.Value == "tag-keep").Value);
        AssertEqual("False", movie.Elements("tag").Any(element =>
            element.Value.StartsWith("Label:", StringComparison.OrdinalIgnoreCase)).ToString());
        AssertEqual("Series: Legacy Series", movie.Elements("tag").Single(element =>
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

        var legacyVideoPath = Path.Combine(root, "IPX-322.mp4");
        var legacyNfoPath = Path.Combine(root, "IPX-322.nfo");
        await File.WriteAllBytesAsync(legacyVideoPath, [0x49, 0x50, 0x59]);
        await File.WriteAllTextAsync(
            legacyNfoPath,
            "<movie><title>旧结构标题</title><id>IPX-322</id><uniqueid type=\"jav\" default=\"true\">ipx00322</uniqueid><unknown>keep</unknown></movie>");
        var legacyBundle = await NfoReader.ReadAsync(LocalSidecarLocator.Locate(legacyVideoPath));
        var legacyEditable = LocalMetadataReviewComposer.CreateLocal(legacyBundle.Metadata).Metadata;
        var migratedDocument = NfoRoundTripWriter.CreateUpdatedDocument(
            legacyBundle,
            legacyEditable,
            updatePosterReference: false,
            posterFileName: null,
            updateFanartReference: false,
            fanartFileName: null,
            includeIdInTitle: true);
        AssertEqual("IPX-322 · 旧结构标题", migratedDocument.Root?.Element("title")?.Value);
        var migratedCanonicalId = migratedDocument.Root?.Elements("uniqueid").Single(element =>
            element.Attribute("type")?.Value == "javnumber");
        AssertEqual("IPX-322", migratedCanonicalId?.Value);
        AssertEqual("true", migratedCanonicalId?.Attribute("default")?.Value);
        var retainedProviderId = migratedDocument.Root?.Elements("uniqueid").Single(element =>
            element.Attribute("type")?.Value == "jav");
        AssertEqual("ipx00322", retainedProviderId?.Value);
        AssertEqual(null, retainedProviderId?.Attribute("default")?.Value);
        AssertEqual("keep", migratedDocument.Root?.Element("unknown")?.Value);
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
    AssertEqual(string.Empty, composition.Metadata.Series);
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

    public static FakeMetadataProvider WaitForCancellation(string name, string displayName) =>
        new(name, displayName, async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("等待取消的测试来源意外完成。 ");
        });

    public Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return search(rawId, cancellationToken);
    }

    public void Dispose()
    {
    }
}

internal sealed class TrackingMetadataProvider(
    string name,
    string displayName,
    Func<string, CancellationToken, Task<MovieMetadata>> search) : IMetadataProvider
{
    private int _activeCalls;
    private int _callCount;
    private int _maxConcurrentCalls;

    public string Name { get; } = name;

    public string DisplayName { get; } = displayName;

    public int CallCount => _callCount;

    public int MaxConcurrentCalls => _maxConcurrentCalls;

    public async Task<MovieMetadata> SearchAsync(
        string rawId,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var activeCalls = Interlocked.Increment(ref _activeCalls);
        var observedMaximum = _maxConcurrentCalls;
        while (activeCalls > observedMaximum)
        {
            var previous = Interlocked.CompareExchange(
                ref _maxConcurrentCalls,
                activeCalls,
                observedMaximum);
            if (previous == observedMaximum)
            {
                break;
            }

            observedMaximum = previous;
        }

        try
        {
            return await search(rawId, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }

    public void Dispose()
    {
    }
}
