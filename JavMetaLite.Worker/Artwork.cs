using System.Security.Cryptography;
using System.Text.Json;
using JavMetaLite.Core.Services;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Worker;

internal sealed partial class Workspace
{
    private const int MaximumPreviewBytes = 20 * 1024 * 1024;
    private readonly SemaphoreSlim _artworkSlots = new(2, 2);
    private static IEnumerable<ArtworkSource> ArtworkSources(Entry e)
    {
        var selectedSource = e.Job.ArtworkReview.SelectedCandidate?.Source.Name;
        for (var index = 0; index < e.Job.ArtworkReview.Candidates.Count; index++)
        {
            var candidate = e.Job.ArtworkReview.Candidates[index];
            var selected = string.Equals(candidate.Source.Name, selectedSource, StringComparison.OrdinalIgnoreCase);
            if (candidate.IsSidecarPair)
            {
                if (!string.IsNullOrWhiteSpace(candidate.LocalPosterPath)) yield return new("local-poster", "poster", "local", candidate.LocalPosterPath, true, selected, candidate.Source.Name);
                if (!string.IsNullOrWhiteSpace(candidate.LocalFanartPath)) yield return new("local-fanart", "fanart", "local", candidate.LocalFanartPath, true, selected, candidate.Source.Name);
            }
            else
            {
                if (!IsCurrentOnlineSource(e, candidate.Source.Name)) continue;
                var covers = candidate.FullCoverLocations.Where(location => ArtworkLocationHelper.TryGetRemoteUri(location, out _)).Take(3).ToArray();
                if (covers.Length > 0) yield return new("cover-" + index + "-fanart", "fanart", candidate.Source.Name, covers[0], false, selected, candidate.Source.Name, covers);
                if (ArtworkLocationHelper.TryGetRemoteUri(candidate.PosterUrl, out var poster))
                {
                    // Preview the registered poster first; any fallback is confined to
                    // this same reviewed source and is explicitly identified in the reply.
                    var posters = new[] { poster.AbsoluteUri }.Concat(covers).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
                    yield return new("cover-" + index + "-poster", "poster", candidate.Source.Name, posters[0], false, selected, candidate.Source.Name, posters);
                }
            }
        }
        for (var i = 0; i < e.Job.LocalExtrafanartPaths.Count; i++)
        {
            var path = e.Job.LocalExtrafanartPaths[i];
            yield return new("local-sample-" + i, "sample", "local", path, true, e.Job.Metadata.ScreenshotUrls.Contains(path), null);
        }
        var remoteSamples = e.Job.AvailableScreenshotUrls.Where(location => ArtworkLocationHelper.TryGetRemoteUri(location, out _) &&
            e.Job.SourceResults.Any(metadata => HasCurrentMovieId(e, metadata) && metadata.ScreenshotUrls.Contains(location))).ToArray();
        for (var i = 0; i < remoteSamples.Length; i++)
        {
            var owners = SampleSources(e, remoteSamples[i]);
            for (var sourceIndex = 0; sourceIndex < owners.Length; sourceIndex++)
                yield return new("sample-" + i + (sourceIndex == 0 ? "" : "-" + owners[sourceIndex]), "sample", owners[sourceIndex], remoteSamples[i],
                    false, e.Job.Metadata.ScreenshotUrls.Contains(remoteSamples[i]), null);
        }
    }

    private static string ArtworkContentKey(ArtworkSource source)
    {
        var identity = new List<string> { source.Kind, source.Source, source.Location };
        identity.AddRange(source.PreviewLocations ?? []);
        if (source.IsLocal)
        {
            try
            {
                var file = new FileInfo(source.Location);
                identity.Add(file.Exists ? $"{file.Length}/{file.LastWriteTimeUtc.Ticks}/{file.LinkTarget}" : "missing");
            }
            catch (IOException) { identity.Add("unavailable"); }
            catch (UnauthorizedAccessException) { identity.Add("unavailable"); }
        }
        // Selection and global review revision do not change image content.
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(identity, JsonDefaults.Options))));
    }

    private object SelectArtwork(JsonElement p)
    {
        lock (_gate)
        {
            var entry = GetJob(p); EnsureIdle(entry); RequireRevision(entry, p);
            var artwork = ArtworkSources(entry).FirstOrDefault(a => a.ArtworkId == Text(p, "artworkId"))
                ?? throw new RpcError(-32602, "ARTWORK_NOT_FOUND", "Choose artwork from this job's registered candidates.");
            var selected = Bool(p, "selected", true);
            if (artwork.Kind == "sample")
            {
                var samples = entry.Job.Metadata.ScreenshotUrls.ToList();
                samples.RemoveAll(location => location == artwork.Location);
                if (selected) samples.Add(artwork.Location);
                entry.Job.Metadata.ScreenshotUrls = samples.ToArray();
            }
            else
            {
                if (!selected) throw new RpcError(-32602, "INVALID_PARAMS", "Select a cover source instead of deselecting its only active pair.");
                if (!entry.Job.SelectArtworkSource(artwork.CandidateSource!)) throw new RpcError(-32602, "ARTWORK_NOT_FOUND", "The cover source is no longer available.");
            }
            InvalidatePlans(entry.Id);
            return Snapshot(entry);
        }
    }
    private static void ValidateArtworkPermissions(IEnumerable<string> locations, IEnumerable<string> currentInputs)
    {
        var allowed = currentInputs.ToHashSet(FileSystemPathSemantics.PathComparer);
        foreach (var location in locations)
        {
            if (ArtworkLocationHelper.TryGetRemoteUri(location, out _)) continue;
            if (!ArtworkLocationHelper.TryGetLocalPath(location, out var local) || !allowed.Contains(local))
                throw new RpcError(-32010, "ARTWORK_INVALID", "Local artwork output is limited to sidecars discovered for this imported job.");
            FileSystemPathSemantics.ValidateCanonicalPath(local);
        }
    }

    private async Task<object> GetArtworkPreview(JsonElement p)
    {
        foreach (var property in p.EnumerateObject())
            if (property.Name is not ("jobId" or "artworkId" or "async" or "leaseId"))
                throw new RpcError(-32602, "INVALID_PARAMS", "Artwork previews accept registered artwork IDs, not URLs or replacement candidates.");
        ArtworkSource source;
        Entry entry;
        long qualityRevision;
        ArtworkDiskCache.Lease lease;
        var asynchronous = Bool(p, "async", false);
        lock (_gate)
        {
            var e = GetJob(p); EnsureIdle(e);
            entry = e; qualityRevision = e.Job.CoverResolutionRevision;
            source = ArtworkSources(e).FirstOrDefault(x => x.ArtworkId == Text(p, "artworkId"))
                ?? throw new RpcError(-32602, "ARTWORK_NOT_FOUND", "Choose artwork from the imported job.");
            var requestedLease = p.TryGetProperty("leaseId", out _) ? Text(p, "leaseId") : null;
            lease = _artworkCache.Reserve(e.Id, requestedLease);
            if (asynchronous)
            {
                try { return StartOperation(e.Id, async operation =>
                {
                    Progress(operation, "loadingArtworkPreview");
                    return await RunArtworkPreview(e, source, qualityRevision, lease, operation.Cancellation.Token);
                }, readOnly: true); }
                catch { _artworkCache.FinishLoad(lease, false); throw; }
            }
        }
        return await RunArtworkPreview(entry, source, qualityRevision, lease, CancellationToken.None);
    }

    private async Task<object> RunArtworkPreview(Entry entry, ArtworkSource source, long qualityRevision,
        ArtworkDiskCache.Lease lease, CancellationToken operationToken)
    {
        var succeeded = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(operationToken, lease.Cancellation.Token);
        try
        {
            var result = await LoadReviewedArtworkPreview(entry, source, qualityRevision, lease, linked.Token);
            succeeded = true;
            return result;
        }
        finally { _artworkCache.FinishLoad(lease, succeeded); }
    }

    private async Task<ArtworkPreview> LoadArtworkPreview(ArtworkSource source, ArtworkDiskCache.Lease lease, CancellationToken operationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var cancellationToken = timeout.Token;
        await _artworkSlots.WaitAsync(cancellationToken);
        try
        {
        byte[] bytes;
        var resolvedLocation = source.Location;
        var usedFallback = false;
        var attempts = new List<ArtworkAttempt>();
        if (source.IsLocal)
        {
            FileSystemPathSemantics.ValidateCanonicalPath(source.Location);
            var info = new FileInfo(source.Location);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumPreviewBytes || info.LinkTarget is not null)
                throw new RpcError(-32010, "ARTWORK_INVALID", "Local preview is missing, linked or larger than 20 MiB.");
            await using var stream = info.OpenRead();
            bytes = await ReadBounded(stream, cancellationToken);
            attempts.Add(new(source.Location, "succeeded", ResolvedLocation: source.Location));
        }
        else
        {
            bytes = [];
            var locations = source.PreviewLocations ?? [source.Location];
            for (var index = 0; index < locations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = locations[index];
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, location);
                    request.Headers.UserAgent.ParseAdd("JavMetaLite/0.4");
                    if (ArtworkLocationHelper.TryGetRemoteUri(location, out var uri) &&
                        (uri.Host.EndsWith(".dmm.co.jp", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".dmm.com", StringComparison.OrdinalIgnoreCase)))
                        request.Headers.Referrer = new Uri("https://www.dmm.co.jp/");
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var actualLocation = response.RequestMessage?.RequestUri?.AbsoluteUri ?? location;
                    if (!response.IsSuccessStatusCode)
                    {
                        attempts.Add(new(location, "httpError", (int)response.StatusCode, actualLocation));
                        continue;
                    }
                    if (response.Content.Headers.ContentLength > MaximumPreviewBytes) throw new RpcError(-32010, "ARTWORK_INVALID", "Image exceeds 20 MiB.");
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    bytes = await ReadBounded(stream, cancellationToken);
                    // Decode/resource failures below are terminal, not a reason to
                    // bypass a rejected image by trying another candidate.
                    resolvedLocation = actualLocation;
                    usedFallback = index > 0;
                    attempts.Add(new(location, "succeeded", (int)response.StatusCode, actualLocation));
                    break;
                }
                catch (Exception error) when (error is HttpRequestException or IOException)
                {
                    attempts.Add(new(location, "transportError"));
                }
            }
            if (attempts.Count == 0 || attempts[^1].Status != "succeeded")
                throw new RpcError(-32010, "OPERATION_FAILED", "Registered artwork candidates could not be downloaded.", new { attempts });
        }
        cancellationToken.ThrowIfCancellationRequested();
        (int Width, int Height) dimensions;
        try { dimensions = _images.GetDimensions(bytes); } // Decoder validates content and 40 MP bound.
        catch (InvalidDataException) { throw new RpcError(-32010, "ARTWORK_INVALID", "Image content is invalid or exceeds the 40 MP limit."); }
        var png = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
        var jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if (!png && !jpeg) { bytes = _images.CreateFanartPng(bytes); png = true; }
        if (bytes.Length > MaximumPreviewBytes)
            throw new RpcError(-32010, "ARTWORK_INVALID", "Converted preview exceeds 20 MiB.");
        cancellationToken.ThrowIfCancellationRequested();
        var cached = await _artworkCache.PublishAsync(bytes, png, lease, cancellationToken);
        return new(cached.CacheId, cached.Path, png ? "image/png" : "image/jpeg", dimensions.Width, dimensions.Height, bytes.Length,
            source.Kind == "fanart" && new CoverResolutionCheck(resolvedLocation, dimensions.Width, dimensions.Height).IsLowResolution,
            resolvedLocation, usedFallback, attempts, lease.Explicit ? lease.Id : null);
        }
        finally { _artworkSlots.Release(); }
    }
    private static async Task<byte[]> ReadBounded(Stream stream, CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (result.Length + count > MaximumPreviewBytes) throw new RpcError(-32010, "ARTWORK_INVALID", "Image exceeds 20 MiB.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
    private sealed record ArtworkAttempt(string Location, string Status, int? HttpStatus = null, string? ResolvedLocation = null);
    private sealed record ArtworkPreview(string CacheId, string Path, string MimeType, int Width, int Height, int ByteLength,
        bool IsLowResolution, string ResolvedLocation, bool UsedFallback, IReadOnlyList<ArtworkAttempt> Attempts, string? LeaseId);
    private sealed record ArtworkSource(string ArtworkId, string Kind, string Source, string Location, bool IsLocal, bool Selected, string? CandidateSource, string[]? PreviewLocations = null);
}
