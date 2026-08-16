using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class R18DevClient : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public R18DevClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? CreateClient();
    }

    public string Name => "r18dev";
    public string DisplayName => "R18.dev";

    public async Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default)
    {
        var id = MovieIdParser.Normalize(rawId);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("请先输入影片番号。", nameof(rawId));
        }

        var normalized = new string(id.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var guessedContentId = BuildCombinedContentId(id);
        Exception? lastError = null;
        if (!string.IsNullOrWhiteSpace(guessedContentId))
        {
            var guessedUrl = BuildCombinedUrl(guessedContentId);
            var guessedResponse = await TryDownloadJsonAsync(guessedUrl, cancellationToken);
            lastError = guessedResponse.Error ?? lastError;
            if (guessedResponse.Json is not null)
            {
                try
                {
                    return ParseJson(guessedResponse.Json, guessedUrl, id);
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException or MetadataNotFoundException)
                {
                    lastError = exception;
                }
            }
        }

        var compactUrl = $"https://r18.dev/videos/vod/movies/detail/-/dvd_id={Uri.EscapeDataString(normalized)}/json";
        var compactResponse = await TryDownloadJsonAsync(compactUrl, cancellationToken);
        lastError = compactResponse.Error ?? lastError;
        if (compactResponse.Json is not null)
        {
            var discoveredContentId = GetContentIdFromJson(compactResponse.Json);
            if (!string.IsNullOrWhiteSpace(discoveredContentId) &&
                !string.Equals(discoveredContentId, guessedContentId, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Info($"R18.dev 从 dvd_id 解析实际 content_id id={id} contentId={discoveredContentId}");
                var discoveredUrl = BuildCombinedUrl(discoveredContentId);
                var discoveredResponse = await TryDownloadJsonAsync(discoveredUrl, cancellationToken);
                lastError = discoveredResponse.Error ?? lastError;
                if (discoveredResponse.Json is not null)
                {
                    try
                    {
                        return ParseJson(discoveredResponse.Json, discoveredUrl, id);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or JsonException or MetadataNotFoundException)
                    {
                        lastError = exception;
                    }
                }
            }

            try
            {
                return ParseJson(compactResponse.Json, compactUrl, id);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or MetadataNotFoundException)
            {
                lastError = exception;
            }
        }

        throw lastError is null
            ? new MetadataNotFoundException(DisplayName, id)
            : new InvalidDataException($"{DisplayName} 暂时无法读取番号 {id}。", lastError);
    }

    public MovieMetadata ParseJson(string json, string sourceUrl, string? fallbackId = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("R18.dev 返回的资料格式不正确。 ");
        }

        var id = MovieIdParser.Normalize(GetString(root, "dvd_id"));
        if (string.IsNullOrWhiteSpace(id))
        {
            id = MovieIdParser.Normalize(fallbackId);
        }

        var genericTitle = GetString(root, "title");
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(GetFirstString(root, "title_ja", "title_en", "title")))
        {
            throw new MetadataNotFoundException(DisplayName, fallbackId ?? string.Empty);
        }

        var coverUrl = GetFirstString(root, "jacket_full_url", "jacket_thumb_url");
        if (string.IsNullOrWhiteSpace(coverUrl) && root.TryGetProperty("images", out var images) &&
            images.TryGetProperty("jacket_image", out var jacket))
        {
            coverUrl = GetFirstString(jacket, "large2", "large");
        }

        var sourceCoverUrl = coverUrl;
        var contentId = GetString(root, "content_id").ToLowerInvariant();
        coverUrl = BuildHighResolutionCoverUrl(contentId, sourceCoverUrl);
        var posterUrl = sourceCoverUrl.Replace("pl.jpg", "ps.jpg", StringComparison.OrdinalIgnoreCase);
        var japaneseTitle = GetString(root, "title_ja");
        var englishTitle = FirstNonEmpty(GetString(root, "title_en"), genericTitle);
        var screenshots = GetScreenshotUrls(root, contentId);
        var actors = GetActors(root);

        return new MovieMetadata
        {
            Id = id,
            Title = FirstNonEmpty(englishTitle, japaneseTitle),
            OriginalTitle = FirstNonEmpty(japaneseTitle, ContainsJapanese(genericTitle) ? genericTitle : string.Empty),
            ReleaseDate = GetString(root, "release_date"),
            RuntimeMinutes = FirstNonEmpty(GetIntString(root, "runtime_mins"), GetIntString(root, "runtime_minutes")),
            Director = FirstNonEmpty(GetPeople(root, "directors", "name_romaji", "name_kanji", "name"), GetString(root, "director")),
            Maker = FirstNonEmpty(GetString(root, "maker_name_en"), GetString(root, "maker_name_ja"), GetNestedName(root, "maker")),
            Label = FirstNonEmpty(GetString(root, "label_name_en"), GetString(root, "label_name_ja"), GetNestedName(root, "label")),
            Series = FirstNonEmpty(GetString(root, "series_name_en"), GetString(root, "series_name_ja"), GetString(root, "series_name"), GetNestedName(root, "series")),
            ActorsText = string.Join(", ", actors.Select(actor => actor.Name)),
            Actors = actors,
            GenresText = GetCategories(root),
            Plot = FirstNonEmpty(GetString(root, "description_en"), GetString(root, "description")),
            ContentId = contentId,
            CoverUrl = coverUrl,
            FallbackCoverUrl = sourceCoverUrl,
            PosterUrl = string.IsNullOrWhiteSpace(posterUrl) ? coverUrl : posterUrl,
            ScreenshotUrls = screenshots,
            SourceName = Name,
            SourceUrl = sourceUrl,
            SourceDisplayName = DisplayName
        };
    }

    private static IReadOnlyList<string> GetScreenshotUrls(JsonElement root, string contentId)
    {
        var urls = new List<string>();
        var galleryCount = 0;
        if (root.TryGetProperty("gallery", out var gallery) && gallery.ValueKind == JsonValueKind.Array)
        {
            galleryCount = gallery.GetArrayLength();
            urls.AddRange(gallery.EnumerateArray()
                .Select(item => FirstNonEmpty(GetString(item, "image_full"), GetString(item, "image_thumb"))));
        }

        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object &&
            images.TryGetProperty("sample_images", out var sampleImages) && sampleImages.ValueKind == JsonValueKind.Array)
        {
            galleryCount = Math.Max(galleryCount, sampleImages.GetArrayLength());
            urls.AddRange(sampleImages.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty));
        }

        var safeContentId = SanitizeContentId(contentId);
        if (!string.IsNullOrWhiteSpace(safeContentId) && galleryCount > 0)
        {
            return Enumerable.Range(1, galleryCount)
                .Select(index => $"https://awsimgsrc.dmm.com/dig/digital/video/{safeContentId}/{safeContentId}jp-{index}.jpg")
                .ToArray();
        }

        return urls
            .Select(DmmImageUrlHelper.NormalizeScreenshotUrl)
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ActorMetadata> GetActors(JsonElement root)
    {
        if (!root.TryGetProperty("actresses", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ActorMetadata>();
        }

        return items.EnumerateArray()
            .Select(item => new ActorMetadata(
                FirstNonEmpty(GetString(item, "name_romaji"), GetString(item, "name_kanji"), GetString(item, "name")),
                FirstNonEmpty(GetString(item, "image_url"), GetString(item, "image_url_large"), GetString(item, "image_url_small"))))
            .Where(actor => !string.IsNullOrWhiteSpace(actor.Name))
            .GroupBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    internal static string BuildHighResolutionCoverUrl(string contentId, string fallbackUrl)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return fallbackUrl;
        }

        var safeContentId = SanitizeContentId(contentId);
        if (string.IsNullOrWhiteSpace(safeContentId))
        {
            return fallbackUrl;
        }

        if (Uri.TryCreate(fallbackUrl, UriKind.Absolute, out var fallbackUri) &&
            fallbackUri.AbsolutePath.Contains("/mono/movie/", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://awsimgsrc.dmm.com/dig/mono/movie/{safeContentId}/{safeContentId}pl.jpg";
        }

        return $"https://awsimgsrc.dmm.com/dig/digital/video/{safeContentId}/{safeContentId}pl.jpg";
    }

    private static string SanitizeContentId(string? contentId) =>
        new string((contentId ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .ToArray())
            .ToLowerInvariant();

    internal static string BuildCombinedContentId(string id)
    {
        var normalized = MovieIdParser.Normalize(id);
        var separator = normalized.LastIndexOf('-');
        if (separator <= 0 || separator >= normalized.Length - 1 ||
            !int.TryParse(normalized[(separator + 1)..], out var number))
        {
            return string.Empty;
        }

        var prefix = new string(normalized[..separator].Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(prefix) ? string.Empty : $"{prefix}{number:D5}";
    }

    private static string BuildCombinedUrl(string contentId) =>
        $"https://r18.dev/videos/vod/movies/detail/-/combined={Uri.EscapeDataString(contentId)}/json";

    private async Task<(string? Json, Exception? Error)> TryDownloadJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://r18.dev/");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound ||
                response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            {
                return (null, null);
            }

            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            return (null, exception);
        }
    }

    private static string GetContentIdFromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return SanitizeContentId(GetString(document.RootElement, "content_id"));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static bool ContainsJapanese(string value) =>
        value.Any(character =>
            character is >= '\u3040' and <= '\u30ff' or >= '\u4e00' and <= '\u9fff');

    private static string GetCategories(JsonElement root)
    {
        if (!root.TryGetProperty("categories", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", items.EnumerateArray()
            .Select(item => FirstNonEmpty(GetString(item, "name_en"), GetString(item, "name_ja"), GetString(item, "name")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string GetPeople(JsonElement root, string property, params string[] preferredNameFields)
    {
        if (!root.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(", ", items.EnumerateArray()
            .Select(item => FirstNonEmpty(preferredNameFields.Select(field => GetString(item, field)).ToArray()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string GetNestedName(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var nested))
        {
            return string.Empty;
        }

        return nested.ValueKind switch
        {
            JsonValueKind.String => nested.GetString() ?? string.Empty,
            JsonValueKind.Object => GetString(nested, "name"),
            _ => string.Empty
        };
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!element.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : value.ToString().Trim();
    }

    private static string GetFirstString(JsonElement element, params string[] properties) =>
        FirstNonEmpty(properties.Select(property => GetString(element, property)).ToArray());

    private static string GetIntString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number.ToString() : string.Empty;

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

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class MetadataNotFoundException(string provider, string id)
    : Exception($"{provider} 没有找到番号 {id}。")
{
    public string Provider { get; } = provider;
    public string MovieId { get; } = id;
}
