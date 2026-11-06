using System.Diagnostics;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private const int MaximumCompletedOperations = 128;
    private const int MaximumPendingPreviews = 64;
    private const long MaximumOperationReceiptBytes = 32L * 1024 * 1024;
    private const long MaximumPendingPreviewBytes = 16L * 1024 * 1024;
    private static readonly TimeSpan OperationReceiptLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PendingPreviewLifetime = TimeSpan.FromMinutes(15);

    // Call only while holding _gate. These budgets measure serialized DTOs, not process
    // RSS or managed object size. Active work and its receipt remain protected until
    // the Task actually ends; an operation's terminal status alone is insufficient.
    private void PruneRetainedState()
    {
        var now = Stopwatch.GetTimestamp();
        var completed = _operations.Values.Where(CanEvictOperation)
            .OrderBy(op => op.CompletedTimestamp).ToArray();
        var count = completed.Length;
        var bytes = completed.Sum(OperationReceiptBytes);
        foreach (var op in completed)
        {
            if (Stopwatch.GetElapsedTime(op.CompletedTimestamp!.Value, now) <= OperationReceiptLifetime &&
                count <= MaximumCompletedOperations && bytes <= MaximumOperationReceiptBytes) continue;
            // An executed token must never outlive its operation receipt. Removal does
            // not change filesystem state: retries now fail closed with PLAN_STALE.
            var receiptBytes = OperationReceiptBytes(op);
            foreach (var plan in _plans.Values.Where(plan => plan.OperationId == op.Id).ToArray())
                _plans.Remove(plan.Id);
            _operations.Remove(op.Id);
            op.Cancellation.Dispose();
            count--; bytes -= receiptBytes;
        }

        var pending = _plans.Values.Where(plan => plan.OperationId is null)
            .OrderByDescending(plan => plan.Stale).ThenBy(plan => plan.CreatedTimestamp).ToArray();
        count = pending.Length;
        bytes = pending.Sum(plan => plan.RetainedBytes);
        foreach (var plan in pending)
        {
            if (Stopwatch.GetElapsedTime(plan.CreatedTimestamp, now) <= PendingPreviewLifetime &&
                count <= MaximumPendingPreviews && bytes <= MaximumPendingPreviewBytes) continue;
            _plans.Remove(plan.Id);
            count--; bytes -= plan.RetainedBytes;
        }
    }

    private static bool CanEvictOperation(Operation op) =>
        op.Status != "running" && op.Task.IsCompleted && op.CompletedTimestamp.HasValue;

    private long OperationReceiptBytes(Operation op) => op.RetainedBytes +
        _plans.Values.Where(plan => plan.OperationId == op.Id).Sum(plan => plan.RetainedBytes);

    private object RetentionSnapshot() => new
    {
        limits = new
        {
            completedOperations = MaximumCompletedOperations,
            pendingPreviews = MaximumPendingPreviews,
            operationReceiptBytes = MaximumOperationReceiptBytes,
            pendingPreviewBytes = MaximumPendingPreviewBytes,
            operationReceiptSeconds = OperationReceiptLifetime.TotalSeconds,
            pendingPreviewSeconds = PendingPreviewLifetime.TotalSeconds
        },
        operations = _operations.Count,
        completedOperations = _operations.Values.Count(CanEvictOperation),
        protectedOperations = _operations.Values.Count(op => !CanEvictOperation(op)),
        operationReceiptBytes = _operations.Values.Where(CanEvictOperation).Sum(OperationReceiptBytes),
        pendingPreviews = _plans.Values.Count(plan => plan.OperationId is null),
        pendingPreviewBytes = _plans.Values.Where(plan => plan.OperationId is null).Sum(plan => plan.RetainedBytes),
        executedPreviews = _plans.Values.Count(plan => plan.OperationId is not null)
    };
}
