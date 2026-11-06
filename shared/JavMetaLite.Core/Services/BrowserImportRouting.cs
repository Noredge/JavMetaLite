using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class BrowserImportRouting
{
    public const string JavLibrary = "javlibrary";
    private const string R18NotFoundMessage = "Sorry, this page does not exist";

    private static readonly string[] OrderedSources =
    [
        MetadataSourcePreferenceProfile.LibreDmm,
        MetadataSourcePreferenceProfile.R18Dev,
        JavLibrary
    ];

    public static IReadOnlyList<BrowserImportTarget> BuildTargets(
        string rawId,
        IEnumerable<MovieMetadata> existingSources) =>
        OrderedSources
            .Select(source => BuildTarget(source, rawId, existingSources))
            .ToArray();

    public static BrowserImportTarget BuildTarget(
        string sourceName,
        string rawId,
        IEnumerable<MovieMetadata> existingSources)
    {
        ArgumentNullException.ThrowIfNull(existingSources);
        var normalizedSource = NormalizeSourceName(sourceName);
        if (!IsSupportedSource(normalizedSource))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceName), sourceName, "不支持该网页资料来源。 ");
        }

        var existing = existingSources.FirstOrDefault(source =>
            string.Equals(
                NormalizeSourceName(source.SourceName),
                normalizedSource,
                StringComparison.OrdinalIgnoreCase));
        var initialUrl = normalizedSource == MetadataSourcePreferenceProfile.R18Dev
            ? BuildR18BrowserUrl(existing)
            : BuildStandardBrowserUrl(normalizedSource, existing, rawId);
        return new BrowserImportTarget(
            normalizedSource,
            GetDisplayName(normalizedSource),
            initialUrl,
            existing is not null,
            MovieIdParser.Normalize(rawId));
    }

    public static bool RequiresProviderResolution(BrowserImportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return string.Equals(
                   NormalizeSourceName(target.SourceName),
                   MetadataSourcePreferenceProfile.R18Dev,
                   StringComparison.OrdinalIgnoreCase) &&
               !IsExpectedDetailPage(target.SourceName, target.InitialUrl);
    }

    public static async Task<BrowserImportTarget> ResolveWithProviderAsync(
        BrowserImportTarget target,
        IMetadataProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(provider);
        if (!RequiresProviderResolution(target))
        {
            return target;
        }

        if (!string.Equals(
                NormalizeSourceName(target.SourceName),
                NormalizeSourceName(provider.Name),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("网页目标与资料来源不一致。 ", nameof(provider));
        }

        var resolvedMetadata = await provider.SearchAsync(target.RequestedMovieId, cancellationToken);
        var resolvedTarget = BuildTarget(target.SourceName, target.RequestedMovieId, [resolvedMetadata]);
        if (RequiresProviderResolution(resolvedTarget))
        {
            throw new InvalidDataException($"{provider.DisplayName} 未返回可用的影片详情页标识。 ");
        }

        return resolvedTarget;
    }

    public static bool IsExpectedDetailPage(string sourceName, string? url)
    {
        var normalizedSource = NormalizeSourceName(sourceName);
        if (!IsExpectedHost(normalizedSource, url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (normalizedSource == MetadataSourcePreferenceProfile.LibreDmm)
        {
            return uri.AbsolutePath.Contains("/movies/", StringComparison.OrdinalIgnoreCase);
        }

        if (normalizedSource == MetadataSourcePreferenceProfile.R18Dev)
        {
            return IsR18DetailPath(uri) &&
                   !uri.AbsolutePath.EndsWith("/json", StringComparison.OrdinalIgnoreCase) &&
                   GetR18PathValue(uri, "id=") is not null;
        }

        return normalizedSource == JavLibrary;
    }

    public static string? TryExtractMovieId(string sourceName, string? url)
    {
        var normalizedSource = NormalizeSourceName(sourceName);
        if (!IsExpectedDetailPage(normalizedSource, url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (normalizedSource == MetadataSourcePreferenceProfile.LibreDmm)
        {
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            var moviesIndex = Array.FindIndex(segments, segment =>
                string.Equals(segment, "movies", StringComparison.OrdinalIgnoreCase));
            if (moviesIndex >= 0 && moviesIndex + 1 < segments.Length)
            {
                var value = Uri.UnescapeDataString(segments[moviesIndex + 1]);
                if (value.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    value = value[..^5];
                }

                return MovieIdParser.Normalize(value);
            }
        }

        if (normalizedSource == MetadataSourcePreferenceProfile.R18Dev)
        {
            const string marker = "dvd_id=";
            var markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                var value = uri.AbsolutePath[(markerIndex + marker.Length)..]
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                return MovieIdParser.Normalize(Uri.UnescapeDataString(value ?? string.Empty));
            }
        }

        return null;
    }

    public static string? BuildR18JsonUrl(string? detailPageUrl)
    {
        if (!IsExpectedDetailPage(MetadataSourcePreferenceProfile.R18Dev, detailPageUrl) ||
            !Uri.TryCreate(detailPageUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var contentId = GetR18PathValue(uri, "id=");
        return contentId is null
            ? null
            : $"https://r18.dev/videos/vod/movies/detail/-/combined={Uri.EscapeDataString(contentId)}/json";
    }

    public static BrowserImportPageState ClassifyR18Page(
        string? pageUrl,
        bool navigationSucceeded,
        int httpStatusCode,
        bool hasVideoInfo,
        bool hasSearchInput,
        bool hasNotFoundMessage)
    {
        if (!navigationSucceeded)
        {
            return BrowserImportPageState.Unavailable;
        }

        if (!IsExpectedHost(MetadataSourcePreferenceProfile.R18Dev, pageUrl))
        {
            return BrowserImportPageState.ManualSearchRequired;
        }

        if (httpStatusCode == 404)
        {
            return BrowserImportPageState.NotFound;
        }

        if (httpStatusCode is >= 400 and <= 599)
        {
            return BrowserImportPageState.Unavailable;
        }

        // R18.dev renders its own missing-page body with HTTP 200 in some cases.
        if (hasNotFoundMessage)
        {
            return BrowserImportPageState.NotFound;
        }

        if (IsExpectedDetailPage(MetadataSourcePreferenceProfile.R18Dev, pageUrl))
        {
            return hasVideoInfo
                ? BrowserImportPageState.Ready
                : BrowserImportPageState.Unavailable;
        }

        return hasSearchInput
            ? BrowserImportPageState.ManualSearchRequired
            : BrowserImportPageState.Unavailable;
    }

    public static bool ContainsR18NotFoundMessage(string? pageText) =>
        !string.IsNullOrWhiteSpace(pageText) &&
        pageText.Contains(R18NotFoundMessage, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedSource(string? sourceName)
    {
        var normalized = NormalizeSourceName(sourceName);
        return normalized is MetadataSourcePreferenceProfile.LibreDmm or
            MetadataSourcePreferenceProfile.R18Dev or JavLibrary;
    }

    public static string GetDisplayName(string sourceName) => NormalizeSourceName(sourceName) switch
    {
        MetadataSourcePreferenceProfile.LibreDmm => "LibreDMM",
        MetadataSourcePreferenceProfile.R18Dev => "R18.dev",
        JavLibrary => "JAVLibrary",
        _ => sourceName
    };

    private static string BuildFallbackUrl(string sourceName, string rawId) => sourceName switch
    {
        MetadataSourcePreferenceProfile.LibreDmm => LibreDmmClient.BuildDetailPageUrl(rawId),
        MetadataSourcePreferenceProfile.R18Dev => R18DevClient.HomePageUrl,
        JavLibrary => JavLibraryClient.BuildSearchUrl(rawId),
        _ => throw new ArgumentOutOfRangeException(nameof(sourceName), sourceName, null)
    };

    private static string BuildStandardBrowserUrl(
        string sourceName,
        MovieMetadata? existing,
        string rawId)
    {
        var existingUrl = existing?.SourceUrl?.Trim() ?? string.Empty;
        return IsExpectedDetailPage(sourceName, existingUrl)
            ? existingUrl
            : BuildFallbackUrl(sourceName, rawId);
    }

    private static string BuildR18BrowserUrl(MovieMetadata? existing)
    {
        var existingUrl = existing?.SourceUrl?.Trim() ?? string.Empty;
        if (IsExpectedDetailPage(MetadataSourcePreferenceProfile.R18Dev, existingUrl))
        {
            return existingUrl;
        }

        var contentId = SanitizeR18ContentId(existing?.ContentId);
        if (contentId is null &&
            Uri.TryCreate(existingUrl, UriKind.Absolute, out var existingUri) &&
            IsExpectedHost(MetadataSourcePreferenceProfile.R18Dev, existingUrl))
        {
            contentId = GetR18PathValue(existingUri, "combined=");
        }

        // A catalog number cannot be converted reliably into every DMM content_id
        // (for example FNS-121 is 1fns121, not fns00121). Only a provider result is
        // authoritative enough for a direct detail-page route.
        return contentId is null
            ? R18DevClient.HomePageUrl
            : R18DevClient.BuildDetailPageUrlFromContentId(contentId);
    }

    private static bool IsR18DetailPath(Uri uri) =>
        uri.AbsolutePath.Contains("/videos/vod/movies/detail/", StringComparison.OrdinalIgnoreCase);

    private static string? GetR18PathValue(Uri uri, string marker)
    {
        var segment = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(marker, StringComparison.OrdinalIgnoreCase));
        return segment is null
            ? null
            : SanitizeR18ContentId(Uri.UnescapeDataString(segment[marker.Length..]));
    }

    private static string? SanitizeR18ContentId(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 0 && trimmed.All(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-')
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static bool IsExpectedHost(string sourceName, string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return sourceName switch
        {
            MetadataSourcePreferenceProfile.LibreDmm => HostMatches(uri.Host, "libredmm.com"),
            MetadataSourcePreferenceProfile.R18Dev => HostMatches(uri.Host, "r18.dev"),
            JavLibrary => HostMatches(uri.Host, "javlibrary.com"),
            _ => false
        };
    }

    private static bool HostMatches(string host, string expectedHost) =>
        string.Equals(host, expectedHost, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{expectedHost}", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSourceName(string? sourceName) =>
        sourceName?.Trim().ToLowerInvariant() ?? string.Empty;
}
