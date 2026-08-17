using System.Text;
using System.Xml;
using System.Xml.Linq;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class NfoRoundTripWriter
{
    public static XDocument CreateUpdatedDocument(
        LocalMetadataBundle bundle,
        MovieMetadata metadata,
        bool updatePosterReference,
        string? posterFileName,
        bool updateFanartReference,
        string? fanartFileName)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(metadata);

        var document = bundle.CloneOriginalDocument();
        var root = document.Root
            ?? throw new InvalidDataException("NFO 缺少 <movie> 根元素。");
        if (!string.Equals(root.Name.LocalName, "movie", StringComparison.Ordinal))
        {
            throw new InvalidDataException("NFO 根元素必须是 <movie>。");
        }

        var original = bundle.Metadata;
        UpdateScalarWhenChanged(root, "title", original.Title, metadata.Title);
        UpdateScalarWhenChanged(root, "originaltitle", original.OriginalTitle, metadata.OriginalTitle);
        if (Changed(original.Id, metadata.Id))
        {
            SetScalar(root, "id", metadata.Id);
        }
        if (Changed(original.ContentId, metadata.ContentId) || Changed(original.Id, metadata.Id))
        {
            UpdateDefaultUniqueId(root, metadata);
        }
        if (Changed(original.ReleaseDate, metadata.ReleaseDate))
        {
            SetScalar(root, "premiered", metadata.ReleaseDate);
            SetScalar(root, "releasedate", metadata.ReleaseDate);
            SetScalar(root, "year", ExtractYear(metadata.ReleaseDate));
        }
        UpdateScalarWhenChanged(root, "runtime", original.RuntimeMinutes, metadata.RuntimeMinutes);
        UpdateScalarWhenChanged(root, "studio", original.Maker, metadata.Maker);
        if (Changed(original.Director, metadata.Director))
        {
            SetRepeated(root, "director", SplitList(metadata.Director));
        }
        UpdateScalarWhenChanged(root, "plot", original.Plot, metadata.Plot);
        UpdateScalarWhenChanged(root, "rating", original.Rating, metadata.Rating);
        if (Changed(original.GenresText, metadata.GenresText))
        {
            SetRepeated(root, "genre", SplitList(metadata.GenresText));
        }
        if (Changed(original.ActorsText, metadata.ActorsText) || !ActorsEqual(original.Actors, metadata.Actors))
        {
            SetActors(root, metadata);
        }
        if (Changed(original.Label, metadata.Label))
        {
            SetPrefixedTag(root, "Label:", metadata.Label);
        }
        if (Changed(original.Series, metadata.Series))
        {
            SetPrefixedTag(root, "Series:", metadata.Series);
        }
        if (updatePosterReference)
        {
            SetPosterReference(root, posterFileName);
        }
        if (updateFanartReference)
        {
            SetFanartReference(root, fanartFileName);
        }
        UpdateScalarWhenChanged(root, "website", original.SourceUrl, metadata.SourceUrl);
        return document;
    }

    public static bool HasChanges(
        LocalMetadataBundle bundle,
        MovieMetadata metadata,
        bool updatePosterReference,
        string? posterFileName,
        bool updateFanartReference,
        string? fanartFileName) =>
        !XNode.DeepEquals(
            bundle.CloneOriginalDocument(),
            CreateUpdatedDocument(
                bundle,
                metadata,
                updatePosterReference,
                posterFileName,
                updateFanartReference,
                fanartFileName));

    public static async Task WriteAsync(
        string destinationPath,
        LocalMetadataBundle bundle,
        MovieMetadata metadata,
        bool updatePosterReference,
        string? posterFileName,
        bool updateFanartReference,
        string? fanartFileName,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"NFO 已存在：{destinationPath}");
        }

        var document = CreateUpdatedDocument(
            bundle,
            metadata,
            updatePosterReference,
            posterFileName,
            updateFanartReference,
            fanartFileName);
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("无法确定 NFO 输出目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var settings = new XmlWriterSettings
            {
                Async = true,
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None
            };
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            await using (var writer = XmlWriter.Create(stream, settings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                document.Save(writer);
                await writer.FlushAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void UpdateDefaultUniqueId(XElement root, MovieMetadata metadata)
    {
        var uniqueIds = Elements(root, "uniqueid").ToList();
        var target = uniqueIds.FirstOrDefault(element => string.Equals(
                         element.Attribute("default")?.Value,
                         "true",
                         StringComparison.OrdinalIgnoreCase))
                     ?? uniqueIds.FirstOrDefault();
        var value = FirstNonEmpty(metadata.ContentId, metadata.Id);
        if (value.Length == 0)
        {
            if (target is not null)
            {
                target.Remove();
            }
            return;
        }

        if (target is null)
        {
            target = NewElement(root, "uniqueid");
            target.SetAttributeValue("type", NormalizeProviderName(metadata.SourceName));
            target.SetAttributeValue("default", "true");
            root.Add(target);
        }
        target.Value = value;
    }

    private static void SetScalar(XElement root, string localName, string? value)
    {
        var elements = Elements(root, localName).ToList();
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            foreach (var element in elements)
            {
                element.Remove();
            }
            return;
        }

        var target = elements.FirstOrDefault();
        if (target is null)
        {
            target = NewElement(root, localName);
            root.Add(target);
        }
        SetTextPreservingAttributes(target, normalized);
        foreach (var duplicate in elements.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static void UpdateScalarWhenChanged(
        XElement root,
        string localName,
        string? originalValue,
        string? updatedValue)
    {
        if (Changed(originalValue, updatedValue))
        {
            SetScalar(root, localName, updatedValue);
        }
    }

    private static void SetRepeated(XElement root, string localName, IReadOnlyList<string> values)
    {
        var existing = Elements(root, localName).ToList();
        for (var index = 0; index < values.Count; index++)
        {
            var element = index < existing.Count
                ? existing[index]
                : NewElement(root, localName);
            SetTextPreservingAttributes(element, values[index]);
            if (index >= existing.Count)
            {
                root.Add(element);
            }
        }
        foreach (var extra in existing.Skip(values.Count))
        {
            extra.Remove();
        }
    }

    private static void SetActors(XElement root, MovieMetadata metadata)
    {
        var names = SplitList(metadata.ActorsText);
        var existing = Elements(root, "actor").ToList();
        var available = new List<XElement>(existing);
        var retained = new HashSet<XElement>();
        XElement? insertionAnchor = existing.LastOrDefault();
        foreach (var name in names)
        {
            var actor = available.FirstOrDefault(element => string.Equals(
                ChildValue(element, "name"),
                name,
                StringComparison.OrdinalIgnoreCase));
            if (actor is not null)
            {
                available.Remove(actor);
            }
            else
            {
                actor = NewElement(root, "actor");
                if (insertionAnchor is null)
                {
                    root.Add(actor);
                }
                else
                {
                    insertionAnchor.AddAfterSelf(actor);
                }
                insertionAnchor = actor;
            }

            SetChildScalar(actor, "name", name, preserveWhenEmpty: false);
            var imageUrl = metadata.Actors.FirstOrDefault(item => string.Equals(
                item.Name,
                name,
                StringComparison.OrdinalIgnoreCase))?.ImageUrl;
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                SetChildScalar(actor, "thumb", imageUrl, preserveWhenEmpty: false);
            }
            retained.Add(actor);
        }

        foreach (var actor in existing)
        {
            if (!retained.Contains(actor))
            {
                actor.Remove();
            }
        }
    }

    private static void SetPrefixedTag(XElement root, string prefix, string? value)
    {
        var matching = Elements(root, "tag")
            .Where(element => Normalize(element.Value).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            foreach (var element in matching)
            {
                element.Remove();
            }
            return;
        }

        var target = matching.FirstOrDefault();
        if (target is null)
        {
            target = NewElement(root, "tag");
            root.Add(target);
        }
        SetTextPreservingAttributes(target, $"{prefix} {normalized}");
        foreach (var duplicate in matching.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static void SetPosterReference(XElement root, string? fileName)
    {
        var posterThumbs = Elements(root, "thumb")
            .Where(element => string.Equals(
                element.Attribute("aspect")?.Value,
                "poster",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var normalized = Normalize(fileName);
        if (normalized.Length == 0)
        {
            foreach (var thumb in posterThumbs)
            {
                thumb.Remove();
            }
            return;
        }

        var target = posterThumbs.FirstOrDefault();
        if (target is null)
        {
            target = NewElement(root, "thumb");
            target.SetAttributeValue("aspect", "poster");
            root.Add(target);
        }
        SetTextPreservingAttributes(target, normalized);
        foreach (var duplicate in posterThumbs.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static void SetFanartReference(XElement root, string? fileName)
    {
        var fanarts = Elements(root, "fanart").ToList();
        var normalized = Normalize(fileName);
        if (normalized.Length == 0)
        {
            foreach (var fanart in fanarts)
            {
                fanart.Remove();
            }
            return;
        }

        var target = fanarts.FirstOrDefault();
        if (target is null)
        {
            target = NewElement(root, "fanart");
            root.Add(target);
        }
        SetChildScalar(target, "thumb", normalized, preserveWhenEmpty: false);
        foreach (var duplicate in fanarts.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static void SetChildScalar(
        XElement parent,
        string localName,
        string? value,
        bool preserveWhenEmpty)
    {
        var children = Elements(parent, localName).ToList();
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            if (!preserveWhenEmpty)
            {
                foreach (var child in children)
                {
                    child.Remove();
                }
            }
            return;
        }

        var target = children.FirstOrDefault();
        if (target is null)
        {
            target = NewElement(parent, localName);
            parent.Add(target);
        }
        SetTextPreservingAttributes(target, normalized);
        foreach (var duplicate in children.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static void SetTextPreservingAttributes(XElement element, string value)
    {
        element.RemoveNodes();
        element.Add(new XText(value));
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        (value ?? string.Empty)
            .Split([',', '，', ';', '；', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<XElement> Elements(XElement parent, string localName) =>
        parent.Elements().Where(element =>
            string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static XElement NewElement(XElement parent, string localName) =>
        new(parent.Name.Namespace + localName);

    private static string ChildValue(XElement parent, string localName) =>
        Normalize(Elements(parent, localName).FirstOrDefault()?.Value);

    private static string ExtractYear(string? releaseDate) =>
        DateTime.TryParse(releaseDate, out var date) ? date.Year.ToString() : string.Empty;

    private static string FirstNonEmpty(params string?[] values) =>
        values.Select(Normalize).FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static bool Changed(string? original, string? updated) =>
        !string.Equals(Normalize(original), Normalize(updated), StringComparison.Ordinal);

    private static bool ActorsEqual(
        IReadOnlyList<ActorMetadata> left,
        IReadOnlyList<ActorMetadata> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
            string.Equals(pair.First.ImageUrl, pair.Second.ImageUrl, StringComparison.Ordinal));

    private static string NormalizeProviderName(string? sourceName)
    {
        var normalized = new string((sourceName ?? "manual")
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .ToArray())
            .ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "manual" : normalized;
    }
}
