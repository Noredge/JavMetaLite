using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

// Advisory only: never participates in search eligibility, save validation or review revisions.
public sealed record CoverResolutionCheck(string Location, int Width, int Height)
{
    public bool IsMeasured => Width > 0 && Height > 0;
    // A fallback thumbnail may be portrait or square even in the full-cover/fanart slot.
    // Keep the conservative landscape rule, but never exclude an extremely small full-cover image.
    public bool IsLowResolution => IsMeasured &&
        (Math.Max(Width, Height) < 400 || (Width > Height && Width < 600));
    public string Dimensions => IsMeasured ? $"{Width} × {Height}" : string.Empty;

    public static IReadOnlyList<string> GetLocations(ArtworkCoverCandidate? candidate) => candidate is null
        ? []
        : candidate.IsSidecarPair
            ? new[] { candidate.LocalFanartPath }.Where(ArtworkLocationHelper.IsSupported)
                .Select(ArtworkLocationHelper.Normalize).ToArray()
            : candidate.FullCoverLocations;

    public static async Task<CoverResolutionCheck> MeasureAsync(
        IReadOnlyList<string> locations,
        Func<string, CancellationToken, Task<ArtworkPixelSize>> probe,
        CancellationToken token)
    {
        foreach (var location in locations)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var size = await probe(location, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (size.Width > 0 && size.Height > 0)
                    return new(location, size.Width, size.Height);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or
                IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or FormatException or
                OperationCanceledException or ArgumentException)
            {
                // Same ordered fallback as the preview/save paths. Unknown is not a low-quality verdict.
                AppLog.Info($"封套尺寸检查候选不可用 location={location} error={exception.GetType().Name}");
            }
        }
        return new(string.Empty, 0, 0);
    }
}
