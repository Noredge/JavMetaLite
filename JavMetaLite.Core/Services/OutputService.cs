using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class OutputService : IDisposable
{
    private const int ScreenshotDownloadBatchSize = 3;
    private const int MaximumOnlineScreenshots = 50;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public OutputService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) JavMetaLite/0.4");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*", 0.9));
    }

    public async Task<SaveResult> SaveAsync(
        string videoPath,
        MovieMetadata metadata,
        SaveOptions options,
        CancellationToken cancellationToken = default)
    {
        return await SaveAsync(videoPath, videoPath, metadata, options, null, cancellationToken);
    }

    public async Task<SaveResult> SaveAsync(
        string sourceVideoPath,
        string outputVideoPath,
        MovieMetadata metadata,
        SaveOptions options,
        CancellationToken cancellationToken = default)
    {
        return await SaveAsync(
            sourceVideoPath,
            outputVideoPath,
            metadata,
            options,
            null,
            cancellationToken);
    }

    public async Task<SaveResult> SaveAsync(
        string sourceVideoPath,
        string outputVideoPath,
        MovieMetadata metadata,
        SaveOptions options,
        NfoWriteContext? nfoWriteContext,
        CancellationToken cancellationToken = default)
    {
        return await SaveAsync(
            sourceVideoPath,
            outputVideoPath,
            metadata,
            options,
            nfoWriteContext,
            OutputNamingMode.VideoBase,
            cancellationToken);
    }

    public async Task<SaveResult> SaveAsync(
        string sourceVideoPath,
        string outputVideoPath,
        MovieMetadata metadata,
        SaveOptions options,
        NfoWriteContext? nfoWriteContext,
        OutputNamingMode namingMode,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? extrafanartSourceLocations = null,
        OutputFileNames? outputFileNames = null)
    {
        if (!File.Exists(sourceVideoPath))
        {
            throw new FileNotFoundException("找不到所选影片。", sourceVideoPath);
        }

        if (!options.WriteNfo && !options.DownloadPoster && !options.DownloadFanart && !options.DownloadExtrafanart)
        {
            throw new InvalidOperationException("请至少选择一种输出：NFO、海报、fanart 或全部剧照。 ");
        }

        var directory = Path.GetDirectoryName(outputVideoPath)!;
        var baseName = Path.GetFileNameWithoutExtension(outputVideoPath);
        var names = outputFileNames ?? OutputFileNames.Create(baseName, namingMode);
        var nfoPath = options.WriteNfo
            ? names.Resolve(directory, names.Nfo)
            : null;
        var posterPath = options.DownloadPoster
            ? names.Resolve(directory, names.Poster)
            : null;
        var fanartPath = options.DownloadFanart
            ? names.Resolve(directory, names.Fanart)
            : null;

        using var timing = new SaveTimingLog("metadata", metadata.Id);
        timing.Begin("coverRead");
        DownloadedImage? cover = null;
        if (options.DownloadPoster || options.DownloadFanart)
        {
            AppLog.Info($"读取封面 id={metadata.Id} source={metadata.SourceDisplayName}");
            cover = await DownloadBestCoverAsync(metadata, cancellationToken);
        }

        timing.Begin("samplesRead");
        var screenshots = options.DownloadExtrafanart
            ? await DownloadScreenshotsAsync(
                extrafanartSourceLocations ?? metadata.ScreenshotUrls,
                cancellationToken,
                requireCompleteSelection: options.ReplaceLocalExtrafanart)
            : [];
        timing.Begin("prepareOutputs");
        var fanartImage = options.DownloadFanart ? cover : null;

        var extraImages = options.DownloadExtrafanart
            ? screenshots.ToArray()
            : [];
        if (options.DownloadExtrafanart && extraImages.Length == 0)
        {
            AppLog.Info($"未找到可保存的 Sample Images，已跳过 extrafanart id={metadata.Id}");
        }

        var extraDirectory = Path.Combine(directory, "extrafanart");
        var extraPaths = extraImages
            .Select((_, index) => Path.Combine(extraDirectory, $"fanart{index + 1}.jpg"))
            .ToArray();
        EnsureNoConflicts(
            new[] { nfoPath, posterPath, fanartPath }.Where(path => path is not null).Select(path => path!).Concat(extraPaths),
            options.OverwriteExisting);

        if (posterPath is not null && cover is not null)
        {
            await ProcessAndWriteAsync(posterPath, cover, poster: true);
        }

        if (fanartPath is not null && fanartImage is not null)
        {
            await ProcessAndWriteAsync(fanartPath, fanartImage, poster: false);
        }

        for (var index = 0; index < extraImages.Length; index++)
        {
            await ProcessAndWriteAsync(extraPaths[index], extraImages[index], poster: false);
        }

        timing.Begin("nfoWrite");
        if (nfoPath is not null)
        {
            var posterReference = nfoWriteContext?.UpdatePosterReference == true
                ? nfoWriteContext.PosterFileName
                : posterPath is null ? null : Path.GetFileName(posterPath);
            var fanartReference = nfoWriteContext?.UpdateFanartReference == true
                ? nfoWriteContext.FanartFileName
                : fanartPath is null ? null : Path.GetFileName(fanartPath);
            if (nfoWriteContext?.LocalBundle is not null)
            {
                await NfoRoundTripWriter.WriteAsync(
                    nfoPath,
                    nfoWriteContext.LocalBundle,
                    metadata,
                    nfoWriteContext.UpdatePosterReference,
                    posterReference,
                    nfoWriteContext.UpdateFanartReference,
                    fanartReference,
                    options.IncludeIdInTitle,
                    options.OverwriteExisting,
                    cancellationToken);
            }
            else
            {
                await NfoWriter.WriteAsync(
                    nfoPath,
                    metadata,
                    posterReference,
                    fanartReference,
                    options.IncludeIdInTitle,
                    options.OverwriteExisting,
                    cancellationToken);
            }
        }

        AppLog.Info(
            $"metadata 临时写入完成 base={baseName} nfo={nfoPath is not null} poster={posterPath is not null} " +
            $"fanart={fanartPath is not null} extrafanart={extraPaths.Length}");
        timing.Complete();
        return new SaveResult(nfoPath, posterPath, fanartPath, extraPaths, options.DownloadFanart && cover is not null);

        async Task ProcessAndWriteAsync(string path, DownloadedImage image, bool poster)
        {
            timing.Begin("imageProcess");
            var png = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);
            var bytes = poster
                ? PosterImageProcessor.CreatePoster(image.Bytes, png)
                : png ? PosterImageProcessor.CreateFanartPng(image.Bytes)
                    : PosterImageProcessor.CreateFanartJpeg(image.Bytes);
            timing.Begin("imageWrite");
            await WriteImageAsync(path, bytes, options.OverwriteExisting, cancellationToken);
        }
    }

    public static IReadOnlyList<string> GetExpectedOutputFiles(
        string outputVideoPath,
        MovieMetadata metadata,
        SaveOptions options) =>
        GetExpectedOutputFiles(outputVideoPath, metadata, options, OutputNamingMode.VideoBase);

    public static IReadOnlyList<string> GetExpectedOutputFiles(
        string outputVideoPath,
        MovieMetadata metadata,
        SaveOptions options,
        OutputNamingMode namingMode,
        int? extrafanartCountOverride = null,
        OutputFileNames? outputFileNames = null)
    {
        var directory = Path.GetDirectoryName(outputVideoPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        var baseName = Path.GetFileNameWithoutExtension(outputVideoPath);
        var candidates = new List<string>();
        var names = outputFileNames ?? OutputFileNames.Create(baseName, namingMode);
        if (options.WriteNfo)
        {
            candidates.Add(names.Resolve(directory, names.Nfo));
        }
        if (options.DownloadPoster)
        {
            candidates.Add(names.Resolve(directory, names.Poster));
        }
        if (options.DownloadFanart)
        {
            candidates.Add(names.Resolve(directory, names.Fanart));
        }
        if (options.DownloadExtrafanart)
        {
            var count = extrafanartCountOverride ?? SelectScreenshotLocations(metadata.ScreenshotUrls).Count;
            for (var index = 1; index <= count; index++)
            {
                candidates.Add(Path.Combine(directory, "extrafanart", $"fanart{index}.jpg"));
            }
        }

        return candidates;
    }

    public static IReadOnlyList<string> FindExistingOutputFiles(
        string videoPath,
        MovieMetadata metadata,
        SaveOptions options)
    {
        return GetExpectedOutputFiles(videoPath, metadata, options)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<DownloadedImage> DownloadBestCoverAsync(MovieMetadata metadata, CancellationToken cancellationToken)
    {
        var candidates = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("没有有效的封面链接。你可以取消“下载高清海报”，或手动填写封面链接。 ");
        }

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                return await DownloadImageAsync(candidate, cancellationToken);
            }
            catch (Exception exception) when (IsRecoverableImageError(exception))
            {
                AppLog.Warning($"封面下载候选失败：{candidate}", exception);
                lastError = exception;
            }
        }

        throw new InvalidDataException("所有封面地址都下载失败，可能需要浏览器验证。", lastError);
    }

    private async Task<List<DownloadedImage>> DownloadScreenshotsAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken,
        bool requireCompleteSelection = false)
    {
        var images = new List<DownloadedImage>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var failures = 0;
        var locations = SelectScreenshotLocations(urls);
        // Local-only saves keep their sequential disk access. Bound remote work by a small
        // batch, not one task per sample or per movie; duplicates retain at most one batch
        // of extra bytes until they can be discarded in the user's original order.
        var batchSize = locations.All(location => ArtworkLocationHelper.TryGetLocalPath(location, out _))
            ? 1 : ScreenshotDownloadBatchSize;
        using var downloads = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        for (var offset = 0; offset < locations.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tasks = locations.Skip(offset).Take(batchSize).Select(DownloadSampleAsync).ToArray();
            var batch = await Task.WhenAll(tasks);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var image in batch)
            {
                if (image is null)
                    failures++;
                else if (hashes.Add(image.Hash))
                {
                    images.Add(image);
                }
            }
        }

        // An empty user selection is intentional; a failed download is not. Abort
        // before staging writes so the transaction cannot retire irreplaceable originals.
        if (requireCompleteSelection && failures > 0)
        {
            throw new InvalidDataException(
                $"有 {failures} 张所选样张未能下载，已停止全面替换。本地图片和影片保持不变，请重试或调整选择。");
        }
        return images;

        async Task<DownloadedImage?> DownloadSampleAsync(string url)
        {
            try
            {
                downloads.Token.ThrowIfCancellationRequested();
                return await DownloadImageAsync(url, downloads.Token);
            }
            catch (Exception exception) when (
                !ArtworkLocationHelper.TryGetLocalPath(url, out _) && IsRecoverableImageError(exception))
            {
                // Missing online candidates may be skipped. A failed local read must
                // abort, because committing a partial list would retire its original.
                AppLog.Warning($"样张下载失败，继续处理其余图片：{url}", exception);
                return null;
            }
            catch
            {
                // Cancel siblings immediately on cancellation or a non-recoverable failure.
                // WhenAll still drains the batch before the caller can roll back staging.
                downloads.Cancel();
                throw;
            }
        }
    }

    internal static IReadOnlyList<string> SelectScreenshotLocations(IEnumerable<string> locations)
    {
        var selected = new List<string>();
        var onlineCount = 0;
        foreach (var location in locations)
        {
            // The download limit must not drop local files that the save transaction
            // will reconcile. Continue scanning after the limit for later local items.
            if (ArtworkLocationHelper.TryGetLocalPath(location, out _) || onlineCount++ < MaximumOnlineScreenshots)
                selected.Add(location);
        }
        return selected;
    }

    private async Task<DownloadedImage> DownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        if (ArtworkLocationHelper.TryGetLocalPath(url, out var localPath))
        {
            var bytes = await ArtworkLocationHelper.ReadLocalImageAsync(localPath, cancellationToken);
            var dimensions = PosterImageProcessor.GetDimensions(bytes);
            return new DownloadedImage(
                localPath,
                bytes,
                dimensions.Width,
                dimensions.Height,
                Convert.ToHexString(SHA256.HashData(bytes)));
        }

        Exception? lastError = null;
        foreach (var candidate in DmmImageUrlHelper.GetDownloadCandidates(url).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                    (uri.Host.EndsWith(".dmm.co.jp", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(".dmm.com", StringComparison.OrdinalIgnoreCase)))
                {
                    request.Headers.Referrer = new Uri("https://www.dmm.co.jp/");
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType) &&
                    !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("图片地址没有返回图片。 ");
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length < 128)
                {
                    throw new InvalidDataException("下载到的图片太小，可能是网站错误页面。 ");
                }

                var dimensions = PosterImageProcessor.GetDimensions(bytes);
                return new DownloadedImage(
                    candidate,
                    bytes,
                    dimensions.Width,
                    dimensions.Height,
                    Convert.ToHexString(SHA256.HashData(bytes)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableImageError(exception))
            {
                lastError = exception;
            }
        }

        throw new InvalidDataException($"图片下载失败：{url}", lastError);
    }

    private static bool IsRecoverableImageError(Exception exception) =>
        exception is HttpRequestException or InvalidDataException or NotSupportedException or FormatException or IOException;

    private static void EnsureNoConflicts(IEnumerable<string> paths, bool overwrite)
    {
        if (overwrite)
        {
            return;
        }

        var conflicts = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (conflicts.Length > 0)
        {
            throw new IOException(
                $"以下文件已经存在：\n{string.Join(Environment.NewLine, conflicts)}\n\n请重新保存并在安全预览中确认覆盖。 ");
        }
    }

    private static async Task WriteImageAsync(
        string destinationPath,
        byte[] imageBytes,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, imageBytes, cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record DownloadedImage(string Url, byte[] Bytes, int Width, int Height, string Hash);

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
