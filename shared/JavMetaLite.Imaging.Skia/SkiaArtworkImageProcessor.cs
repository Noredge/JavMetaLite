using JavMetaLite.Core.Services;
using SkiaSharp;

namespace JavMetaLite.Imaging.Skia;

/// <summary>Portable decoder. Preserves the WPF raw-pixel orientation and right-half crop contract.</summary>
public sealed class SkiaArtworkImageProcessor : IArtworkImageProcessor
{
    public const int MaximumEncodedBytes = 100 * 1024 * 1024;
    public const long MaximumPixels = 40_000_000;

    public (int Width, int Height) GetDimensions(byte[] bytes)
    {
        using var bitmap = Decode(bytes);
        return (bitmap.Width, bitmap.Height);
    }

    public byte[] CreatePoster(byte[] sourceBytes, bool png)
    {
        using var source = Decode(sourceBytes);
        if (source.Width <= source.Height) return Encode(source, png);
        var width = Math.Max(1, source.Width / 2);
        using var poster = new SKBitmap(width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(poster))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, new SKRect(source.Width - width, 0, source.Width, source.Height),
                new SKRect(0, 0, width, source.Height));
        }
        return Encode(poster, png);
    }

    public byte[] CreateFanartJpeg(byte[] sourceBytes)
    {
        using var source = Decode(sourceBytes);
        // Existing JPEG fanart remains byte-for-byte unchanged, including EXIF metadata.
        if (sourceBytes.Length >= 3 && sourceBytes[0] == 0xff && sourceBytes[1] == 0xd8 && sourceBytes[2] == 0xff)
            return sourceBytes.ToArray();
        return Encode(source, false);
    }

    public byte[] CreateFanartPng(byte[] sourceBytes)
    {
        using var source = Decode(sourceBytes);
        return Encode(source, true);
    }

    private static SKBitmap Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > MaximumEncodedBytes)
            throw new InvalidDataException("Image encoded size is outside the supported limit.");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Image format is invalid or unsupported.");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > MaximumPixels)
            throw new InvalidDataException("Image dimensions exceed the supported pixel limit.");
        // SKCodec exposes encoded pixels; do not silently rotate EXIF relative to Windows.
        var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new InvalidDataException($"Image decoding failed ({result}).");
        }
        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, bool png)
    {
        // JPEG has no alpha. Flatten transparent pixels on white instead of dropping alpha into black.
        using var flattened = png ? null : new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (flattened is not null)
        {
            using var canvas = new SKCanvas(flattened);
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        using var image = SKImage.FromBitmap(flattened ?? bitmap);
        using var encoded = image.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 95)
            ?? throw new InvalidDataException("Image encoding failed.");
        return encoded.ToArray();
    }
}
