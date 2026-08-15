using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class JavLibraryClient : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly HtmlParser _parser = new();
    private readonly bool _ownsClient;

    public JavLibraryClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? CreateClient();
    }

    public string Name => "javlibrary";
    public string DisplayName => "JAVLibrary";

    public async Task<MovieMetadata> SearchAsync(string rawId, CancellationToken cancellationToken = default)
    {
        var id = MovieIdParser.Normalize(rawId);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("请先输入影片番号。", nameof(rawId));
        }

        var searchUrl = BuildSearchUrl(id);
        var (html, responseUrl) = await GetHtmlAsync(searchUrl, cancellationToken);
        var document = await _parser.ParseDocumentAsync(html, cancellationToken);

        if (IsChallengePage(document, html))
        {
            throw new JavLibraryChallengeException(searchUrl);
        }

        if (document.QuerySelector("#video_info") is null)
        {
            var detailUrl = FindBestDetailUrl(document, responseUrl, id);
            if (detailUrl is null)
            {
                throw new JavLibraryNotFoundException(id);
            }

            (html, responseUrl) = await GetHtmlAsync(detailUrl, cancellationToken);
            document = await _parser.ParseDocumentAsync(html, cancellationToken);
        }

        return ParseDetailDocument(document, responseUrl, id);
    }

    public async Task<MovieMetadata> ParseDetailPageAsync(
        string html,
        string sourceUrl,
        string? fallbackId = null,
        CancellationToken cancellationToken = default)
    {
        var document = await _parser.ParseDocumentAsync(html, cancellationToken);
        if (document.QuerySelector("#video_info") is null)
        {
            throw new InvalidOperationException("当前页面不是 JAVLibrary 影片详情页。请先打开正确影片，再点击读取。 ");
        }

        return ParseDetailDocument(document, sourceUrl, MovieIdParser.Normalize(fallbackId));
    }

    public static string BuildSearchUrl(string rawId) =>
        $"https://www.javlibrary.com/cn/vl_searchbyid.php?keyword={Uri.EscapeDataString(MovieIdParser.Normalize(rawId))}";

    private async Task<(string Html, string ResponseUrl)> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ||
            html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase))
        {
            throw new JavLibraryChallengeException(url);
        }

        response.EnsureSuccessStatusCode();
        return (html, response.RequestMessage?.RequestUri?.ToString() ?? url);
    }

    private MovieMetadata ParseDetailDocument(IDocument document, string sourceUrl, string? fallbackId)
    {
        var pageId = Text(document, "#video_id .text");
        var id = MovieIdParser.Normalize(string.IsNullOrWhiteSpace(pageId) ? fallbackId : pageId);
        var title = CleanTitle(document.Title ?? string.Empty, id);
        var coverUrl = AbsoluteUrl(
            document.QuerySelector("#video_jacket_img")?.GetAttribute("src") ??
            document.QuerySelector("#video_jacket")?.GetAttribute("href"),
            sourceUrl);
        var posterUrl = coverUrl.Replace("pl.jpg", "ps.jpg", StringComparison.OrdinalIgnoreCase);

        var description = document.QuerySelector("meta[name='description']")?.GetAttribute("content")?.Trim() ?? string.Empty;
        if (description.Contains("JAVLibrary", StringComparison.OrdinalIgnoreCase))
        {
            description = string.Empty;
        }

        return new MovieMetadata
        {
            Id = id,
            Title = title,
            OriginalTitle = title,
            ReleaseDate = Text(document, "#video_date .text"),
            RuntimeMinutes = DigitsOnly(Text(document, "#video_length .text")),
            Director = Text(document, "#video_director .text"),
            Maker = Text(document, "#video_maker .text"),
            Label = Text(document, "#video_label .text"),
            Series = Text(document, "#video_series .text"),
            ActorsText = JoinDistinct(document.QuerySelectorAll("#video_cast .star a, #video_cast .star")),
            GenresText = JoinDistinct(document.QuerySelectorAll("#video_genres .genre a, #video_genres .genre")),
            Plot = description,
            Rating = ExtractRating(document),
            CoverUrl = coverUrl,
            PosterUrl = string.IsNullOrWhiteSpace(posterUrl) ? coverUrl : posterUrl,
            SourceUrl = sourceUrl,
            SourceName = Name,
            SourceDisplayName = DisplayName
        };
    }

    private static string? FindBestDetailUrl(IDocument document, string baseUrl, string id)
    {
        var normalizedId = MovieIdParser.Normalize(id);
        var candidates = document.QuerySelectorAll("a[href*='?v=']");
        var best = candidates.FirstOrDefault(anchor =>
            MovieIdParser.Normalize(anchor.ParentElement?.TextContent) == normalizedId ||
            anchor.ParentElement?.TextContent.Contains(normalizedId, StringComparison.OrdinalIgnoreCase) == true);

        best ??= candidates.FirstOrDefault();
        var href = best?.GetAttribute("href");
        return string.IsNullOrWhiteSpace(href) ? null : AbsoluteUrl(href, baseUrl);
    }

    private static bool IsChallengePage(IDocument document, string html) =>
        (document.Title?.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (document.Title?.Contains("Attention Required", StringComparison.OrdinalIgnoreCase) ?? false) ||
        html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase);

    private static string Text(IDocument document, string selector) =>
        WebUtility.HtmlDecode(document.QuerySelector(selector)?.TextContent ?? string.Empty).Trim();

    private static string JoinDistinct(IEnumerable<IElement> elements) =>
        string.Join(", ", elements
            .Select(element => WebUtility.HtmlDecode(element.TextContent).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string CleanTitle(string title, string id)
    {
        title = WebUtility.HtmlDecode(title).Trim();
        var suffixIndex = title.LastIndexOf(" - JAVLibrary", StringComparison.OrdinalIgnoreCase);
        if (suffixIndex > 0)
        {
            title = title[..suffixIndex];
        }

        if (!string.IsNullOrWhiteSpace(id) && title.StartsWith(id, StringComparison.OrdinalIgnoreCase))
        {
            title = title[id.Length..].TrimStart(' ', '-', '–', '—');
        }

        return title.Trim();
    }

    private static string DigitsOnly(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private static string ExtractRating(IDocument document)
    {
        var value = Text(document, "#video_rating .score");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Text(document, "#video_rating .text");
        }

        var token = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        return token ?? string.Empty;
    }

    private static string AbsoluteUrl(string? value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{value}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(new Uri(baseUrl), value).ToString();
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,ja;q=0.7,en;q=0.5");
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

public sealed class JavLibraryChallengeException(string url)
    : Exception("JAVLibrary 要求浏览器验证。请使用内置浏览器打开页面并读取资料。")
{
    public string Url { get; } = url;
}

public sealed class JavLibraryNotFoundException(string id)
    : Exception($"JAVLibrary 没有找到番号 {id}。你可以修改番号或使用内置浏览器手动查找。")
{
    public string MovieId { get; } = id;
}
