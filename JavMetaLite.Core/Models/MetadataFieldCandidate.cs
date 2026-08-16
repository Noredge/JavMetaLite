namespace JavMetaLite.Core.Models;

public sealed record MetadataFieldCandidate(
    MetadataField Field,
    string Value,
    MetadataCandidateSource Source);
