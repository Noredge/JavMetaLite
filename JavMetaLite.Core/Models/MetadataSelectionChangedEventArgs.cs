namespace JavMetaLite.Core.Models;

public sealed class MetadataSelectionChangedEventArgs(
    MetadataField field,
    MetadataFieldCandidate candidate) : EventArgs
{
    public MetadataField Field { get; } = field;

    public MetadataFieldCandidate Candidate { get; } = candidate;
}
