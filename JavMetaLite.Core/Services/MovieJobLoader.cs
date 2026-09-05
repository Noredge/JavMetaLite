using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public enum LocalMetadataLoadStatus
{
    NoNfo,
    Loaded,
    SidecarInspectionFailed,
    NfoReadFailed
}

public sealed record MovieJobLoadResult(
    LocalMetadataLoadStatus MetadataStatus,
    LocalSidecarPaths? Sidecars,
    Exception? MetadataError,
    LocalArtworkDiscoveryResult? ArtworkDiscovery)
{
    public bool HasErrors =>
        MetadataStatus is LocalMetadataLoadStatus.SidecarInspectionFailed or LocalMetadataLoadStatus.NfoReadFailed ||
        ArtworkDiscovery?.Diagnostics.Count > 0;
}

public static class MovieJobLoader
{
    public static async Task<MovieJobLoadResult> LoadAsync(
        MovieJob job,
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        var fileSet = MovieFileSet.Discover(videoPath);
        return await LoadAsync(job, fileSet.VideoPaths, cancellationToken);
    }

    public static async Task<MovieJobLoadResult> LoadAsync(
        MovieJob job,
        IEnumerable<string> videoPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(videoPaths);
        var fileSet = MovieFileSet.Create(videoPaths);
        if (fileSet.VideoPaths.Any(path => !VideoFileSupport.IsSupportedExistingFile(path)))
        {
            throw new ArgumentException("影片路径包含不受支持或不存在的文件。", nameof(videoPaths));
        }

        var normalizedPath = fileSet.PrimaryPath;
        job.ResetForVideos(fileSet.VideoPaths, MovieIdParser.TryExtract(normalizedPath));

        LocalSidecarPaths sidecars;
        try
        {
            sidecars = LocalSidecarLocator.Locate(
                normalizedPath,
                fileSet.MovieBaseName,
                fileSet.UsesMultipartNaming);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new MovieJobLoadResult(
                LocalMetadataLoadStatus.SidecarInspectionFailed,
                null,
                exception,
                null);
        }

        var metadataStatus = LocalMetadataLoadStatus.NoNfo;
        Exception? metadataError = null;
        if (sidecars.HasNfo)
        {
            job.BeginLocalNfoRead();
            try
            {
                var bundle = await NfoReader.ReadAsync(sidecars, cancellationToken);
                var composition = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata);
                if (string.IsNullOrWhiteSpace(composition.Metadata.Id))
                {
                    composition.Metadata.Id = MovieIdParser.TryExtract(normalizedPath) ?? string.Empty;
                }

                job.ApplyLocalMetadata(bundle, composition);
                metadataStatus = LocalMetadataLoadStatus.Loaded;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                metadataStatus = LocalMetadataLoadStatus.NfoReadFailed;
                metadataError = exception;
            }
        }

        var artworkDiscovery = await LocalArtworkDiscovery.DiscoverAsync(sidecars, cancellationToken);
        job.SetLocalArtwork(artworkDiscovery.Candidate);
        job.SetLocalExtrafanart(artworkDiscovery.ExtrafanartPaths);
        if (artworkDiscovery.FanartResolution is { } resolution)
            job.TrySetCoverResolution(job.CoverResolutionRevision, resolution);
        return new MovieJobLoadResult(metadataStatus, sidecars, metadataError, artworkDiscovery);
    }
}
