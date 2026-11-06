namespace JavMetaLite.Core.Services;

/// <summary>Only decoding and encoding depend on a platform image library.</summary>
public interface IArtworkImageProcessor
{
    (int Width, int Height) GetDimensions(byte[] bytes);
    byte[] CreatePoster(byte[] sourceBytes, bool png);
    byte[] CreateFanartJpeg(byte[] sourceBytes);
    byte[] CreateFanartPng(byte[] sourceBytes);
}
