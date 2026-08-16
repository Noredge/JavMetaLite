using System.Xml;
using System.Xml.Linq;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class NfoReader
{
    public const long MaximumNfoBytes = 4 * 1024 * 1024;

    public static async Task<LocalMetadataBundle> ReadAsync(
        LocalSidecarPaths sidecars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        if (string.IsNullOrWhiteSpace(sidecars.NfoPath))
        {
            throw new FileNotFoundException("影片旁没有找到同名 NFO。", sidecars.NfoPath);
        }

        var nfoPath = Path.GetFullPath(sidecars.NfoPath);
        var fileInfo = new FileInfo(nfoPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("NFO 文件不存在。", nfoPath);
        }

        if (fileInfo.Length > MaximumNfoBytes)
        {
            throw new InvalidDataException($"NFO 文件过大，最大允许 {MaximumNfoBytes} 字节。");
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = false,
                IgnoreWhitespace = false,
                MaxCharactersInDocument = MaximumNfoBytes
            };
            await using var stream = new FileStream(
                nfoPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = XmlReader.Create(stream, settings);
            document = await XDocument.LoadAsync(
                reader,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo,
                cancellationToken);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"无法安全读取 NFO：{exception.Message}", exception);
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "movie", StringComparison.Ordinal))
        {
            throw new InvalidDataException("NFO 根元素必须是 <movie>。");
        }

        var actors = Elements(root, "actor")
            .Select(element => new ActorMetadata(
                ElementValue(element, "name"),
                ElementValue(element, "thumb")))
            .Where(actor => !string.IsNullOrWhiteSpace(actor.Name))
            .GroupBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var uniqueId = Elements(root, "uniqueid")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("default")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase))
            ?? Elements(root, "uniqueid").FirstOrDefault();
        var tags = Elements(root, "tag")
            .Select(element => Normalize(element.Value))
            .Where(value => value.Length > 0)
            .ToArray();

        var metadata = new MovieMetadata
        {
            Id = FirstNonEmpty(ElementValue(root, "id"), Normalize(uniqueId?.Value)),
            ContentId = Normalize(uniqueId?.Value),
            Title = ElementValue(root, "title"),
            OriginalTitle = ElementValue(root, "originaltitle"),
            ReleaseDate = FirstNonEmpty(ElementValue(root, "premiered"), ElementValue(root, "releasedate")),
            RuntimeMinutes = ElementValue(root, "runtime"),
            Maker = ElementValue(root, "studio"),
            Director = JoinValues(Elements(root, "director").Select(element => element.Value)),
            Plot = ElementValue(root, "plot"),
            Rating = ElementValue(root, "rating"),
            GenresText = JoinValues(Elements(root, "genre").Select(element => element.Value)),
            Actors = actors,
            ActorsText = JoinValues(actors.Select(actor => actor.Name)),
            Label = FindPrefixedTag(tags, "Label:"),
            Series = FindPrefixedTag(tags, "Series:"),
            SourceUrl = ElementValue(root, "website"),
            SourceName = "local-nfo",
            SourceDisplayName = "本地 NFO"
        };
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(metadata.Id))
        {
            diagnostics.Add("NFO 未包含 id 或 uniqueid。");
        }
        if (string.IsNullOrWhiteSpace(metadata.Title))
        {
            diagnostics.Add("NFO 未包含 title。");
        }

        return new LocalMetadataBundle(
            sidecars with { NfoPath = nfoPath },
            metadata,
            document,
            diagnostics);
    }

    private static IEnumerable<XElement> Elements(XElement parent, string localName) =>
        parent.Elements().Where(element =>
            string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static string ElementValue(XElement parent, string localName) =>
        Normalize(Elements(parent, localName).FirstOrDefault()?.Value);

    private static string JoinValues(IEnumerable<string?> values) =>
        string.Join(", ", values
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string FindPrefixedTag(IEnumerable<string> tags, string prefix)
    {
        var value = tags.FirstOrDefault(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return value is null ? string.Empty : value[prefix.Length..].Trim();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.Select(Normalize).FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
