using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed partial class LibreDmmClient : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public LibreDmmClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? CreateClient();
    }

    public string Name => "libredmm";
    public string DisplayName => "LibreDMM";

    public async Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default)
    {
        var id = MovieIdParser.Normalize(rawId);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("请先输入影片番号。", nameof(rawId));
        }

        var detailPageUrl = $"https://www.libredmm.com/movies/{Uri.EscapeDataString(id)}";
        var detailJsonUrl = $"{detailPageUrl}.json";
        using (var detailResponse = await _httpClient.GetAsync(detailJsonUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (detailResponse.IsSuccessStatusCode)
            {
                var detailJson = await detailResponse.Content.ReadAsStringAsync(cancellationToken);
                return ParseJson(detailJson, detailPageUrl, id);
            }

            if (detailResponse.StatusCode != HttpStatusCode.NotFound)
            {
                detailResponse.EnsureSuccessStatusCode();
            }
        }

        var url = $"https://www.libredmm.com/search?q={Uri.EscapeDataString(id)}&format=json";
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MetadataNotFoundException(DisplayName, id);
        }
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseJson(json, response.RequestMessage?.RequestUri?.AbsoluteUri ?? url, id);
    }

    public MovieMetadata ParseJson(string json, string sourceUrl, string? fallbackId = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !string.IsNullOrWhiteSpace(GetString(root, "err")))
        {
            throw new MetadataNotFoundException(DisplayName, MovieIdParser.Normalize(fallbackId));
        }

        var id = MovieIdParser.Normalize(FirstNonEmpty(GetString(root, "normalized_id"), fallbackId ?? string.Empty));
        var expectedId = MovieIdParser.Normalize(fallbackId);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(GetString(root, "title")) ||
            (!string.IsNullOrWhiteSpace(expectedId) && !string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MetadataNotFoundException(DisplayName, expectedId);
        }

        var sourceCoverUrl = GetString(root, "cover_image_url");
        var posterUrl = GetString(root, "thumbnail_image_url");
        var contentId = GetString(root, "subtitle").ToLowerInvariant();
        var sourceScreenshots = GetStringArray(root, "sample_image_urls").ToArray();
        var coverUrl = BuildHighResolutionCoverUrl(sourceScreenshots, sourceCoverUrl);
        var actors = GetActors(root);
        var screenshots = sourceScreenshots
            .Select(DmmImageUrlHelper.NormalizeScreenshotUrl)
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MovieMetadata
        {
            Id = id,
            Title = GetString(root, "title"),
            OriginalTitle = GetString(root, "title"),
            ReleaseDate = NormalizeReleaseDate(GetString(root, "date")),
            RuntimeMinutes = GetRuntimeMinutes(root),
            Director = JoinStrings(root, "directors"),
            Maker = JoinStrings(root, "makers"),
            Label = JoinStrings(root, "labels"),
            ActorsText = string.Join(", ", actors.Select(actor => actor.Name)),
            Actors = actors,
            GenresText = JoinStrings(root, "genres"),
            Plot = CleanDescription(GetString(root, "description")),
            Rating = GetPositiveNumber(root, "review"),
            ContentId = contentId,
            CoverUrl = coverUrl,
            FallbackCoverUrl = sourceCoverUrl,
            PosterUrl = FirstNonEmpty(posterUrl, sourceCoverUrl, coverUrl),
            ScreenshotUrls = screenshots,
            SourceUrl = sourceUrl,
            SourceName = Name,
            SourceDisplayName = DisplayName
        };
    }

    internal static string BuildHighResolutionCoverUrl(IEnumerable<string> screenshotUrls, string fallbackUrl)
    {
        foreach (var screenshotUrl in screenshotUrls)
        {
            if (!Uri.TryCreate(screenshotUrl, UriKind.Absolute, out var uri) || uri.Segments.Length < 2)
            {
                continue;
            }

            var contentId = uri.Segments[^2].Trim('/').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(contentId) || contentId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            {
                continue;
            }

            return $"https://awsimgsrc.dmm.com/pics_dig/digital/video/{contentId}/{contentId}pl.jpg";
        }

        return fallbackUrl;
    }

    internal static string CleanDescription(string? description)
    {
        var text = (description ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = BoilerplateSentenceRegex().Replace(text, string.Empty);
        var keptLines = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
        return ExcessBlankLinesRegex().Replace(string.Join("\n", keptLines), "\n\n").Trim();
    }

    private static string NormalizeReleaseDate(string raw)
    {
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime))
        {
            var japanDate = dateTime.ToUniversalTime().AddHours(9).Date;
            return japanDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw.Trim();
    }

    private static string GetRuntimeMinutes(JsonElement root)
    {
        if (root.TryGetProperty("volume", out var value) && value.TryGetInt32(out var seconds) && seconds > 0)
        {
            return Math.Max(1, (int)Math.Round(seconds / 60d)).ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string JoinStrings(JsonElement root, string property) =>
        string.Join(", ", GetStringArray(root, property).Distinct(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<ActorMetadata> GetActors(JsonElement root)
    {
        if (!root.TryGetProperty("actresses", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ActorMetadata>();
        }

        return items.EnumerateArray()
            .Select(item => new ActorMetadata(
                GetString(item, "name"),
                NormalizeImageUrl(GetString(item, "image_url"))))
            .Where(actor => !string.IsNullOrWhiteSpace(actor.Name))
            .GroupBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string NormalizeImageUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && uri.Host.EndsWith(".dmm.co.jp", StringComparison.OrdinalIgnoreCase))
        {
            return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.AbsoluteUri;
        }

        return uri.AbsoluteUri;
    }

    private static IEnumerable<string> GetStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return items.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() ?? string.Empty : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string GetPositiveNumber(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetDouble(out var number) || number <= 0)
        {
            return string.Empty;
        }

        return number.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : value.ToString().Trim();
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja,en-US;q=0.8,en;q=0.6");
        return client;
    }

    [GeneratedRegex("[「『]?(?:予約商品の価格保証|コンビニ受取)[」』]?対象商品です。?(?:詳しくはこちらをご覧ください。?)?", RegexOptions.IgnoreCase)]
    private static partial Regex BoilerplateSentenceRegex();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex ExcessBlankLinesRegex();

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
