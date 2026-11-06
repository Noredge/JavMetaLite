using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class LocalSidecarLocator
{
    private static readonly string[] ArtworkExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static LocalSidecarPaths Locate(
        string videoPath,
        string? preferredBaseName = null,
        bool preferFolderSidecars = false)
        => Locate(videoPath, preferredBaseName, preferFolderSidecars, null, CancellationToken.None);

    internal static LocalSidecarPaths Locate(string videoPath, string? preferredBaseName,
        bool preferFolderSidecars, LocalSidecarInventoryScope? scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);

        var fullVideoPath = FileSystemPathSemantics.CanonicalizePath(videoPath);
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

        var inventory = scope?.Get(directory, token) ?? LocalSidecarDirectoryInventory.Capture(directory, token);
        var files = inventory.Files;
        // Eligibility is independent of the requested movie and is computed once
        // per directory instead of rescanning every sibling for every movie.
        var groupKey = MovieFileSet.GetGroupKey(fullVideoPath);
        var canUseFolderSidecars = inventory.VideoGroupKeys.Count == 0 ||
            inventory.VideoGroupKeys.Count == 1 && inventory.VideoGroupKeys.Contains(groupKey);
        preferFolderSidecars &= canUseFolderSidecars;
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

        if (canUseFolderSidecars && nfoPath is null)
            files.TryGetValue("movie.nfo", out nfoPath);

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
        if (canUseFolderSidecars)
        {
            posterPath ??= FindStandaloneArtwork(files, ["poster"]);
            fanartPath ??= FindStandaloneArtwork(files, ["fanart"]);
        }

        return new LocalSidecarPaths(
            fullVideoPath,
            nfoPath,
            posterPath,
            fanartPath)
        {
            ExtrafanartPaths = inventory.ExtrafanartPaths
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

}
