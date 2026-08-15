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
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"NFO 已存在：{destinationPath}\n请勾选“允许覆盖已有文件”后重试。 ");
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

                await WriteElementAsync(writer, "title", metadata.Title);
                await WriteElementAsync(writer, "originaltitle", metadata.OriginalTitle);
                await WriteElementAsync(writer, "id", metadata.Id);

                if (!string.IsNullOrWhiteSpace(metadata.Id))
                {
                    await writer.WriteStartElementAsync(null, "uniqueid", null);
                    await writer.WriteAttributeStringAsync(null, "type", null, NormalizeProviderName(metadata.SourceName));
                    await writer.WriteAttributeStringAsync(null, "default", null, "true");
                    await writer.WriteStringAsync(metadata.Id.Trim());
                    await writer.WriteEndElementAsync();
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

                if (!string.IsNullOrWhiteSpace(metadata.Series))
                {
                    await WriteElementAsync(writer, "tag", $"Series: {metadata.Series.Trim()}");
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
