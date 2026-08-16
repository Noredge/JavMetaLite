using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class ArtworkReviewSession
{
    public const string CombinedScreenshotsName = "combined";

    private readonly ArtworkSelection _metadataFallback;

    private ArtworkReviewSession(MovieMetadata metadata, IEnumerable<MovieMetadata> sourceResults)
    {
        _metadataFallback = ArtworkSelection.FromMetadata(metadata);
        var sources = sourceResults.Where(source => source is not null).ToList();
        if (sources.Count == 0 &&
            (!string.IsNullOrWhiteSpace(metadata.SourceName) ||
             !string.IsNullOrWhiteSpace(metadata.SourceDisplayName)))
        {
            sources.Add(metadata);
        }

        var candidates = new List<ArtworkSourceCandidate>();
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceResult in sources)
        {
            var source = MetadataCandidateSource.FromMetadata(sourceResult);
            if (!sourceNames.Add(source.Name))
            {
                continue;
            }

            var coverUrls = ArtworkSelection.NormalizeUrls(
                [sourceResult.CoverUrl, sourceResult.FallbackCoverUrl, sourceResult.PosterUrl]);
            var screenshotUrls = ArtworkSelection.NormalizeUrls(sourceResult.ScreenshotUrls);
            if (coverUrls.Count > 0 || screenshotUrls.Count > 0)
            {
                candidates.Add(new ArtworkSourceCandidate(source, coverUrls, screenshotUrls));
            }
        }

        Sources = candidates.ToArray();
        CoverCandidates = Sources.Where(candidate => candidate.HasCover).ToArray();
        ScreenshotChoices = BuildScreenshotChoices(Sources);
        SelectedCoverCandidate = FindInitialCoverCandidate(metadata, CoverCandidates);
        SelectedScreenshotChoice = ScreenshotChoices.FirstOrDefault();
    }

    public IReadOnlyList<ArtworkSourceCandidate> Sources { get; }

    public IReadOnlyList<ArtworkSourceCandidate> CoverCandidates { get; }

    public IReadOnlyList<ArtworkScreenshotChoice> ScreenshotChoices { get; }

    public ArtworkSourceCandidate? SelectedCoverCandidate { get; private set; }

    public ArtworkScreenshotChoice? SelectedScreenshotChoice { get; private set; }

    public static ArtworkReviewSession Create(
        MovieMetadata metadata,
        params MovieMetadata[] sourceResults)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new ArtworkReviewSession(metadata, sourceResults ?? []);
    }

    public bool SelectCoverSource(string sourceName)
    {
        var candidate = CoverCandidates.FirstOrDefault(item =>
            string.Equals(item.Source.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return false;
        }

        SelectedCoverCandidate = candidate;
        return true;
    }

    public bool SelectScreenshotSource(string choiceName)
    {
        var choice = ScreenshotChoices.FirstOrDefault(item =>
            string.Equals(item.Name, choiceName, StringComparison.OrdinalIgnoreCase));
        if (choice is null)
        {
            return false;
        }

        SelectedScreenshotChoice = choice;
        return true;
    }

    public ArtworkSelection CreateSelection()
    {
        var cover = SelectedCoverCandidate;
        var screenshots = SelectedScreenshotChoice;
        return new ArtworkSelection(
            cover?.Source.Name ?? _metadataFallback.CoverSourceName,
            cover?.Source.DisplayName ?? _metadataFallback.CoverSourceDisplayName,
            cover?.CoverUrls ?? _metadataFallback.CoverUrls,
            screenshots?.Name ?? _metadataFallback.ScreenshotSourceName,
            screenshots?.DisplayName ?? _metadataFallback.ScreenshotSourceDisplayName,
            screenshots?.Urls ?? _metadataFallback.ScreenshotUrls);
    }

    private static ArtworkSourceCandidate? FindInitialCoverCandidate(
        MovieMetadata metadata,
        IReadOnlyList<ArtworkSourceCandidate> candidates)
    {
        var selectedUrls = ArtworkSelection.NormalizeUrls(
            [metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl]);
        return candidates.FirstOrDefault(candidate =>
                   candidate.CoverUrls.Any(url => selectedUrls.Contains(url, StringComparer.OrdinalIgnoreCase)))
               ?? candidates.FirstOrDefault();
    }

    private static IReadOnlyList<ArtworkScreenshotChoice> BuildScreenshotChoices(
        IReadOnlyList<ArtworkSourceCandidate> candidates)
    {
        var screenshotSources = candidates.Where(candidate => candidate.HasScreenshots).ToArray();
        if (screenshotSources.Length == 0)
        {
            return [];
        }

        var choices = new List<ArtworkScreenshotChoice>();
        if (screenshotSources.Length > 1)
        {
            choices.Add(new ArtworkScreenshotChoice(
                CombinedScreenshotsName,
                $"合并去重（{string.Join(" + ", screenshotSources.Select(item => item.Source.DisplayName))}）",
                ArtworkSelection.NormalizeUrls(screenshotSources.SelectMany(item => item.ScreenshotUrls)),
                true));
        }

        choices.AddRange(screenshotSources.Select(candidate => new ArtworkScreenshotChoice(
            candidate.Source.Name,
            candidate.Source.DisplayName,
            candidate.ScreenshotUrls)));
        return choices;
    }
}
