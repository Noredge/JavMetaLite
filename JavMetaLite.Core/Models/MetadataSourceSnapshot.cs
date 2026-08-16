using System.Collections.ObjectModel;

namespace JavMetaLite.Core.Models;

public sealed class MetadataSourceSnapshot
{
    private MetadataSourceSnapshot(
        MetadataCandidateSource source,
        IReadOnlyDictionary<MetadataField, string> values,
        IReadOnlyList<ActorMetadata> actors)
    {
        Source = source;
        Values = values;
        Actors = actors;
    }

    public MetadataCandidateSource Source { get; }

    public IReadOnlyDictionary<MetadataField, string> Values { get; }

    public IReadOnlyList<ActorMetadata> Actors { get; }

    public string GetValue(MetadataField field) =>
        Values.TryGetValue(field, out var value) ? value : string.Empty;

    public static MetadataSourceSnapshot FromMetadata(MovieMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var values = new Dictionary<MetadataField, string>
        {
            [MetadataField.Title] = metadata.Title,
            [MetadataField.OriginalTitle] = metadata.OriginalTitle,
            [MetadataField.ReleaseDate] = metadata.ReleaseDate,
            [MetadataField.RuntimeMinutes] = metadata.RuntimeMinutes,
            [MetadataField.Maker] = metadata.Maker,
            [MetadataField.Director] = metadata.Director,
            [MetadataField.Label] = metadata.Label,
            [MetadataField.Series] = metadata.Series,
            [MetadataField.Actors] = metadata.ActorsText,
            [MetadataField.Genres] = metadata.GenresText,
            [MetadataField.Plot] = metadata.Plot,
            [MetadataField.Rating] = metadata.Rating
        };
        var actors = metadata.Actors
            .Select(actor => new ActorMetadata(actor.Name, actor.ImageUrl))
            .ToArray();

        return new MetadataSourceSnapshot(
            MetadataCandidateSource.FromMetadata(metadata),
            new ReadOnlyDictionary<MetadataField, string>(values),
            actors);
    }
}
