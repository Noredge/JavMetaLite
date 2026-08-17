namespace JavMetaLite.Core.Models;

public sealed record NfoWriteContext(
    LocalMetadataBundle? LocalBundle,
    bool UpdatePosterReference,
    string? PosterFileName,
    bool UpdateFanartReference,
    string? FanartFileName);
