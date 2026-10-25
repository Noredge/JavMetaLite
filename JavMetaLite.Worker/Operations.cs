using System.Text.Json;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private object StartOperation(string? jobId, Func<Operation, Task<object>> action, Action<Operation>? beforeStart = null, bool readOnly = false)
    {
        lock (_gate)
        {
            if (ShuttingDown) throw new RpcError(-32010, "WORKER_UNAVAILABLE", "The engine is stopping safely.");
            var op = new Operation(jobId, readOnly);
            beforeStart?.Invoke(op);
            _operations.Add(op.Id, op);
            PruneRetainedState();
            op.Task = Task.Run(async () =>
            {
                try
                {
                    var result = await action(op);
                    lock (_gate) { op.Result = result; op.Status = "completed"; }
                }
                catch (OperationCanceledException) { lock (_gate) { op.Status = "canceled"; op.Error = RpcError.From(new OperationCanceledException()).Wire; } }
                catch (Exception error)
                {
                    await Console.Error.WriteLineAsync($"operation={op.Id} reason={RpcError.From(error).Reason}");
                    JavMetaLite.Core.Services.AppLog.Error($"operation={op.Id}", error);
                    lock (_gate) { op.Status = "failed"; op.Error = RpcError.From(error).Wire; }
                }
                finally
                {
                    lock (_gate)
                    {
                        op.Phase = op.Status; op.Sequence++;
                        try { _ = _wire.Send(new { jsonrpc = "2.0", method = "operation.finished", @params = OperationSnapshot(op) }); }
                        catch (RpcError error) when (error.Reason == "MESSAGE_TOO_LARGE")
                        {
                            // Completion is still real. Preserve its status and let the client recover
                            // jobs through workspace.getSnapshot instead of faulting safe shutdown.
                            op.Result = new { resultOmitted = true, requiresSnapshot = true };
                            _ = _wire.Send(new { jsonrpc = "2.0", method = "operation.finished", @params = OperationSnapshot(op) });
                        }
                        op.CompletedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                        op.RetainedBytes = JsonSerializer.SerializeToUtf8Bytes(OperationSnapshot(op), JsonDefaults.Options).LongLength;
                        // This operation's Task is still finishing, so pruning protects it.
                        PruneRetainedState();
                    }
                }
            });
            return new { operationId = op.Id };
        }
    }

    private void Progress(Operation op, string phase, object? details = null)
    {
        lock (_gate)
        {
            op.Sequence++; op.Phase = phase;
            _ = _wire.Send(new { jsonrpc = "2.0", method = "operation.progress", @params = new
            { operationId = op.Id, jobId = op.JobId, sequence = op.Sequence, phase, canCancel = true, details } });
        }
    }

    private static object OperationSnapshot(Operation op) => new
    { operationId = op.Id, jobId = op.JobId, readOnly = op.ReadOnly, status = op.Status, sequence = op.Sequence, phase = op.Phase, result = op.Result, error = op.Error };

    public async Task Stop()
    {
        Task[] tasks;
        lock (_gate)
        {
            ShuttingDown = true;
            foreach (var op in _operations.Values.Where(x => x.Status == "running")) op.Cancellation.Cancel();
            tasks = _operations.Values.Select(x => x.Task).ToArray();
        }
        await Task.WhenAll(tasks);
    }
    public async ValueTask DisposeAsync()
    {
        await Stop();
        foreach (var entry in _jobs.Values) entry.Job.Dispose();
        foreach (var op in _operations.Values) op.Cancellation.Dispose();
        _http.Dispose();
        _searchSlots.Dispose();
        _artworkSlots.Dispose();
        // Only this session's generated preview cache is disposable. Recovery is never touched.
        try { Directory.Delete(_cacheRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class Operation(string? jobId, bool readOnly)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string? JobId { get; } = jobId;
        public bool ReadOnly { get; } = readOnly;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
        public string Status = "running";
        public string Phase = "queued";
        public long Sequence;
        public object? Result;
        public object? Error;
        public long? CompletedTimestamp;
        public long RetainedBytes;
    }
}
