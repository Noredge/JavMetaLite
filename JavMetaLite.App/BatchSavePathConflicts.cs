using System.IO;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

// Plans are prepared before any movie runs. Compare them as a batch, including the
// files one plan will read/keep and another will replace or retire. Read/read sharing
// and directory creation alone are safe; no output naming changes are needed.
internal static class BatchSavePathConflicts
{
    internal static IReadOnlyDictionary<MovieJob, IReadOnlyList<string>> Find(
        IReadOnlyList<BatchSavePreviewItem> items)
    {
        var uses = new Dictionary<string, Dictionary<MovieJob, bool>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var change in item.Plan.Changes.Where(change => change.Kind != PlannedChangeKind.CreateFolder))
            {
                Add(item.Job, change.DestinationPath, change.Kind != PlannedChangeKind.KeepFile);
                Add(item.Job, change.SourcePath, false);
            }
            foreach (var transfer in item.Plan.VideoTransfers)
            {
                Add(item.Job, transfer.SourcePath, transfer.WillMove);
                Add(item.Job, transfer.TargetPath, transfer.WillMove);
            }
            foreach (var transfer in item.Plan.SidecarTransfers)
            {
                Add(item.Job, transfer.SourcePath, false);
                Add(item.Job, transfer.DestinationPath, true);
            }
            foreach (var expectation in item.Plan.SourceFileExpectations) Add(item.Job, expectation.Path, false);
            foreach (var path in item.Plan.SourcePathsToRetire) Add(item.Job, path, true);
        }

        var conflicts = new Dictionary<MovieJob, List<string>>();
        foreach (var (path, owners) in uses.Where(pair => pair.Value.Count > 1 && pair.Value.Values.Any(writes => writes)))
        {
            foreach (var job in owners.Keys)
            {
                if (!conflicts.TryGetValue(job, out var paths)) conflicts[job] = paths = [];
                paths.Add(path);
            }
        }
        return conflicts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);

        void Add(MovieJob job, string? path, bool writes)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var fullPath = Path.GetFullPath(path);
            Record(fullPath);
            // Jellyfin's extrafanart belongs to the movie directory, not a video basename.
            // Disjoint filenames would still mix two movies' samples in that namespace.
            var directory = Path.GetDirectoryName(fullPath);
            if (string.Equals(Path.GetFileName(directory), "extrafanart", StringComparison.OrdinalIgnoreCase))
                Record(directory!);

            void Record(string location)
            {
                if (!uses.TryGetValue(location, out var owners)) uses[location] = owners = [];
                owners[job] = writes || owners.GetValueOrDefault(job);
            }
        }
    }
}
