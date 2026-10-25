using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

// Compatibility adapter: all read/write and extrafanart conflict rules live in Core.
internal static class BatchSavePathConflicts
{
    internal static IReadOnlyDictionary<MovieJob, IReadOnlyList<string>> Find(
        IReadOnlyList<BatchSavePreviewItem> items, IReadOnlyList<BatchSavePreviewItem>? protectedItems = null) =>
        JavMetaLite.Core.Services.BatchSavePathConflicts.Find(
            items.Select(item => new BatchSavePlanItem(item.Job, item.Plan, item.ReviewRevision)).ToArray(),
            protectedItems?.Select(item => new BatchSavePlanItem(item.Job, item.Plan, item.ReviewRevision)).ToArray());
}
