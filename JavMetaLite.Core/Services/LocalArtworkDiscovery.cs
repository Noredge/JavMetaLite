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
        var fanartPath = await ValidateAsync("fanart", sidecars.FanartPath, diagnostics, cancellationToken);
        ArtworkCoverCandidate? candidate = null;
        if (posterPath is not null || fanartPath is not null)
        {
            var directory = Path.GetDirectoryName(sidecars.VideoPath) ?? string.Empty;
            candidate = ArtworkCoverCandidate.CreateSidecarPair(
                new MetadataCandidateSource("local-images", "本地图片", directory),
                posterPath,
                fanartPath);
        }

        return new LocalArtworkDiscoveryResult(candidate, diagnostics.ToArray());
    }

    private static async Task<string?> ValidateAsync(
        string role,
        string? path,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            _ = await ArtworkLocationHelper.ReadLocalImageAsync(path, cancellationToken);
            return Path.GetFullPath(path);
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
