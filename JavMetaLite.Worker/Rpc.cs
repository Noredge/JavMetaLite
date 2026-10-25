using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using JavMetaLite.Core.Services;

namespace JavMetaLite.Worker;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };
    public static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
}

internal sealed class RpcError(int code, string reason, string message, object? detail = null) : Exception(message)
{
    public int Code { get; } = code;
    public string Reason { get; } = reason;
    public object? Detail { get; } = detail;
    public object Wire => new { code = Code, message = Message, data = new
    { reason = Reason, retryable = Reason is "REVISION_CONFLICT" or "PLAN_STALE" or "SOURCE_CHANGED", requiresNewPreview = Reason is "PLAN_STALE" or "SOURCE_CHANGED", detail = Detail } };
    public static RpcError From(Exception error) => error switch
    {
        RpcError rpc => rpc,
        BrowserPageImportException page => new(-32010, page.Reason, page.Message),
        FileRecoveryException recovery => new(-32010, "RECOVERY_REQUIRED", "File recovery needs inspection.", recovery.RecoveryPaths),
        TemporaryCleanupException cleanup => new(-32010, "RECOVERY_REQUIRED", "Temporary files need inspection.", cleanup.Issues),
        UnauthorizedAccessException => new(-32010, "ACCESS_DENIED", "Access to the requested file was denied."),
        FileNotFoundException or DirectoryNotFoundException => new(-32010, "VOLUME_UNAVAILABLE", "The file or volume is no longer available."),
        MetadataSourceRateLimitException => new(-32010, "PROVIDER_RATE_LIMITED", "The provider is rate limited. Try again later."),
        OperationCanceledException => new(-32010, "CANCELED", "Operation canceled safely."),
        ArgumentException or InvalidOperationException or InvalidDataException or KeyNotFoundException => new(-32602, "INVALID_PARAMS", error.Message),
        _ => new(-32010, "OPERATION_FAILED", "The operation failed; edits have been retained.")
    };
}

internal sealed class RpcWriter : IAsyncDisposable
{
    private readonly Channel<byte[]> _queue = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true });
    private readonly Task _pump;
    public RpcWriter(Stream stream) => _pump = Task.Run(async () =>
    {
        try { await foreach (var message in _queue.Reader.ReadAllAsync())
        { await stream.WriteAsync(message); await stream.WriteAsync(new byte[] { 10 }); await stream.FlushAsync(); } }
        catch (IOException) { /* Parent disappeared; transaction shutdown still runs. */ }
    });
    public ValueTask Send(object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.Options);
        if (bytes.Length > Framing.MaximumMessageBytes) throw new RpcError(-32010, "MESSAGE_TOO_LARGE", "Response exceeds protocol limit.");
        return _queue.Writer.WriteAsync(bytes);
    }
    public ValueTask Error(string? id, RpcError error) => Send(new Dictionary<string, object?>
    { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error.Wire });
    public async ValueTask DisposeAsync() { _queue.Writer.TryComplete(); await _pump; }
}
