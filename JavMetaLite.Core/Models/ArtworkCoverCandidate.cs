using JavMetaLite.Core.Services;

namespace JavMetaLite.Core.Models;

public enum ArtworkCoverCandidateKind
{
    CompleteCover,
    SidecarPair
}

public sealed record ArtworkCoverCandidate(
    MetadataCandidateSource Source,
    string CoverUrl,
    string FallbackCoverUrl,
    string PosterUrl)
{
    public ArtworkCoverCandidateKind Kind { get; init; } = ArtworkCoverCandidateKind.CompleteCover;

    public string LocalPosterPath { get; init; } = string.Empty;

    public string LocalFanartPath { get; init; } = string.Empty;

    public IReadOnlyList<string> FullCoverLocations => new[] { CoverUrl, FallbackCoverUrl, PosterUrl }
        .Where(ArtworkLocationHelper.IsSupported)
        .Select(ArtworkLocationHelper.Normalize)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<string> Urls => Kind == ArtworkCoverCandidateKind.SidecarPair
        ? new[] { LocalPosterPath, LocalFanartPath }
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : FullCoverLocations;

    public bool IsSidecarPair => Kind == ArtworkCoverCandidateKind.SidecarPair;

    public bool HasCover => Urls.Count > 0;

    public bool HasPoster => IsSidecarPair
        ? ArtworkLocationHelper.IsSupported(LocalPosterPath)
        : HasCover;

    public bool HasFanart => IsSidecarPair
        ? ArtworkLocationHelper.IsSupported(LocalFanartPath)
        : HasCover;

    public static ArtworkCoverCandidate CreateCompleteCover(
        MetadataCandidateSource source,
        string location) =>
        new(source, ArtworkLocationHelper.Normalize(location), string.Empty, string.Empty);

    public static ArtworkCoverCandidate CreateSidecarPair(
        MetadataCandidateSource source,
        string? posterPath,
        string? fanartPath) =>
        new(source, string.Empty, string.Empty, string.Empty)
        {
            Kind = ArtworkCoverCandidateKind.SidecarPair,
            LocalPosterPath = ArtworkLocationHelper.Normalize(posterPath),
            LocalFanartPath = ArtworkLocationHelper.Normalize(fanartPath)
        };
}
