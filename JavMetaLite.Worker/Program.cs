using System.Text;
using System.Text.Json;
using JavMetaLite.Worker;

Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false);
string? dataRoot = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--data-root" && i + 1 < args.Length) dataRoot = Path.GetFullPath(args[++i]);
    else if (args[i] == "--diagnostic-stderr-bytes" && i + 1 < args.Length)
    {
        var bytes = Math.Clamp(int.Parse(args[++i]), 0, 16 * 1024 * 1024);
        await Console.Error.WriteAsync(new string('d', bytes) + "\n");
    }
    else throw new ArgumentException("Unknown Worker argument.");
}
await using var wire = new RpcWriter(Console.OpenStandardOutput());
await using var workspace = new Workspace(wire, dataRoot);
try
{
    await foreach (var line in Framing.ReadLines(Console.OpenStandardInput()))
    {
        string? id = null;
        try
        {
            if (line is null) throw new RpcError(-32600, "MESSAGE_TOO_LARGE", "Message exceeds 8 MiB.");
            using var document = JsonDocument.Parse(line);
            var request = document.RootElement;
            if (request.ValueKind != JsonValueKind.Object ||
                !request.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0" ||
                !request.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                throw new RpcError(-32600, "INVALID_REQUEST", "Expected a JSON-RPC 2.0 request.");
            var hasId = request.TryGetProperty("id", out var requestId);
            if (hasId && requestId.ValueKind != JsonValueKind.String)
                throw new RpcError(-32600, "INVALID_REQUEST", "Request IDs must be strings.");
            id = hasId ? requestId.GetString() : null;
            // This application protocol requires IDs for all state-changing commands.
            if (id is null) continue;
            var parameters = request.TryGetProperty("params", out var value) ? value.Clone() : JsonDefaults.Empty;
            if (parameters.ValueKind != JsonValueKind.Object)
                throw new RpcError(-32602, "INVALID_PARAMS", "Parameters must be an object.");
            var result = await workspace.Dispatch(method.GetString()!, parameters);
            await wire.Send(new { jsonrpc = "2.0", id, result });
            if (workspace.ShuttingDown) break;
        }
        catch (JsonException) { await wire.Error(id, new RpcError(-32700, "PARSE_ERROR", "Invalid JSON.")); }
        catch (Exception error)
        {
            JavMetaLite.Core.Services.AppLog.Error($"request={id ?? "invalid"} reason={RpcError.From(error).Reason}", error);
            await wire.Error(id, RpcError.From(error));
        }
    }
}
catch (IOException) { /* Parent closed its pipes. Finish active work safely below. */ }
finally { await workspace.Stop(); }

namespace JavMetaLite.Worker
{
    internal static class Framing
    {
        internal const int MaximumMessageBytes = 8 * 1024 * 1024;
        internal static async IAsyncEnumerable<byte[]?> ReadLines(Stream input)
        {
            var buffer = new byte[64 * 1024];
            using var line = new MemoryStream();
            var oversized = false;
            int count;
            while ((count = await input.ReadAsync(buffer)) > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == 10)
                    {
                        yield return oversized ? null : line.ToArray();
                        line.SetLength(0); oversized = false;
                    }
                    else if (!oversized)
                    {
                        if (line.Length >= MaximumMessageBytes) { oversized = true; line.SetLength(0); }
                        else line.WriteByte(buffer[i]);
                    }
                }
            }
            // A non-terminated final frame is rejected; EOF is never a save command.
            if (line.Length > 0 || oversized) yield return null;
        }
    }
}
