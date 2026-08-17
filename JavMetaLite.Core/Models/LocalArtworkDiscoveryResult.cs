namespace JavMetaLite.Core.Models;

public sealed record LocalArtworkDiscoveryResult(
    ArtworkCoverCandidate? Candidate,
    IReadOnlyList<string> Diagnostics);
