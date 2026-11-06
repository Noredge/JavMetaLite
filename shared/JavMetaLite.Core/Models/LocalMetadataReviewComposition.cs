namespace JavMetaLite.Core.Models;

public sealed record LocalMetadataReviewComposition(
    MovieMetadata Metadata,
    IReadOnlyList<MovieMetadata> Sources);
