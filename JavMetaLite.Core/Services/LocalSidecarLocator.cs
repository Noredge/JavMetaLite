using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class LocalSidecarLocator
{
    private static readonly string[] ArtworkExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static LocalSidecarPaths Locate(
        string videoPath,
        string? preferredBaseName = null,
        bool preferFolderSidecars = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);

        var fullVideoPath = Path.GetFullPath(videoPath);
        if (!File.Exists(fullVideoPath))
        {
            throw new FileNotFoundException("影片文件不存在。", fullVideoPath);
        }

        var directory = Path.GetDirectoryName(fullVideoPath)
            ?? throw new InvalidOperationException("无法确定影片所在目录。");
        var videoBaseName = Path.GetFileNameWithoutExtension(fullVideoPath);
        var baseName = string.IsNullOrWhiteSpace(preferredBaseName)
            ? videoBaseName
            : preferredBaseName.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new InvalidDataException("影片文件名无效。");
        }

        var files = Directory.EnumerateFiles(directory)
            .Select(path => (Name: Path.GetFileName(path), Path: path))
            .Where(file => !string.IsNullOrWhiteSpace(file.Name))
            .ToDictionary(file => file.Name!, file => file.Path, StringComparer.OrdinalIgnoreCase);
        string? nfoPath = null;
        if (preferFolderSidecars)
        {
            files.TryGetValue("movie.nfo", out nfoPath);
        }
        nfoPath ??= files.TryGetValue($"{baseName}.nfo", out var preferredNfoPath)
            ? preferredNfoPath
            : null;
        nfoPath ??= !baseName.Equals(videoBaseName, StringComparison.OrdinalIgnoreCase) &&
                    files.TryGetValue($"{videoBaseName}.nfo", out var legacyNfoPath)
            ? legacyNfoPath
            : null;

        var posterPath = (preferFolderSidecars
                             ? FindStandaloneArtwork(files, ["poster", "cover", "folder", "movie", "default"])
                             : null) ??
                         FindArtwork(files, baseName, "-poster") ??
                         (!baseName.Equals(videoBaseName, StringComparison.OrdinalIgnoreCase)
                             ? FindArtwork(files, videoBaseName, "-poster")
                             : null);
        var fanartPath = (preferFolderSidecars
                             ? FindStandaloneArtwork(files, ["fanart", "backdrop", "background", "art"])
                             : null) ??
                         FindArtwork(files, baseName, "-fanart") ??
                         (!baseName.Equals(videoBaseName, StringComparison.OrdinalIgnoreCase)
                             ? FindArtwork(files, videoBaseName, "-fanart")
                             : null);

        return new LocalSidecarPaths(
            fullVideoPath,
            nfoPath,
            posterPath,
            fanartPath)
        {
            ExtrafanartPaths = FindExtrafanart(directory)
        };
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

    private static string? FindStandaloneArtwork(
        IReadOnlyDictionary<string, string> files,
        IEnumerable<string> baseNames)
    {
        foreach (var baseName in baseNames)
        {
            foreach (var extension in ArtworkExtensions)
            {
                if (files.TryGetValue($"{baseName}{extension}", out var path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> FindExtrafanart(string movieDirectory)
    {
        var directory = Path.Combine(movieDirectory, "extrafanart");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ArtworkExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(GetTrailingNumber)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetTrailingNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var firstDigit = name.Length;
        while (firstDigit > 0 && char.IsDigit(name[firstDigit - 1]))
        {
            firstDigit--;
        }

        return firstDigit < name.Length && int.TryParse(name[firstDigit..], out var number)
            ? number
            : int.MaxValue;
    }
}
