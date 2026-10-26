using System.Text;

namespace JavMetaLite.Core.Services;

public enum FileIdentityRelation { Same, Different, Unknown }
public enum VolumeRelation { Same, Different, Unknown }
public enum PathCaseSensitivity { Sensitive, Insensitive, Unknown }

/// <summary>
/// Conservative path policy shared by discovery, plans and transactions. Lexical comparison
/// never claims that two differently spelled Unix paths are the same file. Unknown volume
/// identity always takes verified-copy; ambiguous aliases are refused before destructive work.
/// </summary>
public static class FileSystemPathSemantics
{
    public static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool PathsEqual(string left, string right) => PathComparer.Equals(
        Path.TrimEndingDirectorySeparator(CanonicalizePath(left)),
        Path.TrimEndingDirectorySeparator(CanonicalizePath(right)));

    public static FileIdentityRelation CompareExistingFiles(string left, string right)
    {
        if (!File.Exists(left) || !File.Exists(right) || HasSymbolicLinkComponent(left) || HasSymbolicLinkComponent(right))
            return FileIdentityRelation.Unknown;
        return PathsEqual(left, right) ? FileIdentityRelation.Same : FileIdentityRelation.Unknown;
    }

    public static PathCaseSensitivity GetCaseSensitivity(string existingParent) =>
        OperatingSystem.IsWindows() ? PathCaseSensitivity.Insensitive : PathCaseSensitivity.Unknown;

    public static VolumeRelation GetVolumeRelation(string source, string destination)
    {
        if (!OperatingSystem.IsWindows()) return VolumeRelation.Unknown;
        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(source));
        var targetRoot = Path.GetPathRoot(Path.GetFullPath(destination));
        if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrEmpty(targetRoot)) return VolumeRelation.Unknown;
        return StringComparer.OrdinalIgnoreCase.Equals(sourceRoot, targetRoot) ? VolumeRelation.Same : VolumeRelation.Different;
    }

    // This deliberately over-blocks cross-task aliases when the volume's case mode is unknown.
    // It is only a conflict key; it must never authorize merging, moving, or replacing a file.
    public static string ConflictKey(string path) => CanonicalizePath(path).Normalize(NormalizationForm.FormC).ToUpperInvariant();

    /// <summary>Resolve existing symlink components without changing real filename spelling.</summary>
    public static string CanonicalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) return fullPath;
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var component in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException($"Cannot resolve symbolic-link input: {current}");
        }
        return current;
    }

    public static bool HasSymbolicLinkComponent(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) return true;
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
        return false;
    }

    /// <summary>A prepared canonical path must not gain symlink ancestors before execution.</summary>
    public static void ValidateCanonicalPath(string path)
    {
        if (!OperatingSystem.IsWindows() && !PathComparer.Equals(Path.GetFullPath(path), CanonicalizePath(path)))
            throw new IOException($"A prepared path was redirected through a symbolic link: {path}");
    }

    /// <summary>Validate an existing container without reading its parent on macOS.
    /// File/new-directory targets still require ValidateUnambiguousPath.</summary>
    public static void ValidateExistingContainer(string path)
    {
        if (!OperatingSystem.IsMacOS()) { ValidateUnambiguousPath(path); return; }
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Selected directory is unavailable: {fullPath}");
        if (new DirectoryInfo(fullPath).LinkTarget is not null)
            throw new IOException($"Symbolic-link directories require explicit resolution: {fullPath}");
        fullPath = Path.TrimEndingDirectorySeparator(CanonicalizePath(fullPath));
        if (fullPath == Path.GetPathRoot(fullPath)) return;
        if (!StringComparer.Ordinal.Equals(MacDirectoryName.Read(fullPath), Path.GetFileName(fullPath)))
            throw new IOException($"Ambiguous case or Unicode directory alias; use its exact existing name: {fullPath}");
    }

    public static void ValidateUnambiguousPath(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        var fullPath = Path.GetFullPath(path);
        // Canonicalize ordinary parent aliases such as macOS /var -> /private/var.
        // A symlink at the actual file/directory boundary remains an ambiguous write target.
        FileSystemInfo supplied = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
        if (supplied.LinkTarget is not null)
            throw new IOException($"Symbolic-link paths require explicit resolution before saving: {fullPath}");
        fullPath = CanonicalizePath(fullPath);
        var parent = Path.GetDirectoryName(fullPath);
        if (parent is null || !Directory.Exists(parent)) return;
        var key = ConflictKey(fullPath);
        var matches = Directory.EnumerateFileSystemEntries(parent)
            .Where(entry => ConflictKey(entry) == key).ToArray();
        // Different case/Unicode spelling is not sufficient evidence of filesystem identity.
        if (matches.Any(entry => !PathsEqual(entry, fullPath)))
            throw new IOException($"Ambiguous case or Unicode path alias; use the exact existing name: {fullPath}");
    }
}
