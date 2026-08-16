using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed record MultiSourceSearchResult(
    MovieMetadata Metadata,
    IReadOnlyList<MovieMetadata> Sources,
    IReadOnlyList<MetadataSourceSearchAttempt> Attempts);
