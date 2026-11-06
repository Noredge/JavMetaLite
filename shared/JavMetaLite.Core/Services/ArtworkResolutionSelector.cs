using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public readonly record struct ArtworkPixelSize(int Width, int Height)
{
    public long PixelCount => (long)Math.Max(0, Width) * Math.Max(0, Height);
}

public sealed record ArtworkResolutionSelection(
    string SourceName,
    string CoverLocation,
    ArtworkPixelSize Size);

public static class ArtworkResolutionSelector
{
    public static async Task<ArtworkResolutionSelection?> SelectBestAsync(
        IEnumerable<MovieMetadata> sources,
        Func<string, CancellationToken, Task<ArtworkPixelSize>> probeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(probeAsync);

        var candidates = sources
            .Select((source, index) => new SourceCandidate(
                index,
                MetadataCandidateSource.FromMetadata(source).Name,
                new ArtworkCoverCandidate(
                    MetadataCandidateSource.FromMetadata(source),
                    source.CoverUrl,
                    source.FallbackCoverUrl,
                    source.PosterUrl).FullCoverLocations))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourceName) && candidate.Locations.Count > 0)
            .DistinctBy(candidate => candidate.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        if (candidates.Length == 1)
        {
            return new ArtworkResolutionSelection(
                candidates[0].SourceName,
                candidates[0].Locations[0],
                default);
        }

        var measurements = await Task.WhenAll(candidates.Select(MeasureAsync));
        var measured = measurements
            .Where(result => result is not null)
            .Select(result => result!)
            .OrderByDescending(result => result.Size.PixelCount)
            .ThenByDescending(result => result.Size.Width)
            .ThenBy(result => result.Index)
            .FirstOrDefault();
        if (measured is not null)
        {
            return new ArtworkResolutionSelection(measured.SourceName, measured.Location, measured.Size);
        }

        var fallback = candidates[0];
        return new ArtworkResolutionSelection(
            fallback.SourceName,
            fallback.Locations[0],
            default);

        async Task<MeasuredCandidate?> MeasureAsync(SourceCandidate candidate)
        {
            foreach (var location in candidate.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var size = await probeAsync(location, cancellationToken);
                    if (size.PixelCount > 0)
                    {
                        return new MeasuredCandidate(
                            candidate.Index,
                            candidate.SourceName,
                            location,
                            size);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Warning($"无法读取封套尺寸 source={candidate.SourceName} location={location}", exception);
                }
            }

            return null;
        }
    }

    private sealed record SourceCandidate(int Index, string SourceName, IReadOnlyList<string> Locations);

    private sealed record MeasuredCandidate(
        int Index,
        string SourceName,
        string Location,
        ArtworkPixelSize Size);
}
