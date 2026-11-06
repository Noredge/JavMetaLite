using JavMetaLite.Core.Services;

namespace JavMetaLite.Core.Services;

/// <summary>The unchanged WPF image algorithm, isolated from the portable Core.</summary>
public sealed class WindowsArtworkImageProcessor : IArtworkImageProcessor
{
    public static WindowsArtworkImageProcessor Instance { get; } = new();
    public (int Width, int Height) GetDimensions(byte[] bytes) => PosterImageProcessor.GetDimensions(bytes);
    public byte[] CreatePoster(byte[] bytes, bool png) => PosterImageProcessor.CreatePoster(bytes, png);
    public byte[] CreateFanartJpeg(byte[] bytes) => PosterImageProcessor.CreateFanartJpeg(bytes);
    public byte[] CreateFanartPng(byte[] bytes) => PosterImageProcessor.CreateFanartPng(bytes);
}
