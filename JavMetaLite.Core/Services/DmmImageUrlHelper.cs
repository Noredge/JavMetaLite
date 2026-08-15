namespace JavMetaLite.Core.Services;

public static class DmmImageUrlHelper
{
    public static string NormalizeScreenshotUrl(string? rawUrl)
    {
        var raw = rawUrl?.Trim() ?? string.Empty;
        if (raw.StartsWith("//", StringComparison.Ordinal))
        {
            raw = "https:" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !IsDmmHost(uri.Host))
        {
            return raw;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1,
            Query = string.Empty,
            Fragment = string.Empty
        };
        var fileName = Path.GetFileName(builder.Path);
        if (fileName.Contains('-', StringComparison.Ordinal) &&
            !fileName.Contains("jp-", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith("pl.jpg", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith("ps.jpg", StringComparison.OrdinalIgnoreCase))
        {
            var highResolutionName = fileName.Replace("-", "jp-", StringComparison.Ordinal);
            builder.Path = builder.Path[..^fileName.Length] + highResolutionName;
        }

        return builder.Uri.AbsoluteUri;
    }

    public static IEnumerable<string> GetDownloadCandidates(string? url)
    {
        var normalized = NormalizeScreenshotUrl(url);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            yield return normalized;
        }

        if (normalized.Contains("jp-", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = normalized.Replace("jp-", "-", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(fallback, normalized, StringComparison.OrdinalIgnoreCase))
            {
                yield return fallback;
            }
        }
    }

    private static bool IsDmmHost(string host) =>
        host.Equals("dmm.co.jp", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".dmm.co.jp", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("dmm.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".dmm.com", StringComparison.OrdinalIgnoreCase);
}
