using System.ComponentModel;
using System.Runtime.CompilerServices;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public sealed class MovieJob : IDisposable, INotifyPropertyChanged
{
    private MovieMetadata _metadata = null!;
    private MetadataReviewSession _metadataReview = null!;
    private ArtworkCoverReviewSession _artworkReview = null!;
    private IReadOnlyList<MovieMetadata> _sourceResults = [];
    private IReadOnlyList<MetadataSourceSearchAttempt> _lastSearchAttempts = [];
    private IReadOnlyList<string> _availableScreenshotUrls = [];
    private IReadOnlyList<string> _localExtrafanartPaths = [];
    private MovieSearchState _searchState = MovieSearchState.NeedsId;
    private Exception? _lastSearchError;
    private bool _isSelectedForBatch = true;
    private long _reviewRevision;
    private MovieSaveState _saveState = MovieSaveState.Idle;
    private Exception? _lastSaveError;
    private MovieSaveConfiguration? _saveConfiguration;
    private IReadOnlyList<string> _videoPaths = [];
    private string _movieFileGroupKey = string.Empty;
    private string _sidecarBaseName = string.Empty;
    private string? _onlineExtrafanartCandidateId;
    private bool _disposed;
    private readonly HashSet<MetadataField> _reviewedFields = [];
    private bool _applyingAutomaticSelection;
    private bool _artworkReviewed;
    private long _coverResolutionRevision;

    public long CoverResolutionRevision => _coverResolutionRevision;
    public CoverResolutionCheck? CoverResolution { get; private set; }
    public bool HasLowResolutionCover => CoverResolution?.IsLowResolution == true;

    public bool TrySetCoverResolution(long revision, CoverResolutionCheck result)
    {
        if (_disposed || revision != CoverResolutionRevision) return false;
        CoverResolution = result;
        OnPropertyChanged(nameof(CoverResolution));
        OnPropertyChanged(nameof(HasLowResolutionCover));
        return true;
    }

    private void InvalidateCoverResolution()
    {
        _coverResolutionRevision++;
        CoverResolution = null;
        OnPropertyChanged(nameof(CoverResolution));
        OnPropertyChanged(nameof(HasLowResolutionCover));
        OnPropertyChanged(nameof(CoverResolutionRevision));
    }

    public MovieJob()
    {
        ReplaceMetadata(new MovieMetadata(), [], []);
    }

    public string? VideoPath => _videoPaths.FirstOrDefault();

    public IReadOnlyList<string> VideoPaths => _videoPaths;

    public string MovieFileGroupKey => _movieFileGroupKey;

    public string SidecarBaseName => _sidecarBaseName;

    public int VideoPartCount => _videoPaths.Count;

    public bool IsMultipart => VideoPartCount > 1;

    public string FileName => VideoPath is null
        ? string.Empty
        : VideoPartCount > 1
            ? $"{Path.GetFileName(VideoPath)} (+{VideoPartCount - 1})"
            : Path.GetFileName(VideoPath);

    public MovieMetadata Metadata => _metadata;

    public MetadataReviewSession MetadataReview => _metadataReview;

    public ArtworkCoverReviewSession ArtworkReview => _artworkReview;

    public IReadOnlyList<MovieMetadata> SourceResults => _sourceResults;

    public IReadOnlyList<string> AvailableScreenshotUrls
    {
        get => _availableScreenshotUrls;
        private set
        {
            _availableScreenshotUrls = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> LocalExtrafanartPaths
    {
        get => _localExtrafanartPaths;
        private set
        {
            _localExtrafanartPaths = value;
            OnPropertyChanged();
        }
    }

    public LocalMetadataBundle? LocalMetadataBundle { get; private set; }

    public MovieMetadata? LocalSourceMetadata { get; private set; }

    public ArtworkCoverCandidate? LocalArtworkCandidate { get; private set; }

    public ArtworkCoverCandidate? ManualArtworkCandidate { get; private set; }

    public string? PreferredArtworkSourceName { get; private set; }

    public bool LocalNfoSaveBlocked { get; private set; }

    public bool CanReplaceLocalExtrafanart =>
        !string.IsNullOrWhiteSpace(_onlineExtrafanartCandidateId) &&
        string.Equals(
            _onlineExtrafanartCandidateId,
            MovieIdParser.Normalize(Metadata.Id),
            StringComparison.OrdinalIgnoreCase);

    public MovieSearchState SearchState
    {
        get => _searchState;
        private set
        {
            if (_searchState == value)
            {
                return;
            }

            _searchState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanBatchSearch));
        }
    }

    public IReadOnlyList<MetadataSourceSearchAttempt> LastSearchAttempts
    {
        get => _lastSearchAttempts;
        private set
        {
            _lastSearchAttempts = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFailedSources));
            OnPropertyChanged(nameof(HasPartialSourceFailure));
            OnPropertyChanged(nameof(HasRateLimitedSource));
            OnPropertyChanged(nameof(FailedSourceNames));
        }
    }

    public Exception? LastSearchError
    {
        get => _lastSearchError;
        private set
        {
            _lastSearchError = value;
            OnPropertyChanged();
        }
    }

    public bool HasFailedSources => LastSearchAttempts.Any(attempt => !attempt.Success);

    public bool HasPartialSourceFailure => HasFailedSources && LastSearchAttempts.Any(attempt => attempt.Success);

    public bool HasRateLimitedSource => LastSearchAttempts.Any(attempt => attempt.Error is MetadataSourceRateLimitException);

    public string FailedSourceNames => string.Join(", ", LastSearchAttempts
        .Where(attempt => !attempt.Success).Select(attempt => attempt.SourceDisplayName));

    public bool IsSelectedForBatch
    {
        get => _isSelectedForBatch;
        set
        {
            if (_isSelectedForBatch == value)
            {
                return;
            }

            _isSelectedForBatch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanBatchSearch));
        }
    }

    // Eligibility only needs presence. Normalize also accepts unrecognized, nonempty
    // input via its trimmed fallback; parsing here adds work, not validation.
    public bool CanBatchSearch => IsSelectedForBatch &&
                                  SearchState is (MovieSearchState.Searchable or
                                      MovieSearchState.SearchFailed or
                                      MovieSearchState.SearchCanceled) &&
                                  !string.IsNullOrWhiteSpace(Metadata.Id);

    public long ReviewRevision
    {
        get => _reviewRevision;
        private set
        {
            _reviewRevision = value;
            OnPropertyChanged();
        }
    }

    public MovieSaveState SaveState
    {
        get => _saveState;
        private set
        {
            if (_saveState == value)
            {
                return;
            }

            _saveState = value;
            OnPropertyChanged();
        }
    }

    public Exception? LastSaveError
    {
        get => _lastSaveError;
        private set
        {
            _lastSaveError = value;
            OnPropertyChanged();
        }
    }

    public MovieSaveConfiguration? SaveConfiguration
    {
        get => _saveConfiguration;
        private set
        {
            _saveConfiguration = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? MetadataPropertyChanged;

    public event EventHandler<MetadataSelectionChangedEventArgs>? MetadataSelectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ResetForVideo(string videoPath, string? detectedId)
    {
        ResetForVideos([videoPath], detectedId);
    }

    public void ResetForVideos(IEnumerable<string> videoPaths, string? detectedId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(videoPaths);

        SetVideoPaths(MovieFileSet.Create(videoPaths));
        OnPropertyChanged(nameof(VideoPath));
        OnPropertyChanged(nameof(VideoPaths));
        OnPropertyChanged(nameof(MovieFileGroupKey));
        OnPropertyChanged(nameof(SidecarBaseName));
        OnPropertyChanged(nameof(VideoPartCount));
        OnPropertyChanged(nameof(IsMultipart));
        OnPropertyChanged(nameof(FileName));
        LocalMetadataBundle = null;
        _reviewedFields.Clear();
        _artworkReviewed = false;
        LocalSourceMetadata = null;
        LocalArtworkCandidate = null;
        ManualArtworkCandidate = null;
        PreferredArtworkSourceName = null;
        LocalNfoSaveBlocked = false;
        SaveConfiguration = null;
        IsSelectedForBatch = true;
        SaveState = MovieSaveState.Idle;
        LastSaveError = null;
        LastSearchAttempts = [];
        AvailableScreenshotUrls = [];
        LocalExtrafanartPaths = [];
        _onlineExtrafanartCandidateId = null;
        LastSearchError = null;
        ReplaceMetadata(new MovieMetadata { Id = detectedId ?? string.Empty }, [], []);
        SearchState = string.IsNullOrWhiteSpace(Metadata.Id)
            ? MovieSearchState.NeedsId
            : MovieSearchState.Searchable;
    }

    public void BeginLocalNfoRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LocalNfoSaveBlocked = true;
        TouchReview();
    }

    public void ApplyLocalMetadata(
        LocalMetadataBundle bundle,
        LocalMetadataReviewComposition composition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(composition);

        LocalMetadataBundle = bundle;
        LocalSourceMetadata = composition.Sources.FirstOrDefault();
        LocalNfoSaveBlocked = false;
        ReplaceAvailableScreenshotUrls(composition.Sources);
        ReplaceMetadata(composition.Metadata, composition.Sources, []);
    }

    public void ApplyMetadata(
        MovieMetadata metadata,
        IReadOnlyList<MovieMetadata> sourceResults)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(sourceResults);
        ReplaceAvailableScreenshotUrls(sourceResults.Append(metadata));
        ReplaceMetadata(metadata, sourceResults, []);
    }

    public void InitializeSaveConfiguration(MovieSaveConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(configuration);
        SaveConfiguration ??= configuration;
    }

    public void UpdateSaveConfiguration(MovieSaveConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(configuration);
        if (Equals(SaveConfiguration, configuration))
        {
            return;
        }

        SaveConfiguration = configuration;
        TouchReview();
    }

    public void BeginSave(long expectedReviewRevision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReviewRevision != expectedReviewRevision)
        {
            throw new InvalidOperationException("影片在预览后发生变化，请重新生成预览。 ");
        }

        if (VideoPath is null || string.IsNullOrWhiteSpace(MovieIdParser.Normalize(Metadata.Id)))
        {
            throw new InvalidOperationException("影片路径和番号必须完整。 ");
        }

        if (LocalNfoSaveBlocked || SaveConfiguration is null)
        {
            throw new InvalidOperationException("影片当前不能安全保存。 ");
        }

        LastSaveError = null;
        SaveState = MovieSaveState.Saving;
    }

    public void MarkSaveCompleted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastSaveError = null;
        SaveState = MovieSaveState.Completed;
        IsSelectedForBatch = false;
    }

    public void MarkSaveFailed(Exception exception, bool conflict)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(exception);
        LastSaveError = exception;
        SaveState = conflict ? MovieSaveState.Conflict : MovieSaveState.SaveFailed;
    }

    public void MarkSaveCanceled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastSaveError = null;
        SaveState = MovieSaveState.SaveCanceled;
    }

    public void BeginSearch(bool preserveAttempts = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(MovieIdParser.Normalize(Metadata.Id)))
        {
            throw new InvalidOperationException("请先输入有效影片番号。 ");
        }

        if (SearchState is MovieSearchState.Searching)
        {
            throw new InvalidOperationException("该影片已经在搜索中。 ");
        }

        if (!preserveAttempts)
        {
            LastSearchAttempts = [];
        }
        LastSearchError = null;
        SearchState = MovieSearchState.Searching;
        InvalidateCoverResolution();
    }

    public MovieMetadata ApplyOnlineSources(
        MovieMetadata preferredOnlineMetadata,
        IReadOnlyList<MovieMetadata> onlineSources,
        IReadOnlyList<MetadataSourceSearchAttempt>? attempts = null,
        MetadataSourcePreferenceProfile? sourceProfile = null,
        string? resolvedArtworkBundleSource = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(preferredOnlineMetadata);
        ArgumentNullException.ThrowIfNull(onlineSources);

        // Editable values must never mutate the provider result reused by retries.
        preferredOnlineMetadata = CloneMetadata(preferredOnlineMetadata);
        ReplaceAvailableScreenshotUrls(onlineSources.Append(preferredOnlineMetadata));

        var retainedManualCandidates = CaptureManualCandidates();
        PreferredArtworkSourceName = MetadataCandidateSource
            .FromMetadata(preferredOnlineMetadata)
            .Name;
        if (LocalSourceMetadata is null)
        {
            ReplaceMetadata(preferredOnlineMetadata, onlineSources, retainedManualCandidates);
            MarkOnlineExtrafanartCandidates(onlineSources.Append(preferredOnlineMetadata));
            ApplySourceProfile(sourceProfile, resolvedArtworkBundleSource);
            CompleteSearch(attempts);
            return Metadata;
        }

        var localForMerge = LocalMetadataReviewComposer.CreateLocal(LocalSourceMetadata).Metadata;
        localForMerge.Id = Metadata.Id;
        var composition = LocalMetadataReviewComposer.ComposeWithOnline(
            localForMerge,
            preferredOnlineMetadata,
            onlineSources);
        ReplaceMetadata(composition.Metadata, composition.Sources, retainedManualCandidates);
        MarkOnlineExtrafanartCandidates(onlineSources.Append(preferredOnlineMetadata));
        ApplySourceProfile(sourceProfile, resolvedArtworkBundleSource);
        CompleteSearch(attempts);
        return Metadata;
    }

    public MovieMetadata ApplyRetriedOnlineSources(
        MovieMetadata preferredOnlineMetadata,
        IReadOnlyList<MovieMetadata> onlineSources,
        IReadOnlyList<MetadataSourceSearchAttempt> attempts,
        MetadataSourcePreferenceProfile? sourceProfile = null,
        string? resolvedArtworkBundleSource = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selections = Enum.GetValues<MetadataField>()
            .Where(field => _reviewedFields.Contains(field) ||
                MetadataReview.GetSelectedCandidate(field)?.Source.IsManual == true)
            .Select(field => MetadataReview.GetSelectedCandidate(field))
            .OfType<MetadataFieldCandidate>()
            .ToArray();
        var artworkSource = _artworkReviewed ? ArtworkReview.SelectedCandidate?.Source.Name : null;
        var samples = Metadata.ScreenshotUrls.ToArray();
        var actors = Metadata.Actors.ToArray();
        var preserveSamples = _artworkReviewed;

        // Retain candidates from local/manual web imports absent from the retry response.
        var sources = onlineSources.Concat(SourceResults)
            .DistinctBy(source => MetadataCandidateSource.FromMetadata(source).Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ApplyOnlineSources(preferredOnlineMetadata, sources, attempts, sourceProfile, resolvedArtworkBundleSource);
        foreach (var previous in selections)
        {
            var candidate = MetadataReview.GetCandidates(previous.Field).FirstOrDefault(item =>
                string.Equals(item.Source.Name, previous.Source.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Value, previous.Value, StringComparison.Ordinal));
            if (candidate is null || previous.Source.IsManual)
            {
                MetadataReview.SetManualValue(previous.Field, previous.Value);
                if (previous.Field == MetadataField.Actors)
                {
                    Metadata.Actors = actors;
                }
            }
            else
            {
                MetadataReview.SelectCandidate(previous.Field, previous.Source.Name);
            }
        }
        if (artworkSource is not null)
        {
            SelectArtworkSource(artworkSource);
        }
        if (preserveSamples)
        {
            Metadata.ScreenshotUrls = samples;
        }
        return Metadata;
    }

    public int ApplyManualWebSource(MovieMetadata onlineSource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(onlineSource);

        var source = MetadataCandidateSource.FromMetadata(onlineSource);
        if (string.IsNullOrWhiteSpace(source.Name) || source.Name == "unknown")
        {
            throw new InvalidDataException("网页导入结果没有可识别的资料来源。 ");
        }

        var currentId = MovieIdParser.Normalize(Metadata.Id);
        var importedId = MovieIdParser.Normalize(onlineSource.Id);
        if (!string.IsNullOrWhiteSpace(currentId) &&
            !string.IsNullOrWhiteSpace(importedId) &&
            !string.Equals(currentId, importedId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"网页影片番号 {importedId} 与当前番号 {currentId} 不一致。 ");
        }

        MergeAvailableScreenshotUrls([onlineSource]);
        if (HasOnlineExtrafanartCandidates([onlineSource]))
        {
            _onlineExtrafanartCandidateId = MovieIdParser.Normalize(Metadata.Id);
            OnPropertyChanged(nameof(CanReplaceLocalExtrafanart));
        }

        var importedSnapshot = MetadataSourceSnapshot.FromMetadata(onlineSource);
        var fieldsToPromote = Enum.GetValues<MetadataField>()
            .Where(field => !string.IsNullOrWhiteSpace(importedSnapshot.GetValue(field)))
            .ToArray();
        var retainedManualCandidates = CaptureManualCandidates();
        var currentMetadata = CloneMetadata(Metadata);
        ApplyImportedSupportingMetadata(currentMetadata, onlineSource);
        var updatedSources = SourceResults
            .Where(existing => !string.Equals(
                MetadataCandidateSource.FromMetadata(existing).Name,
                source.Name,
                StringComparison.OrdinalIgnoreCase))
            .Append(onlineSource)
            .ToArray();

        var importedArtwork = new ArtworkCoverCandidate(
            source,
            onlineSource.CoverUrl,
            onlineSource.FallbackCoverUrl,
            onlineSource.PosterUrl);
        if (importedArtwork.HasCover)
        {
            PreferredArtworkSourceName = source.Name;
            _artworkReviewed = true;
        }

        ReplaceMetadata(currentMetadata, updatedSources, retainedManualCandidates);
        foreach (var field in fieldsToPromote)
        {
            MetadataReview.SelectCandidate(field, source.Name);
        }

        if (SearchState is not MovieSearchState.Searching)
        {
            SearchState = MovieSearchState.NeedsReview;
        }

        return fieldsToPromote.Length;
    }

    public void MarkSearchFailed(
        IReadOnlyList<MetadataSourceSearchAttempt> attempts,
        Exception exception)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(exception);
        LastSearchAttempts = attempts.ToArray();
        LastSearchError = exception;
        SearchState = MovieSearchState.SearchFailed;
    }

    public void MarkSearchCanceled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastSearchError = null;
        SearchState = MovieSearchState.SearchCanceled;
    }

    public void SetLocalArtwork(ArtworkCoverCandidate? candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LocalArtworkCandidate = candidate;
        if (candidate is not null)
        {
            PreferredArtworkSourceName = candidate.Source.Name;
        }

        RebuildArtworkReview();
        TouchReview();
    }

    public void SetLocalExtrafanart(IEnumerable<string> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paths);
        LocalExtrafanartPaths = NormalizeScreenshotUrls(paths);
        AvailableScreenshotUrls = NormalizeScreenshotUrls(
            LocalExtrafanartPaths.Concat(AvailableScreenshotUrls));
        // Loading sidecars is not an explicit online artwork/sample review choice.
        _applyingAutomaticSelection = true;
        try
        {
            Metadata.ScreenshotUrls = NormalizeScreenshotUrls(
                LocalExtrafanartPaths.Concat(Metadata.ScreenshotUrls));
        }
        finally
        {
            _applyingAutomaticSelection = false;
        }
    }

    public void RemoveLocalArtworkLocations(IEnumerable<string> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paths);
        var removedPaths = NormalizeScreenshotUrls(paths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removedPaths.Count == 0)
        {
            return;
        }

        LocalExtrafanartPaths = LocalExtrafanartPaths
            .Where(path => !removedPaths.Contains(path))
            .ToArray();
        AvailableScreenshotUrls = AvailableScreenshotUrls
            .Where(path => !removedPaths.Contains(path))
            .ToArray();
        Metadata.ScreenshotUrls = NormalizeScreenshotUrls(
            Metadata.ScreenshotUrls.Where(path => !removedPaths.Contains(path)));

        if (LocalArtworkCandidate?.IsSidecarPair == true)
        {
            var posterPath = removedPaths.Contains(LocalArtworkCandidate.LocalPosterPath)
                ? null
                : LocalArtworkCandidate.LocalPosterPath;
            var fanartPath = removedPaths.Contains(LocalArtworkCandidate.LocalFanartPath)
                ? null
                : LocalArtworkCandidate.LocalFanartPath;
            LocalArtworkCandidate = string.IsNullOrWhiteSpace(posterPath) && string.IsNullOrWhiteSpace(fanartPath)
                ? null
                : ArtworkCoverCandidate.CreateSidecarPair(
                    LocalArtworkCandidate.Source,
                    posterPath,
                    fanartPath);
            RebuildArtworkReview();
        }

        TouchReview();
    }

    public void SetManualArtwork(ArtworkCoverCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(candidate);
        ManualArtworkCandidate = candidate;
        _artworkReviewed = true;
        PreferredArtworkSourceName = candidate.Source.Name;
        RebuildArtworkReview();
        TouchReview();
    }

    public bool SelectArtworkSource(string sourceName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ArtworkReview.SelectSource(sourceName))
        {
            return false;
        }

        PreferredArtworkSourceName = ArtworkReview.SelectedCandidate?.Source.Name ?? sourceName;
        _artworkReviewed = true;
        InvalidateCoverResolution();
        TouchReview();
        return true;
    }

    public void UpdateVideoPath(string videoPath)
    {
        UpdateVideoPaths([videoPath]);
    }

    public void UpdateVideoPaths(IEnumerable<string> videoPaths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(videoPaths);
        SetVideoPaths(MovieFileSet.Create(videoPaths));
        OnPropertyChanged(nameof(VideoPath));
        OnPropertyChanged(nameof(VideoPaths));
        OnPropertyChanged(nameof(MovieFileGroupKey));
        OnPropertyChanged(nameof(SidecarBaseName));
        OnPropertyChanged(nameof(VideoPartCount));
        OnPropertyChanged(nameof(IsMultipart));
        OnPropertyChanged(nameof(FileName));
    }

    public bool AddVideoPaths(IEnumerable<string> videoPaths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(videoPaths);
        var combined = _videoPaths
            .Concat(videoPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (combined.Length == _videoPaths.Count)
        {
            return false;
        }

        UpdateVideoPaths(combined);
        TouchReview();
        return true;
    }

    public LocalSaveContext CreateLocalSaveContext() => new(
        LocalMetadataBundle,
        LocalArtworkCandidate,
        ArtworkReview.SelectedCandidate)
    {
        LocalExtrafanartPaths = LocalExtrafanartPaths,
        CanReplaceLocalExtrafanart = CanReplaceLocalExtrafanart
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _metadata.PropertyChanged -= Metadata_PropertyChanged;
        _metadataReview.SelectionChanged -= MetadataReview_SelectionChanged;
        _metadataReview.Dispose();
        _disposed = true;
    }

    private MetadataFieldCandidate[] CaptureManualCandidates() =>
        Enum.GetValues<MetadataField>()
            .SelectMany(field => MetadataReview.GetCandidates(field))
            .Where(candidate => candidate.Source.IsManual)
            .ToArray();

    private void ApplySourceProfile(
        MetadataSourcePreferenceProfile? sourceProfile,
        string? resolvedArtworkBundleSource)
    {
        _applyingAutomaticSelection = true;
        try
        {
            if (sourceProfile is not null)
            {
                var normalized = MetadataSourcePreferenceProfile.Normalize(sourceProfile);
                MetadataSourcePreferenceApplier.Apply(_metadataReview, normalized);
                var artworkSource = normalized.ArtworkSource == MetadataSourcePreferenceProfile.BestArtworkResolution
                    ? resolvedArtworkBundleSource
                    : normalized.ArtworkSource;
                if (!string.IsNullOrWhiteSpace(artworkSource) && _artworkReview.SelectSource(artworkSource))
                {
                    PreferredArtworkSourceName = _artworkReview.SelectedCandidate?.Source.Name;
                    ApplyOnlineArtworkBundle(artworkSource);
                }
            }
        }
        finally
        {
            _applyingAutomaticSelection = false;
        }
    }

    private void ApplyOnlineArtworkBundle(string sourceName)
    {
        var source = SourceResults.FirstOrDefault(candidate => string.Equals(
            MetadataCandidateSource.FromMetadata(candidate).Name,
            sourceName,
            StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return;
        }

        Metadata.ScreenshotUrls = NormalizeScreenshotUrls(
            LocalExtrafanartPaths.Concat(source.ScreenshotUrls));
    }

    private void MarkOnlineExtrafanartCandidates(IEnumerable<MovieMetadata> sources)
    {
        _onlineExtrafanartCandidateId = HasOnlineExtrafanartCandidates(sources)
            ? MovieIdParser.Normalize(Metadata.Id)
            : null;
        OnPropertyChanged(nameof(CanReplaceLocalExtrafanart));
    }

    private static bool HasOnlineExtrafanartCandidates(IEnumerable<MovieMetadata> sources) =>
        sources.SelectMany(source => source.ScreenshotUrls).Any(location =>
            ArtworkLocationHelper.IsSupported(location) &&
            !ArtworkLocationHelper.TryGetLocalPath(location, out _));

    private void ReplaceMetadata(
        MovieMetadata metadata,
        IReadOnlyList<MovieMetadata> sourceResults,
        IReadOnlyList<MetadataFieldCandidate> retainedManualCandidates)
    {
        metadata.ScreenshotUrls = NormalizeScreenshotUrls(
            LocalExtrafanartPaths.Concat(metadata.ScreenshotUrls));
        if (_metadata is not null)
        {
            _metadata.PropertyChanged -= Metadata_PropertyChanged;
        }

        if (_metadataReview is not null)
        {
            _metadataReview.SelectionChanged -= MetadataReview_SelectionChanged;
            _metadataReview.Dispose();
        }

        _metadata = metadata;
        OnPropertyChanged(nameof(Metadata));
        _metadata.PropertyChanged += Metadata_PropertyChanged;
        _sourceResults = sourceResults.ToArray();
        _metadataReview = MetadataReviewSession.Create(metadata, _sourceResults.ToArray());
        foreach (var manualCandidate in retainedManualCandidates)
        {
            var selectedCandidate = _metadataReview.GetSelectedCandidate(manualCandidate.Field);
            _metadataReview.SetManualValue(manualCandidate.Field, manualCandidate.Value);
            if (selectedCandidate is not null)
            {
                _metadataReview.SelectCandidate(manualCandidate.Field, selectedCandidate.Source.Name);
            }
        }

        _metadataReview.SelectionChanged += MetadataReview_SelectionChanged;
        RebuildArtworkReview();
        TouchReview();
    }

    private void ReplaceAvailableScreenshotUrls(IEnumerable<MovieMetadata> sources)
    {
        AvailableScreenshotUrls = NormalizeScreenshotUrls(
            LocalExtrafanartPaths.Concat(sources.SelectMany(source => source.ScreenshotUrls)));
    }

    private void MergeAvailableScreenshotUrls(IEnumerable<MovieMetadata> sources)
    {
        AvailableScreenshotUrls = NormalizeScreenshotUrls(
            AvailableScreenshotUrls.Concat(sources.SelectMany(source => source.ScreenshotUrls)));
    }

    private static IReadOnlyList<string> NormalizeScreenshotUrls(IEnumerable<string> urls) =>
        urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void RebuildArtworkReview()
    {
        var additionalCandidates = new[] { LocalArtworkCandidate, ManualArtworkCandidate }
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
        _artworkReview = ArtworkCoverReviewSession.CreateWithAdditionalCandidates(
            Metadata,
            additionalCandidates,
            PreferredArtworkSourceName,
            SourceResults.ToArray());
        if (_artworkReview.SelectedCandidate is not null)
        {
            PreferredArtworkSourceName = _artworkReview.SelectedCandidate.Source.Name;
        }
        InvalidateCoverResolution();
    }

    private static string GetMetadataFieldValue(MovieMetadata metadata, MetadataField field) => field switch
    {
        MetadataField.Title => metadata.Title,
        MetadataField.OriginalTitle => metadata.OriginalTitle,
        MetadataField.ReleaseDate => metadata.ReleaseDate,
        MetadataField.RuntimeMinutes => metadata.RuntimeMinutes,
        MetadataField.Maker => metadata.Maker,
        MetadataField.Director => metadata.Director,
        MetadataField.Label => metadata.Label,
        MetadataField.Actors => metadata.ActorsText,
        MetadataField.Genres => metadata.GenresText,
        MetadataField.Plot => metadata.Plot,
        MetadataField.Rating => metadata.Rating,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static MovieMetadata CloneMetadata(MovieMetadata source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        OriginalTitle = source.OriginalTitle,
        ReleaseDate = source.ReleaseDate,
        Director = source.Director,
        Maker = source.Maker,
        Label = source.Label,
        Series = source.Series,
        RuntimeMinutes = source.RuntimeMinutes,
        ActorsText = source.ActorsText,
        GenresText = source.GenresText,
        Plot = source.Plot,
        Rating = source.Rating,
        ContentId = source.ContentId,
        PosterUrl = source.PosterUrl,
        CoverUrl = source.CoverUrl,
        FallbackCoverUrl = source.FallbackCoverUrl,
        SourceUrl = source.SourceUrl,
        SourceName = source.SourceName,
        SourceDisplayName = source.SourceDisplayName,
        Actors = source.Actors.Select(actor => new ActorMetadata(actor.Name, actor.ImageUrl)).ToArray(),
        ScreenshotUrls = source.ScreenshotUrls.ToArray()
    };

    private static void ApplyImportedSupportingMetadata(MovieMetadata target, MovieMetadata source)
    {
        if (!string.IsNullOrWhiteSpace(source.ContentId))
        {
            target.ContentId = source.ContentId;
        }

        if (source.ScreenshotUrls.Count > 0)
        {
            target.ScreenshotUrls = source.ScreenshotUrls.ToArray();
        }

        target.SourceName = source.SourceName;
        target.SourceDisplayName = source.SourceDisplayName;
        target.SourceUrl = source.SourceUrl;
    }

    private void Metadata_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs) =>
        HandleMetadataPropertyChanged(eventArgs);

    private void HandleMetadataPropertyChanged(PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MovieMetadata.Id) or nameof(MovieMetadata.CoverUrl) or
            nameof(MovieMetadata.FallbackCoverUrl) or nameof(MovieMetadata.PosterUrl))
            InvalidateCoverResolution();
        if (eventArgs.PropertyName == nameof(MovieMetadata.ScreenshotUrls) && !_applyingAutomaticSelection)
        {
            _artworkReviewed = true;
        }
        if (eventArgs.PropertyName == nameof(MovieMetadata.Id) && SearchState is not MovieSearchState.Searching)
        {
            _onlineExtrafanartCandidateId = null;
            OnPropertyChanged(nameof(CanReplaceLocalExtrafanart));
            LastSearchAttempts = [];
            LastSearchError = null;
            SearchState = string.IsNullOrWhiteSpace(MovieIdParser.Normalize(Metadata.Id))
                ? MovieSearchState.NeedsId
                : MovieSearchState.Searchable;
        }

        TouchReview();

        MetadataPropertyChanged?.Invoke(this, eventArgs);
    }

    private void CompleteSearch(IReadOnlyList<MetadataSourceSearchAttempt>? attempts)
    {
        LastSearchAttempts = attempts?.ToArray() ?? [];
        LastSearchError = null;
        SearchState = MovieSearchState.NeedsReview;
    }

    private void MetadataReview_SelectionChanged(
        object? sender,
        MetadataSelectionChangedEventArgs eventArgs)
    {
        if (!_applyingAutomaticSelection)
        {
            _reviewedFields.Add(eventArgs.Field);
        }
        TouchReview();
        MetadataSelectionChanged?.Invoke(this, eventArgs);
    }

    private void TouchReview()
    {
        ReviewRevision++;
        if (SaveState is MovieSaveState.Completed or
            MovieSaveState.SaveFailed or
            MovieSaveState.Conflict or
            MovieSaveState.SaveCanceled)
        {
            LastSaveError = null;
            SaveState = MovieSaveState.Idle;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetVideoPaths(MovieFileSet fileSet)
    {
        _videoPaths = fileSet.VideoPaths;
        _movieFileGroupKey = fileSet.GroupKey;
        _sidecarBaseName = fileSet.MovieBaseName;
    }
}
