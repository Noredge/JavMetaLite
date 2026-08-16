namespace JavMetaLite.Core.Models;

public sealed record ArtworkSourceCandidate(
    MetadataCandidateSource Source,
    IReadOnlyList<string> CoverUrls,
    IReadOnlyList<string> ScreenshotUrls)
{
    public bool HasCover => CoverUrls.Count > 0;

    public bool HasScreenshots => ScreenshotUrls.Count > 0;
}

public sealed record ArtworkScreenshotChoice(
    string Name,
    string DisplayName,
    IReadOnlyList<string> Urls,
    bool IsCombined = false);
