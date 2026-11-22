using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace JavMetaLite.App;

public sealed class QueueThumbnail : Image
{
    public static readonly DependencyProperty VideoPathProperty = PathProperty(nameof(VideoPath));
    public static readonly DependencyProperty PosterPathProperty = PathProperty(nameof(PosterPath));
    public static readonly DependencyProperty CoverPathProperty = PathProperty(nameof(CoverPath));
    public string? VideoPath { get => (string?)GetValue(VideoPathProperty); set => SetValue(VideoPathProperty, value); }
    public string? PosterPath { get => (string?)GetValue(PosterPathProperty); set => SetValue(PosterPathProperty, value); }
    public string? CoverPath { get => (string?)GetValue(CoverPathProperty); set => SetValue(CoverPathProperty, value); }

    private static readonly SemaphoreSlim Workers = new(2);
    private static readonly Dictionary<CacheKey, BitmapSource?> Cache = [];
    private static readonly Queue<CacheKey> CacheOrder = [];
    private CancellationTokenSource? _request;
    private bool _scheduled;

    public QueueThumbnail()
    {
        IsVisibleChanged += (_, _) => Schedule();
        Loaded += (_, _) => Schedule();
        Unloaded += (_, _) => Cancel();
    }

    private static DependencyProperty PathProperty(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(QueueThumbnail),
            new PropertyMetadata(null, (sender, _) => ((QueueThumbnail)sender).Schedule()));

    private void Cancel()
    {
        _request?.Cancel();
        _request = null;
        Source = null;
    }

    private void Schedule()
    {
        Cancel();
        if (_scheduled || !IsVisible || !IsLoaded) return;
        _scheduled = true;
        Dispatcher.InvokeAsync(LoadVisibleAsync, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void LoadVisibleAsync()
    {
        _scheduled = false;
        if (!IsLoaded || !IsVisible || string.IsNullOrEmpty(VideoPath)) return;
        using var request = new CancellationTokenSource();
        _request = request;
        var video = VideoPath;
        var poster = PosterPath;
        var cover = CoverPath;
        try
        {
            await Workers.WaitAsync(request.Token);
            try
            {
                var bitmap = await Task.Run(() => ReadThumbnail(video, poster, cover, request.Token), request.Token);
                if (!request.IsCancellationRequested) Source = bitmap;
            }
            finally { Workers.Release(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }

    private readonly record struct CacheKey(string Video, long VideoTime, string? Poster, long PosterTime, string? Cover, long CoverTime);

    private static BitmapSource? ReadThumbnail(string video, string? poster, string? cover, CancellationToken token)
    {
        try
        {
            static long Timestamp(string? path) => string.IsNullOrEmpty(path) ? 0 : File.GetLastWriteTimeUtc(path).Ticks;
            var key = new CacheKey(video, Timestamp(video), poster, Timestamp(poster), cover, Timestamp(cover));
            lock (Cache)
                if (Cache.TryGetValue(key, out var cached)) return cached;

            token.ThrowIfCancellationRequested();
            var bitmap = ReadCover(poster) ?? ReadCover(cover);
            if (bitmap is null)
            {
                token.ThrowIfCancellationRequested();
                bitmap = ReadShellThumbnail(video);
            }
            lock (Cache)
            {
                if (Cache.TryAdd(key, bitmap)) CacheOrder.Enqueue(key);
                if (CacheOrder.Count > 128) Cache.Remove(CacheOrder.Dequeue());
            }
            return bitmap;
        }
        catch (Exception ex) when (IsThumbnailError(ex)) { return null; }
    }

    private static BitmapSource? ReadCover(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var landscape = frame.PixelWidth >= frame.PixelHeight;
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (landscape) bitmap.DecodePixelWidth = Math.Min(112, frame.PixelWidth);
            else bitmap.DecodePixelHeight = Math.Min(112, frame.PixelHeight);
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (IsThumbnailError(ex)) { return null; }
    }

    private static bool IsThumbnailError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException or FormatException or InvalidOperationException;

    private static BitmapSource? ReadShellThumbnail(string path)
    {
        if (!File.Exists(path)) return null;
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) < 0) return null;
        IntPtr handle = IntPtr.Zero;
        try
        {
            const uint thumbnailOnly = 8;
            if (factory.GetImage(new NativeSize(112, 112), thumbnailOnly, out handle) < 0 || handle == IntPtr.Zero) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            if (handle != IntPtr.Zero) DeleteObject(handle);
            Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
