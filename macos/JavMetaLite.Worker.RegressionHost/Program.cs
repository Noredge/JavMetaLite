using System.Net;
using System.Text;
using System.Text.Json;
using JavMetaLite.Core.Services;
using JavMetaLite.Core.Models;
using JavMetaLite.Worker;

// This separate, non-publishable test host runs the production Workspace and real
// provider parsers/scheduler over fixed HTTP responses. No fixture method or HTTP
// injection option is accepted by the packaged Worker.
Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false);
string? ArgumentOrEnvironment(string argument, string environment)
{
    var index = Array.IndexOf(args, argument);
    if (index >= 0) return index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing value: " + argument);
    return Environment.GetEnvironmentVariable(environment);
}
var dataRoot = ArgumentOrEnvironment("--data-root", "JAVMETALITE_REGRESSION_DATA_ROOT");
if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot)) throw new ArgumentException("A disposable absolute --data-root is required.");
var configPath = ArgumentOrEnvironment("--fixture-config", "JAVMETALITE_REGRESSION_CONFIG");
using var fixtures = new FixedHttp();
var timeout = 150;
ArtworkDiskCacheLimits? artworkCacheLimits = null;
if (!string.IsNullOrWhiteSpace(configPath))
{
    if (!Path.IsPathFullyQualified(configPath)) throw new ArgumentException("An absolute fixture configuration path is required.");
    using var config = JsonDocument.Parse(File.ReadAllBytes(configPath));
    fixtures.Configure(config.RootElement);
    if (config.RootElement.TryGetProperty("providerTimeoutMilliseconds", out var configuredTimeout)) timeout = configuredTimeout.GetInt32();
    if (timeout is < 1 or > 60000) throw new ArgumentException("Provider fixture timeout must be between 1 and 60000 ms.");
    if (config.RootElement.TryGetProperty("artworkCacheLimits", out var cacheLimits))
        artworkCacheLimits = new(cacheLimits.GetProperty("maximumBytes").GetInt64(),
            cacheLimits.GetProperty("maximumFiles").GetInt32(), cacheLimits.GetProperty("maximumLeases").GetInt32());
}
await using var wire = new RpcWriter(Console.OpenStandardOutput());
await using var workspace = new Workspace(wire, dataRoot, fixtures.CreateProvider, TimeSpan.FromMilliseconds(timeout), fixtures.CreateOrganization,
    fixtures.Discover, fixtures.UtcNow, fixtures.BeforePreviewRead, artworkCacheLimits, fixtures.AfterSidecarInventoryCapture);
try
{
    await foreach (var line in Framing.ReadLines(Console.OpenStandardInput()))
    {
        string? id = null;
        try
        {
            if (line is null) throw new RpcError(-32600, "MESSAGE_TOO_LARGE", "Message exceeds 8 MiB.");
            using var doc = JsonDocument.Parse(line);
            var request = doc.RootElement;
            id = request.GetProperty("id").GetString();
            var method = request.GetProperty("method").GetString()!;
            var p = request.GetProperty("params").Clone();
            var result = method switch
            {
                "fixture.configure" => fixtures.Configure(p),
                "fixture.requests" => fixtures.Snapshot(),
                _ => await workspace.Dispatch(method, p)
            };
            await wire.Send(new { jsonrpc = "2.0", id, result });
            if (workspace.ShuttingDown) break;
        }
        catch (Exception error) { await wire.Error(id, RpcError.From(error)); }
    }
}
finally { await workspace.Stop(); }

sealed class FixedHttp : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<JsonElement>> _routes = [];
    private readonly List<object> _requests = [];
    private readonly List<HttpClient> _clients = [];
    private DateTimeOffset _now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private R18RequestScheduler _scheduler;
    private bool _holdScheduler;
    private bool _cleanupFailure;
    private string? _alterCommittedNfoDuringCleanup;
    private string[] _discoveryReadFailures = [];
    private bool _holdDiscovery;
    private int _discoveryDelayMilliseconds;
    private bool _holdPreviewRead, _ignorePreviewReadCancellation, _previewReadFailure;
    private string? _previewReadGateFile;
    private bool _holdSidecarInventory;
    private string? _sidecarInventoryPhase;
    public FixedHttp() => _scheduler = NewScheduler();
    private R18RequestScheduler NewScheduler() => new(TimeSpan.Zero, TimeSpan.FromSeconds(30),
        () => { lock (_gate) return _now; }, async (delay, token) =>
        {
            bool hold;
            lock (_gate) { hold = _holdScheduler; _requests.Add(new { kind = "schedulerWait", milliseconds = delay.TotalMilliseconds }); }
            if (hold) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            else { await Task.Delay(1, token); lock (_gate) _now += delay; }
        });
    public IMetadataProvider CreateProvider(string source)
    {
        var client = new HttpClient(this, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        lock (_gate) _clients.Add(client);
        return source switch { "libredmm" => new LibreDmmClient(client), "r18dev" => new R18DevClient(client, _scheduler), _ => throw new ArgumentException(source) };
    }
    public FileOrganizationService CreateOrganization(OutputService output) => new(output, path =>
    {
        lock (_gate)
        {
            if (_alterCommittedNfoDuringCleanup is { } nfo && File.Exists(nfo))
            {
                _alterCommittedNfoDuringCleanup = null;
                File.Copy(nfo, nfo + ".fixture-original", true);
                File.WriteAllText(nfo, "SYNTHETIC EXTERNAL EDIT AFTER COMMIT");
                _requests.Add(new { kind = "committedNfoAltered", path = nfo });
            }
            if (_cleanupFailure) return Task.FromResult<TemporaryCleanupIssue?>(new(path, "Synthetic cleanup access denied; original error retained."));
        }
        return TemporaryDirectoryCleanup.TryDeleteAsync(path);
    });
    public DateTimeOffset UtcNow() { lock (_gate) return _now; }
    public async Task<MovieInputDiscoveryResult> Discover(IEnumerable<string> inputs, bool recursive, CancellationToken token)
    {
        string[] failures; bool hold; int delay;
        lock (_gate)
        {
            failures = _discoveryReadFailures; hold = _holdDiscovery; delay = _discoveryDelayMilliseconds;
            _requests.Add(new { kind = "discoveryStarted" });
        }
        if (hold) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        if (delay > 0) await Task.Delay(delay, token);
        var paths = inputs.ToArray();
        var readable = paths.Where(path => !failures.Contains(path, StringComparer.Ordinal)).ToArray();
        var found = readable.Length > 0 ? await MovieInputDiscovery.DiscoverAsync(readable, recursive, token)
            : new MovieInputDiscoveryResult(paths, recursive, [], 0, 0, []);
        // Service-boundary substitute, never a claim that hardware produced EIO.
        var errors = paths.Where(path => failures.Contains(path, StringComparer.Ordinal))
            .Select(path => MovieFileDiscoveryDiagnostic.FromException(path, new IOException("Synthetic ordinary filesystem I/O error (EIO).")));
        return found with { InputPaths = paths, Diagnostics = found.Diagnostics.Concat(errors).ToArray() };
    }
    public async Task BeforePreviewRead(CancellationToken token)
    {
        lock (_gate) _requests.Add(new { kind = "previewReadStarted" });
        try
        {
            while (true)
            {
                bool hold, ignoreCancellation, failure;
                lock (_gate)
                {
                    hold = _holdPreviewRead || _previewReadGateFile is not null && File.Exists(_previewReadGateFile);
                    ignoreCancellation = _ignorePreviewReadCancellation;
                    failure = _previewReadFailure;
                }
                if (!hold)
                {
                    if (failure) throw new IOException("Synthetic late read-only planning failure.");
                    return;
                }
                await Task.Delay(20, ignoreCancellation ? CancellationToken.None : token);
            }
        }
        finally { lock (_gate) _requests.Add(new { kind = "previewReadReturned", canceled = token.IsCancellationRequested }); }
    }
    public void AfterSidecarInventoryCapture(string phase, string directory, CancellationToken token)
    {
        lock (_gate) _requests.Add(new { kind = "sidecarInventoryCaptured", phase });
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate) if (!_holdSidecarInventory || _sidecarInventoryPhase is not null && _sidecarInventoryPhase != phase) return;
            Thread.Sleep(10);
        }
    }
    public object Configure(JsonElement p)
    {
        lock (_gate)
        {
            if (p.TryGetProperty("advanceSeconds", out var advance)) _now += TimeSpan.FromSeconds(advance.GetDouble());
            if (p.TryGetProperty("holdScheduler", out var hold)) _holdScheduler = hold.GetBoolean();
            if (p.TryGetProperty("cleanupFailure", out var cleanup)) _cleanupFailure = cleanup.GetBoolean();
            if (p.TryGetProperty("alterCommittedNfoDuringCleanup", out var alterCommitted))
            {
                var path = alterCommitted.ValueKind == JsonValueKind.Null ? null : alterCommitted.GetString();
                if (path is not null && (!Path.IsPathFullyQualified(path) || Path.GetExtension(path) != ".nfo"))
                    throw new ArgumentException("The synthetic committed-output edit requires an absolute NFO path.");
                _alterCommittedNfoDuringCleanup = path;
            }
            if (p.TryGetProperty("discoveryReadFailures", out var failures)) _discoveryReadFailures = failures.EnumerateArray().Select(v => v.GetString()!).ToArray();
            if (p.TryGetProperty("holdSidecarInventory", out var inventoryHold)) _holdSidecarInventory = inventoryHold.GetBoolean();
            if (p.TryGetProperty("sidecarInventoryPhase", out var inventoryPhase))
            {
                _sidecarInventoryPhase = inventoryPhase.ValueKind == JsonValueKind.Null ? null : inventoryPhase.GetString();
                if (_sidecarInventoryPhase is not (null or "importDiscovery" or "save.preview")) throw new ArgumentException("Unknown sidecar inventory phase.");
            }
            if (p.TryGetProperty("holdDiscovery", out var discoveryHold)) _holdDiscovery = discoveryHold.GetBoolean();
            if (p.TryGetProperty("discoveryDelayMilliseconds", out var discoveryDelay)) _discoveryDelayMilliseconds = discoveryDelay.GetInt32();
            if (p.TryGetProperty("holdPreviewRead", out var previewHold)) _holdPreviewRead = previewHold.GetBoolean();
            if (p.TryGetProperty("ignorePreviewReadCancellation", out var ignorePreview)) _ignorePreviewReadCancellation = ignorePreview.GetBoolean();
            if (p.TryGetProperty("previewReadFailure", out var previewFailure)) _previewReadFailure = previewFailure.GetBoolean();
            if (p.TryGetProperty("previewReadGateFile", out var gateFile))
            {
                _previewReadGateFile = gateFile.ValueKind == JsonValueKind.Null ? null : gateFile.GetString();
                if (_previewReadGateFile is not null && !Path.IsPathFullyQualified(_previewReadGateFile)) throw new ArgumentException("Absolute preview gate path required.");
            }
            if (p.TryGetProperty("resetRequests", out var reset) && reset.GetBoolean()) _requests.Clear();
            if (p.TryGetProperty("routes", out var routes)) foreach (var route in routes.EnumerateArray())
                _routes[route.GetProperty("url").GetString()!] = new(route.GetProperty("responses").EnumerateArray().Select(r => r.Clone()));
            return new { configured = true };
        }
    }
    public object Snapshot() { lock (_gate) return new { requests = _requests.ToArray() }; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var url = request.RequestUri!.AbsoluteUri;
        JsonElement fixedResponse;
        lock (_gate)
        {
            _requests.Add(new { kind = "http", url });
            if (!_routes.TryGetValue(url, out var responses) || responses.Count == 0)
                throw new InvalidOperationException("No fixed HTTP response registered: " + url);
            fixedResponse = responses.Dequeue();
        }
        if (fixedResponse.TryGetProperty("waitForCancellation", out var wait) && wait.GetBoolean())
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { lock (_gate) _requests.Add(new { kind = "httpCanceled", url }); throw; }
        }
        if (fixedResponse.TryGetProperty("delayMilliseconds", out var delay)) await Task.Delay(delay.GetInt32(), token);
        if (fixedResponse.TryGetProperty("responseGateFile", out var gate))
        {
            var path = gate.GetString()!;
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute response gate path required.");
            lock (_gate) _requests.Add(new { kind = "httpResponseHeld", url });
            while (File.Exists(path)) await Task.Delay(10, token);
            token.ThrowIfCancellationRequested();
        }
        var response = new HttpResponseMessage((HttpStatusCode)fixedResponse.GetProperty("status").GetInt32()) { RequestMessage = request,
            Content = new StringContent(fixedResponse.TryGetProperty("json", out var body) ? body.GetRawText() : "{}", Encoding.UTF8, "application/json") };
        if (fixedResponse.TryGetProperty("retryAfterSeconds", out var retry)) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retry.GetDouble()));
        return response;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var client in _clients) client.Dispose();
        base.Dispose(disposing);
    }
}
