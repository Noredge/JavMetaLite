using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class OutputService : IDisposable
{
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
        var nfoPath = options.WriteNfo ? Path.Combine(directory, $"{baseName}.nfo") : null;
        var posterPath = options.DownloadPoster ? Path.Combine(directory, $"{baseName}-poster.jpg") : null;
        var fanartPath = options.DownloadFanart ? Path.Combine(directory, $"{baseName}-fanart.jpg") : null;

        DownloadedImage? cover = null;
        if (options.DownloadPoster || options.DownloadFanart)
        {
            AppLog.Info($"读取封面 id={metadata.Id} source={metadata.SourceDisplayName}");
            cover = await DownloadBestCoverAsync(metadata, cancellationToken);
        }

        var screenshots = options.DownloadExtrafanart
            ? await DownloadScreenshotsAsync(metadata.ScreenshotUrls, cancellationToken)
            : [];
        var fanartImage = options.DownloadFanart ? cover : null;

        var extraImages = options.DownloadExtrafanart
            ? screenshots.ToArray()
            : [];
        if (options.DownloadExtrafanart && extraImages.Length == 0 &&
            !options.WriteNfo && !options.DownloadPoster && !options.DownloadFanart)
        {
            throw new InvalidOperationException("没有找到可保存的 Sample Images。 ");
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
            await WriteImageAsync(
                posterPath,
                PosterImageProcessor.CreatePosterJpeg(cover.Bytes),
                options.OverwriteExisting,
                cancellationToken);
        }

        if (fanartPath is not null && fanartImage is not null)
        {
            await WriteImageAsync(
                fanartPath,
                PosterImageProcessor.CreateFanartJpeg(fanartImage.Bytes),
                options.OverwriteExisting,
                cancellationToken);
        }

        for (var index = 0; index < extraImages.Length; index++)
        {
            await WriteImageAsync(
                extraPaths[index],
                PosterImageProcessor.CreateFanartJpeg(extraImages[index].Bytes),
                options.OverwriteExisting,
                cancellationToken);
        }

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
                    options.OverwriteExisting,
                    cancellationToken);
            }
        }

        AppLog.Info(
            $"metadata 临时写入完成 base={baseName} nfo={nfoPath is not null} poster={posterPath is not null} " +
            $"fanart={fanartPath is not null} extrafanart={extraPaths.Length}");
        return new SaveResult(nfoPath, posterPath, fanartPath, extraPaths, options.DownloadFanart && cover is not null);
    }

    public static IReadOnlyList<string> GetExpectedOutputFiles(
        string outputVideoPath,
        MovieMetadata metadata,
        SaveOptions options)
    {
        var directory = Path.GetDirectoryName(outputVideoPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        var baseName = Path.GetFileNameWithoutExtension(outputVideoPath);
        var candidates = new List<string>();
        if (options.WriteNfo)
        {
            candidates.Add(Path.Combine(directory, $"{baseName}.nfo"));
        }
        if (options.DownloadPoster)
        {
            candidates.Add(Path.Combine(directory, $"{baseName}-poster.jpg"));
        }
        if (options.DownloadFanart)
        {
            candidates.Add(Path.Combine(directory, $"{baseName}-fanart.jpg"));
        }
        if (options.DownloadExtrafanart)
        {
            var count = Math.Min(50, metadata.ScreenshotUrls.Count);
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
        CancellationToken cancellationToken)
    {
        var images = new List<DownloadedImage>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in urls.Take(50))
        {
            try
            {
                var image = await DownloadImageAsync(url, cancellationToken);
                if (hashes.Add(image.Hash))
                {
                    images.Add(image);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableImageError(exception))
            {
                // Individual missing/blocked samples should not prevent the remaining images from being used.
                AppLog.Warning($"样张下载失败，继续处理其余图片：{url}", exception);
            }
        }

        return images;
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
                $"以下文件已经存在：\n{string.Join(Environment.NewLine, conflicts)}\n\n请勾选“直接保存并覆盖（跳过预览）”后重试。 ");
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
