using System.Windows.Media.Imaging;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

internal sealed record ArtworkPreview(BitmapSource Bitmap, int Width, int Height);

internal static class ArtworkPreviewLoader
{
    // Main panel is much smaller than the viewer. This never feeds saving or source comparison.
    internal const int MainPreviewWidth = 640;

    public static Task<ArtworkPreview> LoadLocalAsync(string path, CancellationToken token) => Task.Run(async () =>
    {
        var image = await ArtworkLocationHelper.ReadLocalImageWithDimensionsAsync(path, token);
        token.ThrowIfCancellationRequested();
        var bitmap = PosterBitmapFactory.CreateFrozen(image.Bytes, Math.Min(MainPreviewWidth, image.Width));
        token.ThrowIfCancellationRequested();
        return new ArtworkPreview(bitmap, image.Width, image.Height);
    }, token);
}
