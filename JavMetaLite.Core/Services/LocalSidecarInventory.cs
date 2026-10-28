using System.Collections.Frozen;
using System.Text;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

// A scope belongs to one import/preview read phase. It never survives publication
// or substitutes for source hashes/revisions/execution-time baseline validation.
internal sealed class LocalSidecarInventoryScope(Action<string, CancellationToken>? afterCapture = null)
{
    private readonly Dictionary<string, Use> _directories = new(FileSystemPathSemantics.PathComparer);

    internal LocalSidecarDirectoryInventory Get(string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_directories.TryGetValue(directory, out var use)) { use.Count++; return use.Inventory; }
        var inventory = LocalSidecarDirectoryInventory.Capture(directory, token);
        _directories.Add(directory, new(inventory));
        afterCapture?.Invoke(directory, token);
        token.ThrowIfCancellationRequested();
        return inventory;
    }

    internal void ValidateReusedDirectories(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Single-use directories keep the standalone locator's fresh scan. Only
        // reuse extends that observation window, so revalidate those inventories.
        foreach (var use in _directories.Values.Where(use => use.Count > 1))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var fresh = LocalSidecarDirectoryInventory.Capture(use.Inventory.DirectoryPath, token);
                if (!use.Inventory.SameNamespace(fresh)) throw new SidecarInventoryChangedException();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new SidecarInventoryChangedException(error); }
        }
    }

    private sealed class Use(LocalSidecarDirectoryInventory inventory)
    {
        internal LocalSidecarDirectoryInventory Inventory { get; } = inventory;
        internal int Count = 1;
    }
}

internal sealed class SidecarInventoryChangedException(Exception? inner = null)
    : IOException("A sidecar directory changed during this operation. Refresh the inputs and try again.", inner);

internal sealed class LocalSidecarDirectoryInventory
{
    private static readonly string[] ArtworkExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    internal string DirectoryPath { get; }
    internal FrozenDictionary<string, string> Files { get; }
    internal FrozenSet<string> VideoGroupKeys { get; }
    internal IReadOnlyList<string> ExtrafanartPaths { get; }
    private readonly NamespaceEntry[] _entries, _extraEntries;
    private readonly bool _extraExists;
    private readonly string? _extraLink, _extraCanonicalPath;

    private LocalSidecarDirectoryInventory(string directory, NamespaceEntry[] entries, NamespaceEntry[] extraEntries,
        bool extraExists, string? extraLink, string? extraCanonicalPath)
    {
        DirectoryPath = directory;
        _entries = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        _extraEntries = extraEntries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        _extraExists = extraExists; _extraLink = extraLink; _extraCanonicalPath = extraCanonicalPath;
        Files = entries.ToDictionary(entry => Path.GetFileName(entry.Path), entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        VideoGroupKeys = Files.Values.Where(VideoFileSupport.HasSupportedExtension)
            .Select(MovieFileSet.GetGroupKey).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        ExtrafanartPaths = Array.AsReadOnly(extraEntries.Select(entry => entry.Path)
            .Where(path => ArtworkExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(GetTrailingNumber).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static LocalSidecarDirectoryInventory Capture(string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        FileSystemPathSemantics.ValidateCanonicalPath(directory);
        var entries = ReadEntries(directory, token);
        if (!OperatingSystem.IsWindows() && entries.GroupBy(entry => entry.ConflictKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new IOException("Ambiguous case or Unicode sidecar names require review before importing this folder.");
        var extra = Path.Combine(directory, "extrafanart");
        var extraExists = Directory.Exists(extra);
        var extraLink = new DirectoryInfo(extra).LinkTarget;
        var extraCanonical = extraExists ? FileSystemPathSemantics.CanonicalizePath(extra) : null;
        // Keep legacy extrafanart ordering/eligibility. Namespace observations also
        // include unrelated entries, but never read/hash their contents.
        var extraEntries = extraExists ? ReadEntries(extra, token, allowUnresolvedLinks: true) : [];
        FileSystemPathSemantics.ValidateCanonicalPath(directory);
        return new(directory, entries, extraEntries, extraExists, extraLink, extraCanonical);
    }

    private static NamespaceEntry[] ReadEntries(string directory, CancellationToken token, bool allowUnresolvedLinks = false)
    {
        var entries = new List<NamespaceEntry>();
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            token.ThrowIfCancellationRequested();
            var link = new FileInfo(path).LinkTarget;
            string key;
            try
            {
                key = OperatingSystem.IsWindows() ? path : link is null
                    ? path.Normalize(NormalizationForm.FormC).ToUpperInvariant() : FileSystemPathSemantics.ConflictKey(path);
            }
            catch (Exception error) when (allowUnresolvedLinks && error is IOException or UnauthorizedAccessException)
            {
                // Legacy extrafanart discovery lists candidates without decoding
                // them. Unrelated/broken links must not block loading a valid NFO;
                // actual selected image reads retain their existing guards.
                key = "unresolved:" + link;
            }
            entries.Add(new(path, link, key));
        }
        return entries.ToArray();
    }

    internal bool SameNamespace(LocalSidecarDirectoryInventory other) =>
        DirectoryPath == other.DirectoryPath && _extraExists == other._extraExists &&
        _extraLink == other._extraLink && _extraCanonicalPath == other._extraCanonicalPath &&
        _entries.SequenceEqual(other._entries) && _extraEntries.SequenceEqual(other._extraEntries);

    private static int GetTrailingNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); var firstDigit = name.Length;
        while (firstDigit > 0 && char.IsDigit(name[firstDigit - 1])) firstDigit--;
        return firstDigit < name.Length && int.TryParse(name[firstDigit..], out var number) ? number : int.MaxValue;
    }
    private sealed record NamespaceEntry(string Path, string? LinkTarget, string ConflictKey);
}
