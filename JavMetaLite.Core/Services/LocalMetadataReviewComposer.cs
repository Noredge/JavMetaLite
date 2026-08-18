using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class LocalMetadataReviewComposer
{
    public static LocalMetadataReviewComposition CreateLocal(MovieMetadata localMetadata)
    {
        ArgumentNullException.ThrowIfNull(localMetadata);

        var source = Clone(localMetadata);
        return new LocalMetadataReviewComposition(Clone(source), [source]);
    }

    public static LocalMetadataReviewComposition ComposeWithOnline(
        MovieMetadata localSource,
        MovieMetadata onlinePreferred,
        IEnumerable<MovieMetadata> onlineSources)
    {
        ArgumentNullException.ThrowIfNull(localSource);
        ArgumentNullException.ThrowIfNull(onlinePreferred);
        ArgumentNullException.ThrowIfNull(onlineSources);

        var local = Clone(localSource);
        var online = Clone(onlinePreferred);
        var metadata = MetadataMerger.Merge(online, local);

        // Actors are one selectable field. Do not silently union online actors into
        // the selected source before the user explicitly chooses another candidate.
        if (!string.IsNullOrWhiteSpace(online.ActorsText))
        {
            metadata.Actors = online.Actors;
        }
        else if (!string.IsNullOrWhiteSpace(local.ActorsText))
        {
            metadata.Actors = local.Actors;
        }

        var sources = new List<MovieMetadata>();
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in onlineSources)
        {
            if (source is null)
            {
                continue;
            }

            var snapshot = Clone(source);
            if (sourceNames.Add(GetSourceKey(snapshot)))
            {
                sources.Add(snapshot);
            }
        }

        if (sources.Count == 0)
        {
            sources.Add(online);
            sourceNames.Add(GetSourceKey(online));
        }

        if (sourceNames.Add(GetSourceKey(local)))
        {
            sources.Add(local);
        }

        return new LocalMetadataReviewComposition(metadata, sources);
    }

    private static MovieMetadata Clone(MovieMetadata source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        OriginalTitle = source.OriginalTitle,
        ReleaseDate = source.ReleaseDate,
        Director = source.Director,
        Maker = source.Maker,
        Label = source.Label,
        Series = source.Series,
        RuntimeMinutes = source.RuntimeMinutes,
        ActorsText = source.ActorsText,
        GenresText = source.GenresText,
        Plot = source.Plot,
        Rating = source.Rating,
        ContentId = source.ContentId,
        PosterUrl = source.PosterUrl,
        CoverUrl = source.CoverUrl,
        FallbackCoverUrl = source.FallbackCoverUrl,
        SourceUrl = source.SourceUrl,
        SourceName = source.SourceName,
        SourceDisplayName = source.SourceDisplayName,
        Actors = source.Actors.Select(actor => new ActorMetadata(actor.Name, actor.ImageUrl)).ToArray(),
        ScreenshotUrls = source.ScreenshotUrls.ToArray()
    };

    private static string GetSourceKey(MovieMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.SourceName)
            ? metadata.SourceDisplayName.Trim()
            : metadata.SourceName.Trim();
}
