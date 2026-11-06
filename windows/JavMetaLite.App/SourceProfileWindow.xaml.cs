using System.Collections.ObjectModel;
using System.Windows;
using JavMetaLite.Core.Models;

namespace JavMetaLite.App;

public partial class SourceProfileWindow : Window
{
    public SourceProfileWindow(MetadataSourcePreferenceProfile profile)
    {
        var normalized = MetadataSourcePreferenceProfile.Normalize(profile);
        Rows = new ObservableCollection<SourceProfileRow>(
            Enum.GetValues<MetadataField>().Select(field => new SourceProfileRow(
                field,
                GetFieldDisplayName(field),
                normalized.GetPreferredSourceName(field),
                BuildSourceOptions(false))));
        Rows.Add(new SourceProfileRow(
            null,
            LocalizationService.Get("Main.ArtworkHeader"),
            normalized.ArtworkSource,
            BuildSourceOptions(true)));
        InitializeComponent();
        WindowVisualTheme.ApplyDarkTitleBar(this);
    }

    public ObservableCollection<SourceProfileRow> Rows { get; }

    public MetadataSourcePreferenceProfile Profile { get; private set; } = new();

    public MetadataSourcePreferenceProfile BuildProfile()
    {
        string SourceFor(MetadataField field) =>
            Rows.Single(row => row.Field == field).SourceName;

        return MetadataSourcePreferenceProfile.Normalize(new MetadataSourcePreferenceProfile
        {
            TitleSource = SourceFor(MetadataField.Title),
            OriginalTitleSource = SourceFor(MetadataField.OriginalTitle),
            ReleaseDateSource = SourceFor(MetadataField.ReleaseDate),
            RuntimeSource = SourceFor(MetadataField.RuntimeMinutes),
            MakerSource = SourceFor(MetadataField.Maker),
            DirectorSource = SourceFor(MetadataField.Director),
            LabelSource = SourceFor(MetadataField.Label),
            ActorsSource = SourceFor(MetadataField.Actors),
            GenresSource = SourceFor(MetadataField.Genres),
            PlotSource = SourceFor(MetadataField.Plot),
            RatingSource = SourceFor(MetadataField.Rating),
            ArtworkSource = Rows.Single(row => row.IsArtwork).SourceName
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Profile = BuildProfile();
        DialogResult = true;
    }

    private static string GetFieldDisplayName(MetadataField field) => field switch
    {
        MetadataField.Title => LocalizationService.Get("Field.Title"),
        MetadataField.OriginalTitle => LocalizationService.Get("Field.OriginalTitle"),
        MetadataField.ReleaseDate => LocalizationService.Get("Field.ReleaseDate"),
        MetadataField.RuntimeMinutes => LocalizationService.Get("Field.Runtime"),
        MetadataField.Maker => LocalizationService.Get("Field.Maker"),
        MetadataField.Director => LocalizationService.Get("Field.Director"),
        MetadataField.Label => LocalizationService.Get("Field.Label"),
        MetadataField.Actors => LocalizationService.Get("Field.ActorsShort"),
        MetadataField.Genres => LocalizationService.Get("Field.GenresShort"),
        MetadataField.Plot => LocalizationService.Get("Field.Plot"),
        MetadataField.Rating => LocalizationService.Get("Field.Rating"),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static IReadOnlyList<SourceProfileOption> BuildSourceOptions(bool includeBestArtwork) =>
        includeBestArtwork
            ?
            [
                new("LibreDMM", MetadataSourcePreferenceProfile.LibreDmm),
                new("R18.dev", MetadataSourcePreferenceProfile.R18Dev),
                new(
                    LocalizationService.Get("SourceProfile.BestArtworkResolution"),
                    MetadataSourcePreferenceProfile.BestArtworkResolution)
            ]
            :
            [
                new("LibreDMM", MetadataSourcePreferenceProfile.LibreDmm),
                new("R18.dev", MetadataSourcePreferenceProfile.R18Dev)
            ];
}

public sealed class SourceProfileRow(
    MetadataField? field,
    string displayName,
    string sourceName,
    IReadOnlyList<SourceProfileOption> options)
{
    public MetadataField? Field { get; } = field;

    public bool IsArtwork => Field is null;

    public string DisplayName { get; } = displayName;

    public string SourceName { get; set; } = sourceName;

    public IReadOnlyList<SourceProfileOption> Options { get; } = options;
}

public sealed record SourceProfileOption(string DisplayName, string Value);
