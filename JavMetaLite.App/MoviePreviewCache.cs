using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

internal sealed record MoviePreviewState(ImageSource? Poster, ImageSource? Fanart,
    (int Width, int Height)? FanartDimensions, Visibility DropHintVisibility)
{
    // Estimated decoded pixel storage, not a promise about total process/native memory.
    public long DecodedBytes => Size(Poster) + Size(Fanart);
    private static long Size(ImageSource? image) => image is BitmapSource bitmap
        ? ((long)bitmap.PixelWidth * Math.Max(32, bitmap.Format.BitsPerPixel) + 7) / 8 * bitmap.PixelHeight
        : 0;
}

internal sealed class MoviePreviewCache(int maximumEntries = 12, long maximumDecodedBytes = 32 * 1024 * 1024)
{
    private readonly Dictionary<MovieJob, LinkedListNode<(MovieJob Job, MoviePreviewState Preview)>> _entries = [];
    private readonly LinkedList<(MovieJob Job, MoviePreviewState Preview)> _recency = [];
    public int Count => _entries.Count;
    public long DecodedBytes { get; private set; }
    public bool Contains(MovieJob job) => _entries.ContainsKey(job);

    public void Store(MovieJob job, MoviePreviewState preview)
    {
        Remove(job);
        if (maximumEntries <= 0 || preview.DecodedBytes > maximumDecodedBytes) return;
        _entries.Add(job, _recency.AddLast((job, preview)));
        DecodedBytes += preview.DecodedBytes;
        while (Count > maximumEntries || DecodedBytes > maximumDecodedBytes)
            Remove(_recency.First!.Value.Job);
    }

    public bool TryGetValue(MovieJob job, out MoviePreviewState preview)
    {
        if (!_entries.TryGetValue(job, out var node)) { preview = null!; return false; }
        _recency.Remove(node);
        _recency.AddLast(node);
        preview = node.Value.Preview;
        return true;
    }

    public void Remove(MovieJob job)
    {
        if (!_entries.Remove(job, out var node)) return;
        _recency.Remove(node);
        DecodedBytes -= node.Value.Preview.DecodedBytes;
    }

    public void Clear() { _entries.Clear(); _recency.Clear(); DecodedBytes = 0; }
}
