using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed record MetadataSourceSearchAttempt(
    string SourceName,
    string SourceDisplayName,
    TimeSpan Elapsed,
    MovieMetadata? Metadata,
    Exception? Error,
    int CandidateFieldCount)
{
    public bool Success => Metadata is not null;
}
