using System.IO;
using System.Windows.Media.Imaging;

namespace JavMetaLite.App;

public static class PosterBitmapFactory
{
    public static BitmapImage CreateFrozen(byte[] imageBytes, int decodePixelWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("图片内容为空。", nameof(imageBytes));
        }

        using var stream = new MemoryStream(imageBytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
