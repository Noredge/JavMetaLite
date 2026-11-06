namespace JavMetaLite.Core.Models;

public sealed record MetadataSourcePreferenceProfile
{
    public const string LibreDmm = "libredmm";
    public const string R18Dev = "r18dev";
    public const string BestArtworkResolution = "best-artwork-resolution";

    public string TitleSource { get; init; } = LibreDmm;

    public string OriginalTitleSource { get; init; } = LibreDmm;

    public string ReleaseDateSource { get; init; } = LibreDmm;

    public string RuntimeSource { get; init; } = LibreDmm;

    public string MakerSource { get; init; } = LibreDmm;

    public string DirectorSource { get; init; } = LibreDmm;

    public string LabelSource { get; init; } = LibreDmm;

    public string ActorsSource { get; init; } = LibreDmm;

    public string GenresSource { get; init; } = LibreDmm;

    public string PlotSource { get; init; } = LibreDmm;

    public string RatingSource { get; init; } = LibreDmm;

    public string ArtworkSource { get; init; } = LibreDmm;

    public string GetPreferredSourceName(MetadataField field) => field switch
    {
        MetadataField.Title => TitleSource,
        MetadataField.OriginalTitle => OriginalTitleSource,
        MetadataField.ReleaseDate => ReleaseDateSource,
        MetadataField.RuntimeMinutes => RuntimeSource,
        MetadataField.Maker => MakerSource,
        MetadataField.Director => DirectorSource,
        MetadataField.Label => LabelSource,
        MetadataField.Actors => ActorsSource,
        MetadataField.Genres => GenresSource,
        MetadataField.Plot => PlotSource,
        MetadataField.Rating => RatingSource,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    public static MetadataSourcePreferenceProfile Normalize(MetadataSourcePreferenceProfile? profile)
    {
        profile ??= new MetadataSourcePreferenceProfile();
        return profile with
        {
            TitleSource = NormalizeSourceName(profile.TitleSource),
            OriginalTitleSource = NormalizeSourceName(profile.OriginalTitleSource),
            ReleaseDateSource = NormalizeSourceName(profile.ReleaseDateSource),
            RuntimeSource = NormalizeSourceName(profile.RuntimeSource),
            MakerSource = NormalizeSourceName(profile.MakerSource),
            DirectorSource = NormalizeSourceName(profile.DirectorSource),
            LabelSource = NormalizeSourceName(profile.LabelSource),
            ActorsSource = NormalizeSourceName(profile.ActorsSource),
            GenresSource = NormalizeSourceName(profile.GenresSource),
            PlotSource = NormalizeSourceName(profile.PlotSource),
            RatingSource = NormalizeSourceName(profile.RatingSource),
            ArtworkSource = NormalizeArtworkSourceName(profile.ArtworkSource)
        };
    }

    public static bool IsPreview11Default(MetadataSourcePreferenceProfile? profile)
    {
        var normalized = Normalize(profile);
        return normalized.TitleSource == R18Dev &&
               normalized.OriginalTitleSource == LibreDmm &&
               normalized.ReleaseDateSource == LibreDmm &&
               normalized.RuntimeSource == LibreDmm &&
               normalized.MakerSource == LibreDmm &&
               normalized.DirectorSource == LibreDmm &&
               normalized.LabelSource == LibreDmm &&
               normalized.ActorsSource == LibreDmm &&
               normalized.GenresSource == LibreDmm &&
               normalized.PlotSource == LibreDmm &&
               normalized.RatingSource == LibreDmm &&
               normalized.ArtworkSource == LibreDmm;
    }

    public static bool IsSupportedSource(string? sourceName) =>
        string.Equals(sourceName, LibreDmm, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceName, R18Dev, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedArtworkSource(string? sourceName) =>
        IsSupportedSource(sourceName) ||
        string.Equals(sourceName, BestArtworkResolution, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSourceName(string? sourceName) =>
        string.Equals(sourceName, R18Dev, StringComparison.OrdinalIgnoreCase)
            ? R18Dev
            : LibreDmm;

    private static string NormalizeArtworkSourceName(string? sourceName) =>
        string.Equals(sourceName, BestArtworkResolution, StringComparison.OrdinalIgnoreCase)
            ? BestArtworkResolution
            : NormalizeSourceName(sourceName);
}
