using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class LocalArtworkDiscovery
{
    public static async Task<LocalArtworkDiscoveryResult> DiscoverAsync(
        LocalSidecarPaths sidecars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sidecars);

        var diagnostics = new List<string>();
        var posterPath = await ValidateAsync("poster", sidecars.PosterPath, diagnostics, cancellationToken);
        CoverResolutionCheck? fanartResolution = null;
        var fanartPath = await ValidateAsync("fanart", sidecars.FanartPath, diagnostics, cancellationToken,
            measured => fanartResolution = measured);
        var extrafanartPaths = new List<string>();
        foreach (var path in sidecars.ExtrafanartPaths)
        {
            var validatedPath = await ValidateAsync("extrafanart", path, diagnostics, cancellationToken);
            if (validatedPath is not null)
            {
                extrafanartPaths.Add(validatedPath);
            }
        }

        ArtworkCoverCandidate? candidate = null;
        if (posterPath is not null || fanartPath is not null)
        {
            var directory = Path.GetDirectoryName(sidecars.VideoPath) ?? string.Empty;
            candidate = ArtworkCoverCandidate.CreateSidecarPair(
                new MetadataCandidateSource("local-images", "本地图片", directory),
                posterPath,
                fanartPath);
        }

        return new LocalArtworkDiscoveryResult(candidate, diagnostics.ToArray())
        {
            ExtrafanartPaths = extrafanartPaths,
            FanartResolution = fanartResolution
        };
    }

    private static async Task<string?> ValidateAsync(
        string role,
        string? path,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken,
        Action<CoverResolutionCheck>? measured = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var image = await ArtworkLocationHelper.ReadLocalImageWithDimensionsAsync(path, cancellationToken);
            var normalizedPath = Path.GetFullPath(path);
            measured?.Invoke(new(normalizedPath, image.Width, image.Height));
            return normalizedPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                NotSupportedException or FormatException)
        {
            diagnostics.Add($"本地 {role} 无效，已忽略：{Path.GetFileName(path)}；{exception.Message}");
            return null;
        }
    }
}
