using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class FolderSidecarOwnershipChangedException(string sidecarPath)
    : IOException($"The folder sidecar is no longer assigned to this movie. Reimport the movie before saving.{Environment.NewLine}{sidecarPath}")
{
    public string SidecarPath { get; } = sidecarPath;
}

public static class LocalSidecarLocator
{
    private static readonly string[] ArtworkExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] FolderPosterNames = ["poster", "cover", "folder", "movie", "default"];
    private static readonly string[] FolderFanartNames = ["fanart", "backdrop", "background", "art"];

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
                             ? FindStandaloneArtwork(files, FolderPosterNames)
                             : null) ??
                         FindArtwork(files, baseName, "-poster") ??
                         (!baseName.Equals(videoBaseName, StringComparison.OrdinalIgnoreCase)
                             ? FindArtwork(files, videoBaseName, "-poster")
                             : null);
        var fanartPath = (preferFolderSidecars
                             ? FindStandaloneArtwork(files, FolderFanartNames)
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

    internal static void ValidateWindowsFolderSidecarOwnership(
        string sourceVideoPath, LocalSaveContext? context, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || context is null) return;

        var imported = context.MetadataBundle?.Sidecars;
        var nfo = imported?.NfoPath;
        var poster = context.LocalArtwork?.LocalPosterPath;
        var fanart = context.LocalArtwork?.LocalFanartPath;
        var folderNfo = string.Equals(Path.GetFileName(nfo), "movie.nfo", StringComparison.OrdinalIgnoreCase) ? nfo : null;
        var folderPoster = FolderPosterNames.Contains(Path.GetFileNameWithoutExtension(poster), StringComparer.OrdinalIgnoreCase)
            ? poster : null;
        var folderFanart = FolderFanartNames.Contains(Path.GetFileNameWithoutExtension(fanart), StringComparer.OrdinalIgnoreCase)
            ? fanart : null;
        if (folderNfo is null && folderPoster is null && folderFanart is null) return;

        // Check the original source name before applying any save-time multipart naming.
        var original = MovieFileSet.Create([imported?.VideoPath ?? sourceVideoPath]);
        var fresh = Locate(original.PrimaryPath, original.MovieBaseName, original.UsesMultipartNaming, scope: null, token: token);
        foreach (var (expected, actual) in new[]
                 {
                     (folderNfo, fresh.NfoPath), (folderPoster, fresh.PosterPath), (folderFanart, fresh.FanartPath)
                 })
        {
            if (expected is not null && (actual is null || !FileSystemPathSemantics.PathsEqual(expected, actual)))
                throw new FolderSidecarOwnershipChangedException(expected);
        }
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
