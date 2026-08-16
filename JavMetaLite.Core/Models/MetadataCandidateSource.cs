namespace JavMetaLite.Core.Models;

public sealed record MetadataCandidateSource(
    string Name,
    string DisplayName,
    string Url,
    bool IsManual = false)
{
    public static MetadataCandidateSource Manual { get; } =
        new("manual", "手动编辑", string.Empty, true);

    public static MetadataCandidateSource FromMetadata(MovieMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var name = FirstNonEmpty(metadata.SourceName, metadata.SourceDisplayName, "unknown");
        var displayName = FirstNonEmpty(metadata.SourceDisplayName, metadata.SourceName, "未知来源");
        return new MetadataCandidateSource(name, displayName, metadata.SourceUrl.Trim());
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
