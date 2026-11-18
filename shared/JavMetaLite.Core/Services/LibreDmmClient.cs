using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed partial class LibreDmmClient : IRequestScheduledMetadataProvider
{
    private const string Site = "https://www.libredmm.com";
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromMinutes(1);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public LibreDmmClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? CreateClient();
    }

    public string Name => "libredmm";
    public string DisplayName => "LibreDMM";

    public Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default) =>
        SearchWithRequestTimeoutAsync(rawId, TimeSpan.FromSeconds(30), cancellationToken);

    public async Task<MovieMetadata> SearchWithRequestTimeoutAsync(
        string rawId, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        var code = NormalizeLookupCode(rawId);
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("请先输入影片番号。", nameof(rawId));

        var url = $"{Site}/api/v1/movies/{Uri.EscapeDataString(code)}";
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lookup.CancelAfter(LookupTimeout);
        try
        {
            while (true)
            {
                using var response = await GetResponseAsync(url, requestTimeout, lookup.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new MetadataNotFoundException(DisplayName, code);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new MetadataSourceRateLimitException(
                        DateTimeOffset.UtcNow + RetryDelay(response, TimeSpan.FromMinutes(1)), DisplayName);

                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.Accepted)
                {
                    var json = await response.Content.ReadAsStringAsync(lookup.Token);
                    using var document = JsonDocument.Parse(json);
                    return ParseMovieResponse(document.RootElement, code);
                }

                var delay = RetryDelay(response, TimeSpan.FromSeconds(3));
                // A 202 starts a lookup on the server. Poll the same code, within one overall deadline.
                await Task.Delay(delay > LookupTimeout ? LookupTimeout : delay, lookup.Token);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MetadataSourceTimeoutException(Name, DisplayName, LookupTimeout, error);
        }
    }

    private async Task<HttpResponseMessage> GetResponseAsync(string url, TimeSpan timeout, CancellationToken token)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        request.CancelAfter(timeout);
        try
        {
            // Buffer the body within the per-request timeout, including slow reads after the headers.
            return await _httpClient.GetAsync(url, request.Token);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new MetadataSourceTimeoutException(Name, DisplayName, timeout, error);
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, TimeSpan fallback)
    {
        var retry = response.Headers.RetryAfter;
        var delay = retry?.Delta ?? (retry?.Date - (response.Headers.Date ?? DateTimeOffset.UtcNow)) ?? fallback;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    public MovieMetadata ParseJson(string json, string sourceUrl, string? fallbackId = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            (root.TryGetProperty("movie", out _) || root.TryGetProperty("works", out _)))
            return ParseMovieResponse(root, NormalizeLookupCode(fallbackId));

        // Older flat responses are still supported by this parsing entry point.
        var expectedId = MovieIdParser.Normalize(fallbackId);
        var id = MovieIdParser.Normalize(FirstNonEmpty(GetString(root, "normalized_id"), expectedId));
        if (GetString(root, "err").Length > 0 || id.Length == 0 || GetString(root, "title").Length == 0 ||
            (expectedId.Length > 0 && !string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase)))
            throw new MetadataNotFoundException(DisplayName, expectedId);

        var metadata = ReadCommonFields(root, id, sourceUrl);
        metadata.ReleaseDate = NormalizeReleaseDate(GetString(root, "date"));
        metadata.RuntimeMinutes = GetRuntimeMinutes(root);
        metadata.ContentId = GetString(root, "subtitle").ToLowerInvariant();
        metadata.Rating = GetPositiveNumber(root, "review");
        var samples = GetStringArray(root, "sample_image_urls").ToArray();
        metadata.FallbackCoverUrl = GetString(root, "cover_image_url");
        metadata.CoverUrl = BuildHighResolutionCoverUrl(samples, metadata.FallbackCoverUrl);
        metadata.PosterUrl = FirstNonEmpty(GetString(root, "thumbnail_image_url"), metadata.FallbackCoverUrl, metadata.CoverUrl);
        metadata.ScreenshotUrls = samples.Select(DmmImageUrlHelper.NormalizeScreenshotUrl)
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _)).ToArray();
        return metadata;
    }

    private MovieMetadata ParseMovieResponse(JsonElement root, string expectedCode)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("movie", out var movie) ||
            movie.ValueKind != JsonValueKind.Object)
        {
            if (Items(root, "works").Any())
                throw new InvalidDataException($"LibreDMM 上有多部作品共用番号 {expectedCode}，请在网页中选择具体作品后导入。");
            throw new InvalidDataException("LibreDMM 未返回作品详情。");
        }

        var code = GetString(movie, "code");
        var id = FirstNonEmpty(GetString(movie, "base_code"), code.Split('~')[0]);
        var pseudoCode = GetString(movie, "pseudo_code");
        if (code.Length == 0 || id.Length == 0 || GetString(movie, "title").Length == 0)
            throw new InvalidDataException("LibreDMM 作品详情缺少番号或标题。");
        if (expectedCode.Length > 0)
        {
            var matchesWork = string.Equals(expectedCode, code, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(expectedCode, pseudoCode, StringComparison.OrdinalIgnoreCase);
            if (!matchesWork || !string.Equals(expectedCode.Split('~')[0], id, StringComparison.OrdinalIgnoreCase))
                throw new MetadataNotFoundException(DisplayName, expectedCode);
        }

        var metadata = ReadCommonFields(movie, id, BuildDetailPageUrl(code));
        metadata.ReleaseDate = NormalizeReleaseDate(GetString(movie, "release_date"));
        if (movie.TryGetProperty("runtime_minutes", out var runtime) && runtime.ValueKind == JsonValueKind.Number &&
            runtime.TryGetInt32(out var minutes) && minutes > 0)
            metadata.RuntimeMinutes = minutes.ToString(CultureInfo.InvariantCulture);
        if (movie.TryGetProperty("review", out var review))
            metadata.Rating = GetPositiveNumber(review, "average");

        var products = Items(movie, "products");
        var preferred = products.FirstOrDefault(product => product.ValueKind == JsonValueKind.Object &&
            product.TryGetProperty("preferred", out var flag) && flag.ValueKind == JsonValueKind.True);
        metadata.ContentId = FirstNonEmpty(GetString(preferred, "content_id"),
            products.Select(product => GetString(product, "content_id")).FirstOrDefault(value => value.Length > 0) ?? "");

        if (movie.TryGetProperty("cover", out var cover))
        {
            var full = NormalizeImageUrl(GetString(cover, "url"));
            metadata.FallbackCoverUrl = full;
            metadata.CoverUrl = HighResolutionJacket(full);
            metadata.PosterUrl = FirstNonEmpty(NormalizeImageUrl(GetString(cover, "thumb_url")), full);
        }

        var large = Items(movie, "sample_images").ToArray();
        var small = Items(movie, "sample_images_small").ToArray();
        metadata.ScreenshotUrls = Enumerable.Range(0, Math.Max(large.Length, small.Length))
            .Select(i => FirstNonEmpty(SampleAt(large, i), SampleAt(small, i))).ToArray();
        return metadata;
    }

    private static string SampleAt(JsonElement[] samples, int index) =>
        index < samples.Length && samples[index].ValueKind == JsonValueKind.String
            ? NormalizeImageUrl(samples[index].GetString() ?? "") : "";

    // Only upgrade the selected digital jacket. Samples can belong to a different product.
    private static string HighResolutionJacket(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host == "pics.dmm.co.jp" &&
            uri.Query.Length == 0 && uri.Segments is ["/", "digital/", "video/", var directory, var file])
        {
            var id = directory.TrimEnd('/');
            if (id.All(char.IsAsciiLetterOrDigit) && file == id + "pl.jpg")
                return $"https://awsimgsrc.dmm.com/pics_dig{uri.AbsolutePath}";
        }
        return url;
    }

    private MovieMetadata ReadCommonFields(JsonElement root, string id, string sourceUrl)
    {
        var actors = GetActors(root);
        return new MovieMetadata
        {
            Id = id,
            Title = GetString(root, "title"),
            OriginalTitle = GetString(root, "title"),
            Director = JoinStrings(root, "directors"),
            Maker = JoinStrings(root, "makers"),
            Label = JoinStrings(root, "labels"),
            Series = JoinStrings(root, "series"),
            ActorsText = string.Join(", ", actors.Select(actor => actor.Name)),
            Actors = actors,
            GenresText = JoinStrings(root, "genres"),
            Plot = CleanDescription(GetString(root, "description")),
            SourceUrl = sourceUrl,
            SourceName = Name,
            SourceDisplayName = DisplayName
        };
    }

    internal static string NormalizeLookupCode(string? value)
    {
        // A work selected on the website has a suffix that the generic filename parser would discard.
        var parts = (value ?? "").Trim().Split('~', 2);
        var id = MovieIdParser.Normalize(parts[0]);
        return parts.Length == 2 ? id + "~" + parts[1] : id;
    }

    public static string BuildDetailPageUrl(string rawId) =>
        $"{Site}/movies/{Uri.EscapeDataString(NormalizeLookupCode(rawId))}";

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
        if (root.TryGetProperty("volume", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var seconds) && seconds > 0)
        {
            return Math.Max(1, (int)Math.Round(seconds / 60d)).ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string JoinStrings(JsonElement root, string property) =>
        string.Join(", ", Items(root, property)
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : GetString(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<ActorMetadata> GetActors(JsonElement root)
    {
        return Items(root, "actresses")
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
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && uri.Host.EndsWith(".dmm.co.jp", StringComparison.OrdinalIgnoreCase))
        {
            return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.AbsoluteUri;
        }

        return uri.AbsoluteUri;
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var items) &&
        items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [];

    private static IEnumerable<string> GetStringArray(JsonElement root, string property) =>
        Items(root, property).Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim()).Where(value => value.Length > 0);

    private static string GetPositiveNumber(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || number <= 0)
        {
            return string.Empty;
        }

        return number.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString()!.Trim();
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
