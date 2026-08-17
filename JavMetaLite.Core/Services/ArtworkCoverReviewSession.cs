using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class ArtworkCoverReviewSession
{
    private readonly MovieMetadata _editableMetadata;

    private ArtworkCoverReviewSession(
        MovieMetadata metadata,
        IEnumerable<MovieMetadata> sourceResults,
        IEnumerable<ArtworkCoverCandidate> additionalCandidates,
        string? preferredSourceName)
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
        var candidates = additionalCandidates
            .Concat(sources
            .Select(source => new ArtworkCoverCandidate(
                MetadataCandidateSource.FromMetadata(source),
                source.CoverUrl,
                source.FallbackCoverUrl,
                source.PosterUrl)))
            .Where(candidate => candidate.HasCover && sourceNames.Add(candidate.Source.Name))
            .ToArray();
        Candidates = candidates;
        SelectedCandidate = !string.IsNullOrWhiteSpace(preferredSourceName)
            ? candidates.FirstOrDefault(candidate => string.Equals(
                candidate.Source.Name,
                preferredSourceName,
                StringComparison.OrdinalIgnoreCase))
            : null;
        SelectedCandidate ??= FindInitialCandidate(metadata, Candidates) ?? Candidates.FirstOrDefault();
        if (SelectedCandidate is not null)
        {
            ApplyCandidate(SelectedCandidate);
        }
    }

    public IReadOnlyList<ArtworkCoverCandidate> Candidates { get; }

    public ArtworkCoverCandidate? SelectedCandidate { get; private set; }

    public static ArtworkCoverReviewSession Create(
        MovieMetadata metadata,
        params MovieMetadata[] sourceResults)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new ArtworkCoverReviewSession(metadata, sourceResults ?? [], [], null);
    }

    public static ArtworkCoverReviewSession CreateWithAdditionalCandidates(
        MovieMetadata metadata,
        IEnumerable<ArtworkCoverCandidate> additionalCandidates,
        string? preferredSourceName,
        params MovieMetadata[] sourceResults)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(additionalCandidates);
        return new ArtworkCoverReviewSession(
            metadata,
            sourceResults ?? [],
            additionalCandidates,
            preferredSourceName);
    }

    public bool SelectSource(string sourceName)
    {
        var candidate = Candidates.FirstOrDefault(item =>
            string.Equals(item.Source.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return false;
        }

        ApplyCandidate(candidate);
        return true;
    }

    private void ApplyCandidate(ArtworkCoverCandidate candidate)
    {
        SelectedCandidate = candidate;
        if (candidate.IsSidecarPair)
        {
            _editableMetadata.CoverUrl = string.Empty;
            _editableMetadata.FallbackCoverUrl = string.Empty;
            _editableMetadata.PosterUrl = string.Empty;
            return;
        }

        _editableMetadata.CoverUrl = candidate.CoverUrl;
        _editableMetadata.FallbackCoverUrl = candidate.FallbackCoverUrl;
        _editableMetadata.PosterUrl = candidate.PosterUrl;
    }

    private static ArtworkCoverCandidate? FindInitialCandidate(
        MovieMetadata metadata,
        IReadOnlyList<ArtworkCoverCandidate> candidates)
    {
        var selectedUrls = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(candidate => candidate.Urls.Any(selectedUrls.Contains));
    }
}
