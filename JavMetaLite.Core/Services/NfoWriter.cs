using System.Text;
using System.Xml;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class NfoWriter
{
    public static async Task WriteAsync(
        string destinationPath,
        MovieMetadata metadata,
        string? posterFileName,
        string? fanartFileName,
        bool includeIdInTitle,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"NFO 已存在：{destinationPath}\n请重新保存并在安全预览中确认覆盖。 ");
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("无法确定 NFO 输出目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var settings = new XmlWriterSettings
            {
                Async = true,
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine
            };

            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            await using (var writer = XmlWriter.Create(stream, settings))
            {
                await writer.WriteStartDocumentAsync();
                await writer.WriteStartElementAsync(null, "movie", null);

                var canonicalId = MovieIdParser.Normalize(metadata.Id);
                await WriteElementAsync(
                    writer,
                    "title",
                    JellyfinTitleFormatter.Format(canonicalId, metadata.Title, includeIdInTitle));
                await WriteElementAsync(writer, "originaltitle", metadata.OriginalTitle);
                await WriteElementAsync(writer, "id", canonicalId);

                if (!string.IsNullOrWhiteSpace(canonicalId))
                {
                    await WriteUniqueIdAsync(writer, "javnumber", canonicalId, isDefault: true);

                    var contentId = metadata.ContentId.Trim();
                    var providerName = NormalizeProviderName(metadata.SourceName);
                    if (contentId.Length > 0 &&
                        !contentId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase) &&
                        !providerName.Equals("javnumber", StringComparison.OrdinalIgnoreCase) &&
                        !providerName.Equals("local-nfo", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteUniqueIdAsync(writer, providerName, contentId, isDefault: false);
                    }
                }

                await WriteElementAsync(writer, "premiered", metadata.ReleaseDate);
                await WriteElementAsync(writer, "releasedate", metadata.ReleaseDate);
                await WriteElementAsync(writer, "year", ExtractYear(metadata.ReleaseDate));
                await WriteElementAsync(writer, "runtime", metadata.RuntimeMinutes);
                await WriteElementAsync(writer, "studio", metadata.Maker);
                await WriteElementAsync(writer, "director", metadata.Director);
                await WriteElementAsync(writer, "plot", metadata.Plot);
                await WriteElementAsync(writer, "rating", metadata.Rating);

                foreach (var genre in SplitList(metadata.GenresText))
                {
                    await WriteElementAsync(writer, "genre", genre);
                }

                foreach (var actor in SplitList(metadata.ActorsText))
                {
                    await writer.WriteStartElementAsync(null, "actor", null);
                    await WriteElementAsync(writer, "name", actor);
                    var actorImageUrl = metadata.Actors
                        .FirstOrDefault(item => string.Equals(item.Name, actor, StringComparison.OrdinalIgnoreCase))
                        ?.ImageUrl;
                    if (Uri.TryCreate(actorImageUrl, UriKind.Absolute, out _))
                    {
                        await WriteElementAsync(writer, "thumb", actorImageUrl);
                    }
                    await writer.WriteEndElementAsync();
                }

                if (!string.IsNullOrWhiteSpace(metadata.Label))
                {
                    await WriteElementAsync(writer, "tag", $"Label: {metadata.Label.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(posterFileName))
                {
                    await writer.WriteStartElementAsync(null, "thumb", null);
                    await writer.WriteAttributeStringAsync(null, "aspect", null, "poster");
                    await writer.WriteStringAsync(posterFileName);
                    await writer.WriteEndElementAsync();
                }

                if (!string.IsNullOrWhiteSpace(fanartFileName))
                {
                    await writer.WriteStartElementAsync(null, "fanart", null);
                    await WriteElementAsync(writer, "thumb", fanartFileName);
                    await writer.WriteEndElementAsync();
                }

                await WriteElementAsync(writer, "website", metadata.SourceUrl);
                await writer.WriteEndElementAsync();
                await writer.WriteEndDocumentAsync();
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

    private static async Task WriteElementAsync(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            await writer.WriteElementStringAsync(null, name, null, value.Trim());
        }
    }

    private static async Task WriteUniqueIdAsync(
        XmlWriter writer,
        string providerName,
        string value,
        bool isDefault)
    {
        await writer.WriteStartElementAsync(null, "uniqueid", null);
        await writer.WriteAttributeStringAsync(null, "type", null, providerName);
        if (isDefault)
        {
            await writer.WriteAttributeStringAsync(null, "default", null, "true");
        }
        await writer.WriteStringAsync(value);
        await writer.WriteEndElementAsync();
    }

    private static string ExtractYear(string? releaseDate) =>
        DateTime.TryParse(releaseDate, out var date) ? date.Year.ToString() : string.Empty;

    private static IEnumerable<string> SplitList(string? value) =>
        (value ?? string.Empty)
            .Split([',', '，', ';', '；', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeProviderName(string? sourceName)
    {
        var normalized = new string((sourceName ?? "manual")
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .ToArray())
            .ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "manual" : normalized;
    }
}
