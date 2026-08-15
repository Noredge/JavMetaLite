using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JavMetaLite.Core.Models;

public sealed class MovieMetadata : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _originalTitle = string.Empty;
    private string _releaseDate = string.Empty;
    private string _director = string.Empty;
    private string _maker = string.Empty;
    private string _label = string.Empty;
    private string _series = string.Empty;
    private string _runtimeMinutes = string.Empty;
    private string _actorsText = string.Empty;
    private string _genresText = string.Empty;
    private string _plot = string.Empty;
    private string _rating = string.Empty;
    private string _contentId = string.Empty;
    private string _posterUrl = string.Empty;
    private string _coverUrl = string.Empty;
    private string _fallbackCoverUrl = string.Empty;
    private string _sourceUrl = string.Empty;
    private string _sourceName = string.Empty;
    private string _sourceDisplayName = string.Empty;
    private IReadOnlyList<ActorMetadata> _actors = Array.Empty<ActorMetadata>();
    private IReadOnlyList<string> _screenshotUrls = Array.Empty<string>();

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Title { get => _title; set => SetField(ref _title, value); }
    public string OriginalTitle { get => _originalTitle; set => SetField(ref _originalTitle, value); }
    public string ReleaseDate { get => _releaseDate; set => SetField(ref _releaseDate, value); }
    public string Director { get => _director; set => SetField(ref _director, value); }
    public string Maker { get => _maker; set => SetField(ref _maker, value); }
    public string Label { get => _label; set => SetField(ref _label, value); }
    public string Series { get => _series; set => SetField(ref _series, value); }
    public string RuntimeMinutes { get => _runtimeMinutes; set => SetField(ref _runtimeMinutes, value); }
    public string ActorsText { get => _actorsText; set => SetField(ref _actorsText, value); }
    public string GenresText { get => _genresText; set => SetField(ref _genresText, value); }
    public string Plot { get => _plot; set => SetField(ref _plot, value); }
    public string Rating { get => _rating; set => SetField(ref _rating, value); }
    public string ContentId { get => _contentId; set => SetField(ref _contentId, value); }
    public string PosterUrl { get => _posterUrl; set => SetField(ref _posterUrl, value); }
    public string CoverUrl { get => _coverUrl; set => SetField(ref _coverUrl, value); }
    public string FallbackCoverUrl { get => _fallbackCoverUrl; set => SetField(ref _fallbackCoverUrl, value); }
    public string SourceUrl { get => _sourceUrl; set => SetField(ref _sourceUrl, value); }
    public string SourceName { get => _sourceName; set => SetField(ref _sourceName, value); }
    public string SourceDisplayName { get => _sourceDisplayName; set => SetField(ref _sourceDisplayName, value); }
    public IReadOnlyList<ActorMetadata> Actors
    {
        get => _actors;
        set
        {
            var normalized = (value ?? Array.Empty<ActorMetadata>())
                .Where(actor => !string.IsNullOrWhiteSpace(actor.Name))
                .Select(actor => new ActorMetadata(actor.Name.Trim(), actor.ImageUrl?.Trim() ?? string.Empty))
                .GroupBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (_actors.SequenceEqual(normalized))
            {
                return;
            }

            _actors = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Actors)));
        }
    }
    public IReadOnlyList<string> ScreenshotUrls
    {
        get => _screenshotUrls;
        set
        {
            var normalized = (value ?? Array.Empty<string>())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (_screenshotUrls.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _screenshotUrls = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScreenshotUrls)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
