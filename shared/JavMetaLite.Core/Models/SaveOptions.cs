namespace JavMetaLite.Core.Models;

public enum OutputNamingMode
{
    VideoBase,
    MovieFolder
}

public sealed record SaveOptions(
    bool WriteNfo,
    bool DownloadPoster,
    bool DownloadFanart,
    bool DownloadExtrafanart,
    bool IncludeIdInTitle,
    bool OverwriteExisting,
    bool ReplaceLocalExtrafanart = false)
{
    public bool RequiresPreview => !OverwriteExisting;
}

public sealed record SaveResult(
    string? NfoPath,
    string? PosterPath,
    string? FanartPath,
    IReadOnlyList<string> ExtrafanartPaths,
    bool FanartUsedFullCover);
