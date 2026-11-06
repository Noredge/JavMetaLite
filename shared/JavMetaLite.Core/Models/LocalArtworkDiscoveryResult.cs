using JavMetaLite.Core.Services;

namespace JavMetaLite.Core.Models;

public sealed record LocalArtworkDiscoveryResult(
    ArtworkCoverCandidate? Candidate,
    IReadOnlyList<string> Diagnostics)
{
    public IReadOnlyList<string> ExtrafanartPaths { get; init; } = [];
    public CoverResolutionCheck? FanartResolution { get; init; }
}
