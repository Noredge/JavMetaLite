namespace JavMetaLite.Core.Models;

public sealed record ArtworkSelection(
    string CoverSourceName,
    string CoverSourceDisplayName,
    IReadOnlyList<string> CoverUrls,
    string ScreenshotSourceName,
    string ScreenshotSourceDisplayName,
    IReadOnlyList<string> ScreenshotUrls)
{
    public static ArtworkSelection FromMetadata(MovieMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var source = MetadataCandidateSource.FromMetadata(metadata);
        return new ArtworkSelection(
            source.Name,
            source.DisplayName,
            NormalizeUrls([metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl]),
            source.Name,
            source.DisplayName,
            NormalizeUrls(metadata.ScreenshotUrls));
    }

    public string CoverSummary => CoverUrls.Count == 0
        ? "没有封套候选"
        : $"{CoverSourceDisplayName} · {CoverUrls.Count} 个地址";

    public string ScreenshotSummary => ScreenshotUrls.Count == 0
        ? "没有剧照候选"
        : $"{ScreenshotSourceDisplayName} · 最多 {Math.Min(50, ScreenshotUrls.Count)} 张（保存时按内容去重）";

    internal static IReadOnlyList<string> NormalizeUrls(IEnumerable<string> urls) =>
        urls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
