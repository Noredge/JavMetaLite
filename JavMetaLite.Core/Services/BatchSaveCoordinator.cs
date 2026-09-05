using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed record PreparedMovieSave(
    MovieJob Job,
    SavePlan Plan,
    long ReviewRevision,
    bool AllowOverwrite);

public enum BatchSaveItemStatus
{
    Completed,
    Failed,
    Canceled,
    NotStarted
}

public sealed record BatchSaveItemResult(
    PreparedMovieSave Item,
    BatchSaveItemStatus Status,
    OrganizedSaveResult? SaveResult,
    Exception? Error);

public sealed record BatchSaveResult(IReadOnlyList<BatchSaveItemResult> Items)
{
    public int CompletedCount => Items.Count(item => item.Status is BatchSaveItemStatus.Completed);

    public int FailedCount => Items.Count(item => item.Status is BatchSaveItemStatus.Failed);

    public int CanceledCount => Items.Count(item => item.Status is BatchSaveItemStatus.Canceled);

    public int NotStartedCount => Items.Count(item => item.Status is BatchSaveItemStatus.NotStarted);
}

public static class BatchSaveCoordinator
{
    public static async Task<BatchSaveResult> ExecuteAsync(
        IEnumerable<PreparedMovieSave> preparedItems,
        Func<PreparedMovieSave, CancellationToken, Task<OrganizedSaveResult>> executeAsync,
        CancellationToken cancellationToken = default,
        Func<BatchSaveItemResult, Task>? itemFinishedAsync = null)
    {
        ArgumentNullException.ThrowIfNull(preparedItems);
        ArgumentNullException.ThrowIfNull(executeAsync);
        var items = preparedItems.ToArray();
        var results = new BatchSaveItemResult?[items.Length];

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var stopAfterItem = false;
            try
            {
                item.Job.BeginSave(item.ReviewRevision);
                var saveResult = await executeAsync(item, cancellationToken);
                item.Job.UpdateVideoPaths(saveResult.VideoPaths);
                item.Job.MarkSaveCompleted();
                results[index] = new BatchSaveItemResult(
                    item,
                    BatchSaveItemStatus.Completed,
                    saveResult,
                    null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                item.Job.MarkSaveCanceled();
                results[index] = new BatchSaveItemResult(
                    item,
                    BatchSaveItemStatus.Canceled,
                    null,
                    null);
                stopAfterItem = true;
            }
            catch (Exception exception)
            {
                item.Job.MarkSaveFailed(exception, IsConflict(exception));
                results[index] = new BatchSaveItemResult(
                    item,
                    BatchSaveItemStatus.Failed,
                    null,
                    exception);
                stopAfterItem = true;
            }

            if (itemFinishedAsync is not null)
            {
                await itemFinishedAsync(results[index]!);
            }

            if (stopAfterItem)
            {
                break;
            }
        }

        for (var index = 0; index < items.Length; index++)
        {
            results[index] ??= new BatchSaveItemResult(
                items[index],
                BatchSaveItemStatus.NotStarted,
                null,
                null);
        }

        return new BatchSaveResult(results.Select(item => item!).ToArray());
    }

    private static bool IsConflict(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException;
}
