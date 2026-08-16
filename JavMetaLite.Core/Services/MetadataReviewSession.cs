using System.ComponentModel;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class MetadataReviewSession : IDisposable
{
    private static readonly MetadataField[] TrackedFields = Enum.GetValues<MetadataField>();

    private readonly Dictionary<MetadataField, List<MetadataFieldCandidate>> _candidates = [];
    private readonly Dictionary<MetadataField, MetadataFieldCandidate> _selectedCandidates = [];
    private readonly Dictionary<string, MetadataSourceSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _applyingCandidate;
    private bool _disposed;

    private MetadataReviewSession(MovieMetadata metadata, IEnumerable<MovieMetadata> sourceResults)
    {
        Metadata = metadata;

        var sources = sourceResults.Where(source => source is not null).ToList();
        if (sources.Count == 0 &&
            (!string.IsNullOrWhiteSpace(metadata.SourceName) ||
             !string.IsNullOrWhiteSpace(metadata.SourceDisplayName)))
        {
            sources.Add(metadata);
        }

        foreach (var sourceResult in sources)
        {
            var snapshot = MetadataSourceSnapshot.FromMetadata(sourceResult);
            if (!_snapshots.TryAdd(snapshot.Source.Name, snapshot))
            {
                continue;
            }

            foreach (var field in TrackedFields)
            {
                var value = snapshot.GetValue(field);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                GetOrCreateCandidates(field).Add(new MetadataFieldCandidate(field, value, snapshot.Source));
            }
        }

        Sources = _snapshots.Values.ToArray();
        InitializeSelections();
        Metadata.PropertyChanged += Metadata_PropertyChanged;
    }

    public MovieMetadata Metadata { get; }

    public IReadOnlyList<MetadataSourceSnapshot> Sources { get; }

    public event EventHandler<MetadataSelectionChangedEventArgs>? SelectionChanged;

    public static MetadataReviewSession Create(
        MovieMetadata metadata,
        params MovieMetadata[] sourceResults)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new MetadataReviewSession(metadata, sourceResults ?? []);
    }

    public IReadOnlyList<MetadataFieldCandidate> GetCandidates(MetadataField field) =>
        _candidates.TryGetValue(field, out var candidates) ? candidates.ToArray() : [];

    public MetadataFieldCandidate? GetSelectedCandidate(MetadataField field) =>
        _selectedCandidates.GetValueOrDefault(field);

    public bool SelectCandidate(MetadataField field, string sourceName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var candidate = GetCandidates(field).FirstOrDefault(item =>
            string.Equals(item.Source.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return false;
        }

        ApplyCandidate(candidate);
        return true;
    }

    public void SetManualValue(MetadataField field, string? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = value ?? string.Empty;
        _applyingCandidate = true;
        try
        {
            SetValue(Metadata, field, normalized);
        }
        finally
        {
            _applyingCandidate = false;
        }

        SelectManualCandidate(field, normalized);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Metadata.PropertyChanged -= Metadata_PropertyChanged;
        _disposed = true;
    }

    private void InitializeSelections()
    {
        foreach (var field in TrackedFields)
        {
            var selectedValue = GetValue(Metadata, field);
            if (_candidates.TryGetValue(field, out var candidates))
            {
                var match = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Value, selectedValue, StringComparison.Ordinal));
                if (match is not null)
                {
                    _selectedCandidates[field] = match;
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedValue))
            {
                SelectManualCandidate(field, selectedValue);
            }
        }
    }

    private void ApplyCandidate(MetadataFieldCandidate candidate)
    {
        _applyingCandidate = true;
        try
        {
            SetValue(Metadata, candidate.Field, candidate.Value);
            if (candidate.Field == MetadataField.Actors &&
                _snapshots.TryGetValue(candidate.Source.Name, out var snapshot))
            {
                Metadata.Actors = snapshot.Actors;
            }
        }
        finally
        {
            _applyingCandidate = false;
        }

        _selectedCandidates[candidate.Field] = candidate;
        SelectionChanged?.Invoke(this, new MetadataSelectionChangedEventArgs(candidate.Field, candidate));
    }

    private void Metadata_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_applyingCandidate || !TryGetField(eventArgs.PropertyName, out var field))
        {
            return;
        }

        SelectManualCandidate(field, GetValue(Metadata, field));
    }

    private void SelectManualCandidate(MetadataField field, string value)
    {
        var candidates = GetOrCreateCandidates(field);
        var candidate = new MetadataFieldCandidate(field, value, MetadataCandidateSource.Manual);
        var existingIndex = candidates.FindIndex(item => item.Source.IsManual);
        if (existingIndex >= 0)
        {
            candidates[existingIndex] = candidate;
        }
        else
        {
            candidates.Add(candidate);
        }

        _selectedCandidates[field] = candidate;
        SelectionChanged?.Invoke(this, new MetadataSelectionChangedEventArgs(field, candidate));
    }

    private List<MetadataFieldCandidate> GetOrCreateCandidates(MetadataField field)
    {
        if (_candidates.TryGetValue(field, out var candidates))
        {
            return candidates;
        }

        candidates = [];
        _candidates[field] = candidates;
        return candidates;
    }

    private static string GetValue(MovieMetadata metadata, MetadataField field) => field switch
    {
        MetadataField.Title => metadata.Title,
        MetadataField.OriginalTitle => metadata.OriginalTitle,
        MetadataField.ReleaseDate => metadata.ReleaseDate,
        MetadataField.RuntimeMinutes => metadata.RuntimeMinutes,
        MetadataField.Maker => metadata.Maker,
        MetadataField.Director => metadata.Director,
        MetadataField.Label => metadata.Label,
        MetadataField.Series => metadata.Series,
        MetadataField.Actors => metadata.ActorsText,
        MetadataField.Genres => metadata.GenresText,
        MetadataField.Plot => metadata.Plot,
        MetadataField.Rating => metadata.Rating,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static void SetValue(MovieMetadata metadata, MetadataField field, string value)
    {
        switch (field)
        {
            case MetadataField.Title:
                metadata.Title = value;
                break;
            case MetadataField.OriginalTitle:
                metadata.OriginalTitle = value;
                break;
            case MetadataField.ReleaseDate:
                metadata.ReleaseDate = value;
                break;
            case MetadataField.RuntimeMinutes:
                metadata.RuntimeMinutes = value;
                break;
            case MetadataField.Maker:
                metadata.Maker = value;
                break;
            case MetadataField.Director:
                metadata.Director = value;
                break;
            case MetadataField.Label:
                metadata.Label = value;
                break;
            case MetadataField.Series:
                metadata.Series = value;
                break;
            case MetadataField.Actors:
                metadata.ActorsText = value;
                break;
            case MetadataField.Genres:
                metadata.GenresText = value;
                break;
            case MetadataField.Plot:
                metadata.Plot = value;
                break;
            case MetadataField.Rating:
                metadata.Rating = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static bool TryGetField(string? propertyName, out MetadataField field)
    {
        field = propertyName switch
        {
            nameof(MovieMetadata.Title) => MetadataField.Title,
            nameof(MovieMetadata.OriginalTitle) => MetadataField.OriginalTitle,
            nameof(MovieMetadata.ReleaseDate) => MetadataField.ReleaseDate,
            nameof(MovieMetadata.RuntimeMinutes) => MetadataField.RuntimeMinutes,
            nameof(MovieMetadata.Maker) => MetadataField.Maker,
            nameof(MovieMetadata.Director) => MetadataField.Director,
            nameof(MovieMetadata.Label) => MetadataField.Label,
            nameof(MovieMetadata.Series) => MetadataField.Series,
            nameof(MovieMetadata.ActorsText) => MetadataField.Actors,
            nameof(MovieMetadata.GenresText) => MetadataField.Genres,
            nameof(MovieMetadata.Plot) => MetadataField.Plot,
            nameof(MovieMetadata.Rating) => MetadataField.Rating,
            _ => default
        };
        return propertyName is
            nameof(MovieMetadata.Title) or
            nameof(MovieMetadata.OriginalTitle) or
            nameof(MovieMetadata.ReleaseDate) or
            nameof(MovieMetadata.RuntimeMinutes) or
            nameof(MovieMetadata.Maker) or
            nameof(MovieMetadata.Director) or
            nameof(MovieMetadata.Label) or
            nameof(MovieMetadata.Series) or
            nameof(MovieMetadata.ActorsText) or
            nameof(MovieMetadata.GenresText) or
            nameof(MovieMetadata.Plot) or
            nameof(MovieMetadata.Rating);
    }
}
