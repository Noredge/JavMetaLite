using System.IO;

namespace JavMetaLite.App;

internal static class DuplicateMovieResolution
{
    internal static IReadOnlyList<IReadOnlyList<BatchSavePreviewItem>> Groups(IReadOnlyList<BatchSavePreviewItem> items) =>
        items.Where(item => item.Plan.VideoTransfers.Count == 1)
            .GroupBy(item => Path.ChangeExtension(Path.GetFullPath(item.Plan.VideoTransfers[0].TargetPath), null), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => (IReadOnlyList<BatchSavePreviewItem>)group.ToArray()).ToArray();

    internal static bool CanCombine(IReadOnlyList<BatchSavePreviewItem> items) =>
        items.Count > 1 && items.All(item => item.Job.VideoPaths.Count == 1 &&
            item.Job.SaveConfiguration is not null && item.Job.SaveConfiguration == items[0].Job.SaveConfiguration &&
            string.Equals(item.Job.Metadata.Id, items[0].Job.Metadata.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(item.Job.VideoPath), Path.GetDirectoryName(items[0].Job.VideoPath), StringComparison.OrdinalIgnoreCase)) &&
        items.Select(item => item.Job.VideoPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count;
}
