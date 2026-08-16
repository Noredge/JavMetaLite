using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class LocalSidecarLocator
{
    private static readonly string[] ArtworkExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static LocalSidecarPaths Locate(string videoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);

        var fullVideoPath = Path.GetFullPath(videoPath);
        if (!File.Exists(fullVideoPath))
        {
            throw new FileNotFoundException("影片文件不存在。", fullVideoPath);
        }

        var directory = Path.GetDirectoryName(fullVideoPath)
            ?? throw new InvalidOperationException("无法确定影片所在目录。");
        var baseName = Path.GetFileNameWithoutExtension(fullVideoPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new InvalidDataException("影片文件名无效。");
        }

        var files = Directory.EnumerateFiles(directory)
            .Select(path => (Name: Path.GetFileName(path), Path: path))
            .Where(file => !string.IsNullOrWhiteSpace(file.Name))
            .ToDictionary(file => file.Name!, file => file.Path, StringComparer.OrdinalIgnoreCase);
        files.TryGetValue($"{baseName}.nfo", out var nfoPath);

        return new LocalSidecarPaths(
            fullVideoPath,
            nfoPath,
            FindArtwork(files, baseName, "-poster"),
            FindArtwork(files, baseName, "-fanart"));
    }

    private static string? FindArtwork(
        IReadOnlyDictionary<string, string> files,
        string baseName,
        string suffix)
    {
        foreach (var extension in ArtworkExtensions)
        {
            if (files.TryGetValue($"{baseName}{suffix}{extension}", out var path))
            {
                return path;
            }
        }

        return null;
    }
}
