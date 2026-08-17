using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace JavMetaLite.Core.Models;

public sealed class LocalMetadataBundle
{
    private readonly XDocument _originalDocument;

    internal LocalMetadataBundle(
        LocalSidecarPaths sidecars,
        MovieMetadata metadata,
        XDocument originalDocument,
        string originalNfoSha256,
        IEnumerable<string> diagnostics)
    {
        Sidecars = sidecars;
        Metadata = metadata;
        SourceSnapshot = MetadataSourceSnapshot.FromMetadata(metadata);
        _originalDocument = new XDocument(originalDocument);
        OriginalNfoSha256 = originalNfoSha256;
        Diagnostics = new ReadOnlyCollection<string>(
            diagnostics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public LocalSidecarPaths Sidecars { get; }

    public MovieMetadata Metadata { get; }

    public MetadataSourceSnapshot SourceSnapshot { get; }

    public string OriginalNfoSha256 { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public XDocument CloneOriginalDocument() => new(_originalDocument);
}
