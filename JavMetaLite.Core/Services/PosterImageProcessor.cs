using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace JavMetaLite.Core.Services;

public static class PosterImageProcessor
{
    public static byte[] CreatePosterJpeg(byte[] sourceBytes)
    {
        var source = Decode(sourceBytes);
        BitmapSource poster = source;

        if (source.PixelWidth > source.PixelHeight)
        {
            var targetWidth = Math.Max(1, source.PixelWidth / 2);
            var crop = new Int32Rect(
                source.PixelWidth - targetWidth,
                0,
                targetWidth,
                source.PixelHeight);
            poster = new CroppedBitmap(source, crop);
        }

        return EncodeJpeg(poster);
    }

    public static byte[] CreateFanartJpeg(byte[] sourceBytes)
    {
        var source = Decode(sourceBytes);
        if (sourceBytes.Length >= 3 && sourceBytes[0] == 0xFF && sourceBytes[1] == 0xD8 && sourceBytes[2] == 0xFF)
        {
            return sourceBytes.ToArray();
        }

        return EncodeJpeg(source);
    }

    public static (int Width, int Height) GetDimensions(byte[] imageBytes)
    {
        var source = Decode(imageBytes);
        return (source.PixelWidth, source.PixelHeight);
    }

    private static BitmapFrame Decode(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("图片内容为空。", nameof(imageBytes));
        }

        using var stream = new MemoryStream(imageBytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static byte[] EncodeJpeg(BitmapSource source)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
