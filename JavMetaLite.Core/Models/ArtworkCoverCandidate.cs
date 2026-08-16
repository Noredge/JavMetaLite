namespace JavMetaLite.Core.Models;

public sealed record ArtworkCoverCandidate(
    MetadataCandidateSource Source,
    string CoverUrl,
    string FallbackCoverUrl,
    string PosterUrl)
{
    public IReadOnlyList<string> Urls { get; } = new[] { CoverUrl, FallbackCoverUrl, PosterUrl }
        .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
        .Select(url => url.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool HasCover => Urls.Count > 0;
}
