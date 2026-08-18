using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JavMetaLite.Core.Services;

namespace JavMetaLite.RegressionTests;

internal sealed record RegressionTestCase(string Category, string Name, Func<Task> Run);

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), $"JavMetaLite.Regression.{name}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        AppLog.ConfigureDirectory(Path.Combine(Root, "logs"));
    }

    public string Root { get; }

    public string PathOf(params string[] segments) =>
        segments.Aggregate(Root, Path.Combine);

    public string CreateDirectory(params string[] segments)
    {
        var path = PathOf(segments);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteFile(string relativePath, byte[] bytes)
    {
        var path = PathOf(relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void AssertNoTemporaryArtifacts()
    {
        var leftovers = Directory
            .EnumerateFileSystemEntries(Root, "*.tmp", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).StartsWith(".JavMetaLite-", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .ToArray();
        AssertEx.Equal(0, leftovers.Length, $"Temporary artifacts remain: {string.Join(", ", leftovers)}");
    }

    public void Dispose()
    {
        AppLog.ConfigureDirectory(null);
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}

internal static class AssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message ?? $"Expected '{expected}', got '{actual}'.");
        }
    }

    public static void FileExists(string path) =>
        True(File.Exists(path), $"Expected file does not exist: {path}");

    public static void FileDoesNotExist(string path) =>
        False(File.Exists(path), $"Unexpected file exists: {path}");

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(message);
    }

    public static TException Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(message);
    }

    public static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

internal sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

internal static class TestImageFactory
{
    public static byte[] CreateJpeg(int width = 80, int height = 54)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x58;
            pixels[index + 1] = 0x72;
            pixels[index + 2] = 0xA4;
            pixels[index + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

internal sealed class StaticImageHandler(byte[] imageBytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
