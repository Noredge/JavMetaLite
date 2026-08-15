using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class MetadataMerger
{
    public static bool NeedsFallback(MovieMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.Title) ||
        string.IsNullOrWhiteSpace(metadata.ReleaseDate) ||
        string.IsNullOrWhiteSpace(metadata.RuntimeMinutes) ||
        string.IsNullOrWhiteSpace(metadata.Director) ||
        string.IsNullOrWhiteSpace(metadata.Maker) ||
        string.IsNullOrWhiteSpace(metadata.Label) ||
        string.IsNullOrWhiteSpace(metadata.Series) ||
        string.IsNullOrWhiteSpace(metadata.ActorsText) ||
        string.IsNullOrWhiteSpace(metadata.GenresText) ||
        string.IsNullOrWhiteSpace(metadata.Plot) ||
        string.IsNullOrWhiteSpace(metadata.CoverUrl) ||
        metadata.ScreenshotUrls.Count == 0;

    public static MovieMetadata Merge(MovieMetadata primary, MovieMetadata fallback)
    {
        var primaryId = MovieIdParser.Normalize(primary.Id);
        var fallbackId = MovieIdParser.Normalize(fallback.Id);
        if (!string.IsNullOrWhiteSpace(primaryId) && !string.IsNullOrWhiteSpace(fallbackId) &&
            !string.Equals(primaryId, fallbackId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"资料来源番号不一致：{primaryId} / {fallbackId}。 ");
        }

        var fallbackUsed = false;
        string Pick(string first, string second)
        {
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }

            if (!string.IsNullOrWhiteSpace(second))
            {
                fallbackUsed = true;
            }
            return second;
        }

        var screenshots = primary.ScreenshotUrls;
        if (screenshots.Count == 0 && fallback.ScreenshotUrls.Count > 0)
        {
            screenshots = fallback.ScreenshotUrls;
            fallbackUsed = true;
        }

        var actors = primary.Actors.ToList();
        foreach (var actor in fallback.Actors)
        {
            var existingIndex = actors.FindIndex(item => string.Equals(item.Name, actor.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                actors.Add(actor);
                fallbackUsed = true;
            }
            else if (string.IsNullOrWhiteSpace(actors[existingIndex].ImageUrl) && !string.IsNullOrWhiteSpace(actor.ImageUrl))
            {
                actors[existingIndex] = actor;
                fallbackUsed = true;
            }
        }

        var result = new MovieMetadata
        {
            Id = Pick(primary.Id, fallback.Id),
            Title = Pick(primary.Title, fallback.Title),
            OriginalTitle = Pick(primary.OriginalTitle, fallback.OriginalTitle),
            ReleaseDate = Pick(primary.ReleaseDate, fallback.ReleaseDate),
            RuntimeMinutes = Pick(primary.RuntimeMinutes, fallback.RuntimeMinutes),
            Director = Pick(primary.Director, fallback.Director),
            Maker = Pick(primary.Maker, fallback.Maker),
            Label = Pick(primary.Label, fallback.Label),
            Series = Pick(primary.Series, fallback.Series),
            ActorsText = Pick(primary.ActorsText, fallback.ActorsText),
            Actors = actors,
            GenresText = Pick(primary.GenresText, fallback.GenresText),
            Plot = Pick(primary.Plot, fallback.Plot),
            Rating = Pick(primary.Rating, fallback.Rating),
            ContentId = Pick(primary.ContentId, fallback.ContentId),
            PosterUrl = Pick(primary.PosterUrl, fallback.PosterUrl),
            CoverUrl = Pick(primary.CoverUrl, fallback.CoverUrl),
            FallbackCoverUrl = Pick(primary.FallbackCoverUrl, fallback.FallbackCoverUrl),
            ScreenshotUrls = screenshots,
            SourceUrl = primary.SourceUrl,
            SourceName = primary.SourceName,
            SourceDisplayName = primary.SourceDisplayName
        };

        if (fallbackUsed)
        {
            var primaryName = FirstNonEmpty(primary.SourceDisplayName, primary.SourceName);
            var fallbackName = FirstNonEmpty(fallback.SourceDisplayName, fallback.SourceName);
            result.SourceDisplayName = $"{primaryName} + {fallbackName}";
        }

        return result;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
