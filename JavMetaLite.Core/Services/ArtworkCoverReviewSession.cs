using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class ArtworkCoverReviewSession
{
    private readonly MovieMetadata _editableMetadata;

    private ArtworkCoverReviewSession(MovieMetadata metadata, IEnumerable<MovieMetadata> sourceResults)
    {
        _editableMetadata = metadata;
        var sources = sourceResults.ToList();
        if (sources.Count == 0 &&
            (!string.IsNullOrWhiteSpace(metadata.SourceName) ||
             !string.IsNullOrWhiteSpace(metadata.SourceDisplayName)))
        {
            sources.Add(metadata);
        }

        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Candidates = sources
            .Select(source => new ArtworkCoverCandidate(
                MetadataCandidateSource.FromMetadata(source),
                source.CoverUrl,
                source.FallbackCoverUrl,
                source.PosterUrl))
            .Where(candidate => candidate.HasCover && sourceNames.Add(candidate.Source.Name))
            .ToArray();
        SelectedCandidate = FindInitialCandidate(metadata, Candidates) ?? Candidates.FirstOrDefault();
    }

    public IReadOnlyList<ArtworkCoverCandidate> Candidates { get; }

    public ArtworkCoverCandidate? SelectedCandidate { get; private set; }

    public static ArtworkCoverReviewSession Create(
        MovieMetadata metadata,
        params MovieMetadata[] sourceResults)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new ArtworkCoverReviewSession(metadata, sourceResults ?? []);
    }

    public bool SelectSource(string sourceName)
    {
        var candidate = Candidates.FirstOrDefault(item =>
            string.Equals(item.Source.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return false;
        }

        SelectedCandidate = candidate;
        _editableMetadata.CoverUrl = candidate.CoverUrl;
        _editableMetadata.FallbackCoverUrl = candidate.FallbackCoverUrl;
        _editableMetadata.PosterUrl = candidate.PosterUrl;
        return true;
    }

    private static ArtworkCoverCandidate? FindInitialCandidate(
        MovieMetadata metadata,
        IReadOnlyList<ArtworkCoverCandidate> candidates)
    {
        var selectedUrls = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(candidate => candidate.Urls.Any(selectedUrls.Contains));
    }
}
