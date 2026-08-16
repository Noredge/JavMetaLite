namespace JavMetaLite.Core.Models;

public sealed record LocalSidecarPaths(
    string VideoPath,
    string? NfoPath,
    string? PosterPath,
    string? FanartPath)
{
    public bool HasNfo => !string.IsNullOrWhiteSpace(NfoPath);

    public bool HasArtwork =>
        !string.IsNullOrWhiteSpace(PosterPath) || !string.IsNullOrWhiteSpace(FanartPath);
}
