using System.Text;
using AngleSharp.Html.Parser;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

/// <summary>Explicit, bounded browser imports. Captured HTML is parsed as data, never executed.</summary>
public sealed class BrowserPageImporter(LibreDmmClient libre, R18DevClient r18, JavLibraryClient library)
{
    public const int MaximumHtmlBytes = 2 * 1024 * 1024;

    public static void ValidatePage(string source, string url, string html)
    {
        ValidateUrl(source, url);
        if (string.IsNullOrWhiteSpace(html) || Encoding.UTF8.GetByteCount(html) > MaximumHtmlBytes)
            throw new BrowserPageImportException("PAGE_TOO_LARGE", "The captured page must contain HTML and fit within 2 MiB UTF-8.");
    }

    public static void ValidateUrl(string source, string url)
    {
        if (!BrowserImportRouting.IsSupportedSource(source) || url.Length > 4096 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw new BrowserPageImportException("PAGE_UNSUPPORTED", "Choose an HTTP(S) detail page from the selected supported provider.");
        var host = source switch { "libredmm" => "libredmm.com", "r18dev" => "r18.dev", "javlibrary" => "javlibrary.com", _ => "" };
        if (!(uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.IdnHost.Equals("www." + host, StringComparison.OrdinalIgnoreCase)) ||
            !BrowserImportRouting.IsExpectedDetailPage(source, url) ||
            source == "javlibrary" && !IsLibraryDetailAddress(uri))
            throw new BrowserPageImportException("PAGE_UNSUPPORTED", "This URL is not a supported provider detail page.");
    }

    private static bool IsLibraryDetailAddress(Uri uri)
    {
        // Keep legacy links, and accept the canonical addresses currently emitted
        // by JAVLibrary search. HTML identity checks below remain authoritative.
        if (uri.Query.TrimStart('?').Split('&').Any(part => part.StartsWith("v=", StringComparison.Ordinal) && part.Length > 2)) return true;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !new[] { "cn", "tw", "en", "ja" }.Contains(segments[0], StringComparer.OrdinalIgnoreCase)) return false;
        var name = segments[1];
        if (!name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = name[..^5];
        return stem.Length is > 3 and <= 64 && stem.StartsWith("jav", StringComparison.OrdinalIgnoreCase) && stem.All(char.IsAsciiLetterOrDigit);
    }

    public async Task<MovieMetadata> ImportAsync(string source, string url, string html, string expectedMovieId, CancellationToken token = default)
    {
        ValidatePage(source, url, html);
        var expected = MovieIdParser.Normalize(expectedMovieId);
        if (string.IsNullOrWhiteSpace(expected)) throw new BrowserPageImportException("PAGE_ID_MISMATCH", "The selected movie needs an ID before importing a page.");
        MovieMetadata result;
        switch (source)
        {
            case "javlibrary":
                var document = await new HtmlParser().ParseDocumentAsync(html, token);
                if (document.QuerySelector("#video_info") is null)
                    throw new BrowserPageImportException("PAGE_NOT_READY", "The current page is a challenge, search page or incomplete detail page.");
                // Never use the selected movie as a fallback for a missing page ID.
                RequireMatchingId(expected, document.QuerySelector("#video_id .text")?.TextContent);
                result = await library.ParseDetailPageAsync(html, url, null, token);
                break;
            case "libredmm":
                var pageId = BrowserImportRouting.TryExtractMovieId(source, url);
                RequireMatchingId(expected, pageId);
                result = await libre.SearchAsync(pageId!, token);
                break;
            case "r18dev":
                var r18Document = await new HtmlParser().ParseDocumentAsync(html, token);
                if (BrowserImportRouting.ClassifyR18Page(url, true, 200,
                        r18Document.QuerySelector("#video-info") is not null, false,
                        BrowserImportRouting.ContainsR18NotFoundMessage(r18Document.Body?.TextContent)) != BrowserImportPageState.Ready)
                    throw new BrowserPageImportException("PAGE_NOT_READY", "The R18 page is missing, challenged or not a loaded movie detail page.");
                result = await r18.ImportDetailPageAsync(url, null, token);
                break;
            default: throw new BrowserPageImportException("PAGE_UNSUPPORTED", "The provider is unsupported.");
        }
        RequireMatchingId(expected, result.Id);
        if (!string.Equals(result.SourceName, source, StringComparison.Ordinal))
            throw new BrowserPageImportException("PAGE_UNSUPPORTED", "The result belongs to a different provider.");
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static void RequireMatchingId(string expected, string? actual)
    {
        var normalized = MovieIdParser.Normalize(actual);
        if (string.IsNullOrWhiteSpace(normalized) || !string.Equals(expected, normalized, StringComparison.OrdinalIgnoreCase))
            throw new BrowserPageImportException("PAGE_ID_MISMATCH", "The page ID is missing or differs from the selected movie. Select the matching job before importing.");
    }
}

public sealed class BrowserPageImportException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}
