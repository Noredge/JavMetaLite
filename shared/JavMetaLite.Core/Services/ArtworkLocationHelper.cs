namespace JavMetaLite.Core.Services;

public sealed record ValidatedLocalImage(byte[] Bytes, int Width, int Height);

public static class ArtworkLocationHelper
{
    public const long MaximumLocalImageBytes = 100L * 1024 * 1024;

    public static bool IsSupported(string? location) =>
        TryGetLocalPath(location, out _) || TryGetRemoteUri(location, out _);

    public static string Normalize(string? location)
    {
        if (TryGetLocalPath(location, out var localPath))
        {
            return localPath;
        }

        return TryGetRemoteUri(location, out var remoteUri)
            ? remoteUri.AbsoluteUri
            : string.Empty;
    }

    public static bool TryGetLocalPath(string? location, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        var value = location.Trim();
        try
        {
            if (Path.IsPathFullyQualified(value))
            {
                path = Path.GetFullPath(value);
                return true;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                path = Path.GetFullPath(uri.LocalPath);
                return true;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return false;
    }

    public static bool TryGetRemoteUri(string? location, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(location?.Trim(), UriKind.Absolute, out var candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    public static async Task<byte[]> ReadLocalImageAsync(
        string location,
        IArtworkImageProcessor imageProcessor,
        CancellationToken cancellationToken = default) =>
        (await ReadLocalImageWithDimensionsAsync(location, imageProcessor, cancellationToken)).Bytes;

    public static async Task<ValidatedLocalImage> ReadLocalImageWithDimensionsAsync(
        string location,
        IArtworkImageProcessor imageProcessor,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetLocalPath(location, out var path))
        {
            throw new InvalidDataException("不是有效的本地图片路径。");
        }

        FileSystemPathSemantics.ValidateUnambiguousPath(path);
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("本地图片不存在。", path);
        }

        if (fileInfo.Length == 0 || fileInfo.Length > MaximumLocalImageBytes)
        {
            throw new InvalidDataException(
                $"本地图片大小无效，最大允许 {MaximumLocalImageBytes} 字节。");
        }

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var bytes = await ReadBoundedImageAsync(input, cancellationToken);
        var dimensions = imageProcessor.GetDimensions(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        return new ValidatedLocalImage(bytes, dimensions.Width, dimensions.Height);
    }

    internal static async Task<byte[]> ReadBoundedImageAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumLocalImageBytes)
                throw new InvalidDataException("Image exceeds the encoded byte limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

}
