namespace JavMetaLite.Core.Models;

public sealed record SaveOptions(
    bool WriteNfo,
    bool DownloadPoster,
    bool DownloadFanart,
    bool DownloadExtrafanart,
    bool OverwriteExisting)
{
    public bool RequiresPreview => !OverwriteExisting;
}

public sealed record SaveResult(
    string? NfoPath,
    string? PosterPath,
    string? FanartPath,
    IReadOnlyList<string> ExtrafanartPaths,
    bool FanartUsedFullCover,
    string CoverSourceDisplayName = "",
    string ScreenshotSourceDisplayName = "");
