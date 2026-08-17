namespace JavMetaLite.Core.Services;

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
        CancellationToken cancellationToken = default)
    {
        if (!TryGetLocalPath(location, out var path))
        {
            throw new InvalidDataException("不是有效的本地图片路径。");
        }

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

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        _ = PosterImageProcessor.GetDimensions(bytes);
        return bytes;
    }
}
