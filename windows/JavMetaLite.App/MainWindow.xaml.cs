using System.ComponentModel;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Data;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using Microsoft.Win32;

namespace JavMetaLite.App;

public partial class MainWindow : Window
{
    public static string ApplicationVersion { get; } = typeof(MainWindow).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    internal static string FormatVersionText(string version) => LocalizationService.Get(
        version.Split('+')[0].Contains('-') ? "Main.PreviewVersionFormat" : "Main.CurrentVersionFormat", version);

    private sealed record MetadataSearchOutcome(
        MovieMetadata Metadata,
        IReadOnlyList<MovieMetadata> Sources,
        IReadOnlyList<MetadataSourceSearchAttempt> Attempts)
    {
        public static MetadataSearchOutcome FromSingleAttempt(MetadataSourceSearchAttempt attempt) =>
            new(attempt.Metadata!, [attempt.Metadata!], [attempt]);

        public static MetadataSearchOutcome FromMultipleSources(MultiSourceSearchResult result) =>
            new(result.Metadata, result.Sources, result.Attempts);
    }

    private readonly JavLibraryClient _javLibraryClient = new();
    private readonly LibreDmmClient _libreDmmClient = new();
    private readonly R18DevClient _r18DevClient = new();
    private readonly OutputService _outputService;
    private readonly FileOrganizationService _fileOrganizationService;
    private readonly AppPreferencesStore _preferencesStore;
    private readonly HttpClient _previewHttpClient = CreatePreviewClient();
    private readonly ConcurrentDictionary<string, byte[]> _previewImageCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _previewImageCacheOrder = new();
    private readonly ConcurrentDictionary<string, ArtworkPixelSize> _previewImageDimensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _previewDimensionOrder = new();
    private readonly CoverResolutionMonitor _coverResolutionMonitor;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ObservableCollection<MovieJob> _movieQueue = [];
    private MetadataSourcePreferenceProfile _customSourceProfile = new();
    private readonly ICollectionView _movieQueueView;
    private readonly Dictionary<MovieJob, MovieJobLoadResult> _jobLoadResults = [];
    private readonly MoviePreviewCache _jobPreviews = new();
    private readonly QueueRefreshCoordinator _queueRefresh;
    private (int Width, int Height)? _fanartDimensions;
    private bool _artworkPreviewDeferred;
    private bool _activePreviewReady;
    private MovieJob _activeJob = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private string? _lastValidCustomRootDirectory;
    private string? _customRootAvailabilityCheckPath;
    private readonly List<string> _recentCustomRootDirectories = [];
    private string? _targetConfigurationError;
    private bool _busy;
    private bool _closeRequested;
    private bool _savingAcrossVolumes;
    private AppDialogWindow? _transferCloseDialog;
    private bool _uiInitialized;
    private bool _preferencesLoaded;
    private bool _preferencesCanOverwrite = true;
    private bool _changingQueueSelection;
    private Key? _queueNavigationKey;
    private int _queueFocusVersion;
    private int? _pendingQueueFocusVersion;
    private bool _applyingSaveConfiguration;
    private bool _saveSettingsExpanded;
    private bool? _searchToolbarWrapped;
    private bool _restoringSourceSelection;
    private CancellationTokenSource? _navigationPreviewCancellation;
    private long _statusRevision;
    private CancellationToken CurrentOperationToken =>
        _activeOperationCancellation?.Token ?? _lifetimeCancellation.Token;

    public MainWindow() : this(new AppPreferencesStore())
    {
    }

    internal MainWindow(AppPreferencesStore preferencesStore)
    {
        _preferencesStore = preferencesStore;
        _outputService = new OutputService(WindowsArtworkImageProcessor.Instance);
        _fileOrganizationService = new FileOrganizationService(_outputService);
        LocalizationService.ApplyLanguage(LocalizationService.CurrentLanguageCode);
        InitializeComponent();
        foreach (var row in SourceComboBox.Items.OfType<ComboBoxItem>())
            row.AddHandler(Mouse.MouseEnterEvent, new MouseEventHandler(SourceItem_MouseEnter), handledEventsToo: true);
        _coverResolutionMonitor = new CoverResolutionMonitor(Dispatcher,
            (locations, token) => CoverResolutionCheck.MeasureAsync(locations, ProbeCoverResolutionAsync, token),
            RefreshCoverCheckButton);
        _r18DevClient.CoolingDown += R18CoolingDown;
        WindowVisualTheme.Attach(this);
        SelectLanguageItem(LocalizationService.CurrentLanguageCode);
        _activeJob.MetadataPropertyChanged += Metadata_PropertyChanged;
        _activeJob.MetadataSelectionChanged += MetadataReview_SelectionChanged;
        AttachMovieJob(_activeJob);
        _movieQueueView = CollectionViewSource.GetDefaultView(_movieQueue);
        _queueRefresh = new QueueRefreshCoordinator(Dispatcher, RefreshQueueUiCore);
        _movieQueueView.Filter = FilterQueueItem;
        MovieQueueList.ItemsSource = _movieQueueView;
        DeskLayout.SizeChanged += (_, _) => PositionOutputDrawer();
        QueuePanel.SizeChanged += (_, _) => PositionOutputDrawer();
        ArtworkPanel.SizeChanged += (_, _) => PositionOutputDrawer();
        _uiInitialized = true;
        DataContext = _activeJob.Metadata;
        RefreshSourceBadges();
        RefreshArtworkSourceBadge();
        RefreshSourceSelectionUi();
        RefreshRetryFailedSourcesUi();
        RefreshArtworkPreviewStates();
        RefreshWorkspacePresentation();
        RefreshQueueUi();
        RefreshTargetLocationUi();
        VersionText.Text = FormatVersionText(ApplicationVersion);
        AppLog.Info($"JavMetaLite v{ApplicationVersion} 启动");
    }

    internal void LoadPreferences()
    {
        AppPreferencesLoadResult result;
        try
        {
            result = _preferencesStore.Load();
        }
        catch (Exception exception)
        {
            AppLog.Warning("偏好配置载入失败，已使用安全默认值", exception);
            result = new AppPreferencesLoadResult(
                AppPreferences.CreateSafeDefaults(),
                $"偏好配置载入失败，已使用安全默认值：{exception.Message}");
        }

        _preferencesLoaded = true;
        _preferencesCanOverwrite = result.CanOverwrite;
        ApplyPreferences(result.Preferences);
        if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            AppLog.Warning(result.Warning);
            SetStatus(
                LocalizationService.Get(result.CanOverwrite
                    ? "Status.SettingsFallback"
                    : "Status.SettingsFuture"),
                false);
        }
        else if (result.Preferences.RememberSavePreferences)
        {
            AppLog.Info(
                $"已恢复当前设置 target={result.Preferences.TargetMode} " +
                $"searchSource={result.Preferences.SearchSourceMode} " +
                $"rename={result.Preferences.RenameVideo} skipSavePreview={result.Preferences.SkipSavePreview} " +
                $"includeIdInTitle={result.Preferences.IncludeIdInTitle} " +
                $"crossVolumeVerification={result.Preferences.CrossVolumeVerification} " +
                $"customRoot={result.Preferences.CustomRootDirectory}");
            SetStatus(
                result.Preferences.SkipSavePreview
                    ? LocalizationService.Get("Status.PreferencesSkipPreview")
                    : LocalizationService.Get("Status.PreferencesRestored"),
                !result.Preferences.SkipSavePreview);
        }
    }

    private void ApplyPreferences(AppPreferences preferences)
    {
        _queuePreference = preferences.QueueVisible;
        AppearanceChoice.SelectedValuePath = "Tag";
        AppearanceChoice.SelectedValue = preferences.Appearance;
        WindowVisualTheme.SetAppearance(preferences.Appearance);
        ApplyLanguagePreference(preferences.UiLanguage);
        SkipSavePreviewCheckBox.IsChecked = preferences.SkipSavePreview;
        SkipCrossVolumeVerificationCheckBox.IsChecked =
            preferences.CrossVolumeVerification is CrossVolumeVerificationMode.FileSizeOnly;
        RememberPreferencesCheckBox.IsChecked = preferences.RememberSavePreferences;
        WriteNfoCheckBox.IsChecked = preferences.WriteNfo;
        IncludeIdInTitleCheckBox.IsChecked = preferences.IncludeIdInTitle;
        DownloadPosterCheckBox.IsChecked = preferences.DownloadPoster;
        DownloadFanartCheckBox.IsChecked = preferences.DownloadFanart;
        DownloadExtrafanartCheckBox.IsChecked = preferences.DownloadExtrafanart;
        ReplaceLocalExtrafanartCheckBox.IsChecked = preferences.ReplaceLocalExtrafanart;
        RenameVideoCheckBox.IsChecked = preferences.RenameVideo;
        SourceComboBox.SelectedItem = SourceComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                MetadataSearchSourceModes.Normalize(preferences.SearchSourceMode),
                StringComparison.OrdinalIgnoreCase))
            ?? SourceComboBox.Items[0];
        _customSourceProfile = MetadataSourcePreferenceProfile.Normalize(preferences.CustomSourceProfile);
        _recentCustomRootDirectories.Clear();
        _recentCustomRootDirectories.AddRange(CustomRootHistory.Normalize(
            preferences.RecentCustomRootDirectories));
        CustomRootTextBox.Text = preferences.CustomRootDirectory ?? string.Empty;
        _lastValidCustomRootDirectory = preferences.CustomRootDirectory;
        _customRootAvailabilityCheckPath = CustomRootHistory.TryNormalizePath(
            preferences.CustomRootDirectory,
            out var normalizedCustomRoot)
            ? normalizedCustomRoot
            : null;
        RefreshRecentRootsButton();

        TargetModeComboBox.SelectedItem = TargetModeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                preferences.TargetMode.ToString(),
                StringComparison.Ordinal))
            ?? TargetModeComboBox.Items[0];
        RefreshTargetLocationUi();
        RefreshSourceSelectionUi();
        RefreshSaveSettingsPresentation();
    }

    private AppPreferences CapturePreferences()
    {
        return new AppPreferences
        {
            UiLanguage = LocalizationService.CurrentLanguageCode,
            QueueVisible = _queuePreference,
            Appearance = WindowVisualTheme.Appearance,
            RememberSavePreferences = RememberPreferencesCheckBox.IsChecked == true,
            SearchSourceMode = GetSelectedSourceMode(),
            SkipSavePreview = SkipSavePreviewCheckBox.IsChecked == true,
            CrossVolumeVerification = GetCrossVolumeVerificationMode(),
            TargetMode = GetSelectedTargetMode(),
            CustomRootDirectory = string.IsNullOrWhiteSpace(CustomRootTextBox.Text)
                ? _lastValidCustomRootDirectory
                : CustomRootTextBox.Text,
            RecentCustomRootDirectories = _recentCustomRootDirectories.ToArray(),
            RenameVideo = RenameVideoCheckBox.IsChecked == true,
            WriteNfo = WriteNfoCheckBox.IsChecked == true,
            IncludeIdInTitle = IncludeIdInTitleCheckBox.IsChecked == true,
            DownloadPoster = DownloadPosterCheckBox.IsChecked == true,
            DownloadFanart = DownloadFanartCheckBox.IsChecked == true,
            DownloadExtrafanart = DownloadExtrafanartCheckBox.IsChecked == true,
            ReplaceLocalExtrafanart = DownloadExtrafanartCheckBox.IsChecked == true &&
                                      ReplaceLocalExtrafanartCheckBox.IsChecked == true,
            CustomSourceProfile = MetadataSourcePreferenceProfile.Normalize(_customSourceProfile)
        };
    }

    private void PersistPreferencesOnClose()
    {
        if (!_preferencesLoaded)
        {
            return;
        }

        if (!_preferencesCanOverwrite)
        {
            AppLog.Warning("检测到不受支持版本的偏好配置，本次关闭不会覆盖该文件");
            return;
        }

        try
        {
            var preferences = CapturePreferences();
            _preferencesStore.Save(preferences);
            if (preferences.RememberSavePreferences)
            {
                AppLog.Info(
                    $"已保存偏好 target={preferences.TargetMode} rename={preferences.RenameVideo} " +
                    $"searchSource={preferences.SearchSourceMode} " +
                    $"skipSavePreview={preferences.SkipSavePreview} " +
                    $"includeIdInTitle={preferences.IncludeIdInTitle} " +
                    $"path={_preferencesStore.SettingsPath}");
            }
            else
            {
                AppLog.Info(
                    $"未启用当前设置记忆，仅保留界面语言和自定义来源规则 language={preferences.UiLanguage}");
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning("无法保存安全偏好，影片与 metadata 不受影响", exception);
        }
    }

    private void ApplyLanguagePreference(string? languageCode)
    {
        LocalizationService.ApplyLanguage(languageCode);
        SelectLanguageItem(LocalizationService.CurrentLanguageCode);
        RefreshLocalizedPresentation();
    }

    private void SelectLanguageItem(string languageCode)
    {
        LanguageComboBox.SelectedItem = LanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                languageCode,
                StringComparison.OrdinalIgnoreCase));
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized ||
            LanguageComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string languageCode ||
            string.Equals(languageCode, LocalizationService.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LocalizationService.ApplyLanguage(languageCode);
        RefreshLocalizedPresentation();
        SetStatus(LocalizationService.Get("Status.LanguageChanged"), true);
        AppLog.Info($"界面语言已切换 language={languageCode}");
    }

    private void RefreshLocalizedPresentation()
    {
        if (!_uiInitialized)
        {
            return;
        }

        VersionText.Text = FormatVersionText(ApplicationVersion);
        RefreshRecentRootsButton();
        RefreshTargetLocationUi();
        RefreshSourceBadges();
        RefreshArtworkSourceBadge();
        RefreshSourceSelectionUi();
        RefreshArtworkPreviewStates();
        RefreshWorkspacePresentation();
    }

    private string? _lastInputFolder;

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var initial = _lastInputFolder ?? Path.GetDirectoryName(_activeJob.VideoPath) ??
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(initial) || !Directory.Exists(initial))
            initial = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var picker = new MovieInputPicker(initial) { Owner = this };
        if (picker.ShowDialog() != true) return;
        _lastInputFolder = picker.CurrentDirectory;
        await ImportInputsAsync(picker.SelectedPaths);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = _busy ? DragDropEffects.None : e.AllowedEffects & DragDropEffects.Copy;
        DropOverlay.Visibility = e.Effects == DragDropEffects.Copy ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!TryGetDroppedPaths(e.Data, out var paths)) return;
        e.Effects = _busy ? DragDropEffects.None : e.AllowedEffects & DragDropEffects.Copy;
        e.Handled = true;
        if (e.Effects != DragDropEffects.Copy) return;

        // Activate during the drop so closing the import review returns to its owner.
        Activate();
        await ImportInputsAsync(paths);
    }

    internal async Task ImportInputsAsync(IReadOnlyList<string> paths)
    {
        if (_busy || paths.Count == 0) return;
        await ImportDiscoveredPathsAsync(paths);
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var job = _activeJob;
        if (string.IsNullOrWhiteSpace(job.Metadata.Id))
        {
            ShowError(LocalizationService.Get("Error.EnterId"));
            return;
        }

        var source = GetSelectedSourceMode();
        if (source == "manual")
        {
            SetStatus(LocalizationService.Get("Status.ManualMode"), true);
            return;
        }

        string? browserFallbackUrl = null;
        var sourceProfile = CaptureSearchSourceProfile(source);
        var busyMessage = source == MetadataSearchSourceModes.Custom
            ? LocalizationService.Get("Status.SearchingMulti")
            : LocalizationService.Get("Status.SearchingSingle");
        await RunBusyAsync(busyMessage, async () =>
        {
            job.BeginSearch();
            var searchCompleted = false;
            try
            {
                var outcome = await SearchFromSelectedSourceAsync(source, job.Metadata.Id);
                var resolvedArtworkBundleSource = await ResolveBestArtworkSourceAsync(
                    sourceProfile,
                    outcome.Sources,
                    CurrentOperationToken);
                var result = job.ApplyOnlineSources(
                    outcome.Metadata,
                    outcome.Sources,
                    outcome.Attempts,
                    sourceProfile,
                    resolvedArtworkBundleSource);
                searchCompleted = true;
                _coverResolutionMonitor.Track(job);
                if (ReferenceEquals(job, _activeJob))
                {
                    RefreshActiveJobPresentation();
                }
                var successfulSources = string.Join(
                    "+",
                    outcome.Sources.Select(GetSourceDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                var failedSources = string.Join(
                    "+",
                    outcome.Attempts.Where(attempt => !attempt.Success).Select(attempt => attempt.SourceDisplayName));
                AppLog.Info(
                    $"metadata 搜索成功 sources={successfulSources} failedSources={failedSources} id={result.Id} " +
                    $"contentId={result.ContentId} screenshots={result.ScreenshotUrls.Count} " +
                    $"reviewSources={outcome.Sources.Count} onlineDefault=true localCandidate={job.LocalSourceMetadata is not null}");
                var artworkLoaded = await LoadSelectedArtworkPreviewAsync();
                var sourceName = string.Join(
                    " + ",
                    outcome.Sources.Select(GetSourceDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                var degradedNote = string.IsNullOrWhiteSpace(failedSources)
                    ? string.Empty
                    : LocalizationService.Get("Status.SourceFailed", failedSources);
                var imageSummary = result.ScreenshotUrls.Count > 0
                    ? LocalizationService.Get("Status.ImagesFound", result.ScreenshotUrls.Count)
                    : LocalizationService.Get("Status.NoImages");
                var localPrefix = job.LocalSourceMetadata is null
                    ? LocalizationService.Get("Status.LoadedOnline", sourceName, result.Id)
                    : LocalizationService.Get("Status.LoadedOnlineWithLocal", sourceName);
                var details = artworkLoaded.Poster
                    ? $"{localPrefix}{imageSummary}{(artworkLoaded.Fanart ? string.Empty : LocalizationService.Get("Status.FanartPreviewMissing"))}{degradedNote}"
                    : $"{localPrefix}{LocalizationService.Get("Status.CoverPreviewMissing")}{degradedNote}";
                var notice = !string.IsNullOrWhiteSpace(failedSources) ? LocalizationService.Get("Queue.Search.Partial")
                    : !artworkLoaded.Poster || !artworkLoaded.Fanart ? LocalizationService.Get("Footer.ArtworkIncomplete")
                    : null;
                SetResultStatus("Footer.Found", 1, details, notice);
            }
            catch (JavLibraryChallengeException exception)
            {
                job.MarkSearchFailed([], exception);
                browserFallbackUrl = exception.Url;
                SetStatus(LocalizationService.Get("Status.BrowserVerification"), false);
            }
            catch (OperationCanceledException) when (CurrentOperationToken.IsCancellationRequested)
            {
                if (!searchCompleted)
                {
                    job.MarkSearchCanceled();
                }
                throw;
            }
            catch (Exception exception)
            {
                if (!searchCompleted)
                {
                    job.MarkSearchFailed(GetSearchAttempts(exception), exception);
                }
                throw;
            }
        });

        if (browserFallbackUrl is not null)
        {
            await OpenBrowserAsync(new BrowserImportTarget(
                BrowserImportRouting.JavLibrary,
                BrowserImportRouting.GetDisplayName(BrowserImportRouting.JavLibrary),
                browserFallbackUrl,
                false));
        }
    }

    private async Task<MetadataSearchOutcome> SearchFromSelectedSourceAsync(string source, string id)
    {
        AppLog.Info($"开始搜索 metadata source={source} id={id}");
        try
        {
            var providers = MetadataSearchSourceModes.AutomaticSources(source)
                .Select(GetMetadataProvider).ToArray();
            if (providers.Length == 0)
            {
                throw new InvalidOperationException(LocalizationService.Get("Status.ManualMode"));
            }
            if (providers.Length == 1)
            {
                return MetadataSearchOutcome.FromSingleAttempt(
                    await MetadataSearchCoordinator.SearchSingleAsync(id, providers[0], CurrentOperationToken));
            }
            var multiSourceResult = await MetadataSearchCoordinator.SearchAllAsync(
                id, providers[0], providers[1], CurrentOperationToken);
            return MetadataSearchOutcome.FromMultipleSources(multiSourceResult);
        }
        catch (Exception exception)
        {
            AppLog.Error($"metadata 搜索失败 source={source} id={id}", exception);
            throw;
        }
    }

    private async void ManualWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string source } || !ManualSearchItem.IsEnabled) return;
        e.Handled = true;
        var target = CurrentBrowserTarget(source);
        ManualWebsiteChoices.Visibility = Visibility.Collapsed;
        SourceComboBox.IsDropDownOpen = false;
        await OpenBrowserAsync(target);
    }

    private BrowserImportTarget CurrentBrowserTarget(string source)
    {
        var id = MovieIdParser.Normalize(_activeJob.Metadata.Id);
        // An edited number must never reuse the previous movie's detail URL.
        var matches = _activeJob.SourceResults.Where(result =>
            string.Equals(MovieIdParser.Normalize(result.Id), id, StringComparison.OrdinalIgnoreCase));
        return BrowserImportRouting.BuildTarget(source, id, matches);
    }

    private async void RetryFailedSources_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        if (UseBatchActions)
        {
            await RetryQueueFailedSourcesAsync();
            return;
        }
        var job = _activeJob;
        var failedAttempts = job.LastSearchAttempts
            .Where(attempt => !attempt.Success && IsRetryableMetadataSource(attempt.SourceName))
            .ToArray();
        if (failedAttempts.Length == 0 || string.IsNullOrWhiteSpace(MovieIdParser.Normalize(job.Metadata.Id)))
        {
            return;
        }

        var previousAttempts = job.LastSearchAttempts.ToArray();
        var sourceProfile = CaptureSearchSourceProfile(GetSelectedSourceMode());
        var failedProviders = failedAttempts
            .Select(attempt => GetMetadataProvider(attempt.SourceName))
            .ToArray();

        await RunBusyAsync(
            LocalizationService.Get("SearchFeedback.Retrying", failedAttempts.Length),
            async () =>
            {
                job.BeginSearch(preserveAttempts: true);
                MultiSourceSearchResult retryResult;
                try
                {
                    retryResult = await MetadataSearchCoordinator.RetryFailedAsync(
                        job.Metadata.Id,
                        previousAttempts,
                        failedProviders,
                        CurrentOperationToken);
                    var resolvedArtworkBundleSource = await ResolveBestArtworkSourceAsync(
                        sourceProfile,
                        retryResult.Sources,
                        CurrentOperationToken);
                    CurrentOperationToken.ThrowIfCancellationRequested();
                    job.ApplyRetriedOnlineSources(
                        retryResult.Metadata,
                        retryResult.Sources,
                        retryResult.Attempts,
                        sourceProfile,
                        resolvedArtworkBundleSource);
                }
                catch (MultiSourceSearchException exception)
                {
                    job.MarkSearchFailed(exception.Attempts, exception);
                    SetStatus(LocalizationService.Get("SearchFeedback.AllFailed"), false);
                    return;
                }
                catch (OperationCanceledException) when (CurrentOperationToken.IsCancellationRequested)
                {
                    job.MarkSearchCanceled();
                    throw;
                }
                catch (Exception exception)
                {
                    job.MarkSearchFailed(GetSearchAttempts(exception), exception);
                    throw;
                }

                _coverResolutionMonitor.Track(job);
                RefreshActiveJobPresentation();
                await LoadSelectedArtworkPreviewAsync();
                CacheActivePreview();
                var recovered = retryResult.Attempts.Count(attempt =>
                    failedAttempts.Any(previous =>
                        string.Equals(previous.SourceName, attempt.SourceName, StringComparison.OrdinalIgnoreCase)) &&
                    attempt.Success);
                SetStatus(
                    LocalizationService.Get("SearchFeedback.RetryComplete", recovered, failedAttempts.Length),
                    recovered == failedAttempts.Length);
            });
    }

    private async Task RetryQueueFailedSourcesAsync()
    {
        var sourceMode = GetSelectedSourceMode();
        var sourceProfile = CaptureSearchSourceProfile(sourceMode);
        var jobs = _movieQueue.Where(job => job.IsSelectedForBatch && CanRetrySources(job)).ToArray();
        if (jobs.Length == 0)
        {
            return;
        }
        await RunBusyAsync(LocalizationService.Get("SearchFeedback.RetryingQueue", jobs.Length), async () =>
        {
            var result = await BatchMetadataSearchCoordinator.SearchAsync(jobs, _libreDmmClient, _r18DevClient,
                CurrentOperationToken,
                sourceProfile: sourceProfile,
                bestArtworkSourceResolver: ResolveBestArtworkSourceAsync,
                retryFailedSourcesOnly: true,
                sourceMode: sourceMode,
                progress: BeginSearchProgress());
            InvalidateSucceededBatchSearchPreviews(result);
            RefreshActiveJobPresentation();
            if (jobs.Contains(_activeJob) && !CurrentOperationToken.IsCancellationRequested)
            {
                await LoadSelectedArtworkPreviewAsync();
                CacheActivePreview();
            }
            ShowBatchSearchSummary(result, "retry");
        });
    }

    private void R18CoolingDown(TimeSpan wait)
    {
        var operation = _activeOperationCancellation;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_busy && operation is not null && ReferenceEquals(operation, _activeOperationCancellation) &&
                !operation.IsCancellationRequested)
            {
                SetStatus(LocalizationService.Get("SearchFeedback.R18CoolingDown", Math.Ceiling(wait.TotalSeconds)), null);
            }
        });
    }

    private IMetadataProvider GetMetadataProvider(string sourceName) =>
        sourceName.ToLowerInvariant() switch
        {
            MetadataSourcePreferenceProfile.LibreDmm => _libreDmmClient,
            MetadataSourcePreferenceProfile.R18Dev => _r18DevClient,
            _ => throw new InvalidOperationException(
                LocalizationService.Get("SearchFeedback.UnknownSource", sourceName))
        };

    private bool IsRetryableMetadataSource(string sourceName) =>
        MetadataSearchSourceModes.AutomaticSources(GetSelectedSourceMode())
            .Contains(sourceName, StringComparer.OrdinalIgnoreCase);

    private MetadataSourcePreferenceProfile? CaptureSearchSourceProfile(string sourceMode) =>
        sourceMode == MetadataSearchSourceModes.Custom
            ? MetadataSourcePreferenceProfile.Normalize(_customSourceProfile) : null;

    private string GetAutomaticSourceDisplayName(string sourceMode) =>
        string.Join(" + ", MetadataSearchSourceModes.AutomaticSources(sourceMode)
            .Select(name => GetMetadataProvider(name).DisplayName));

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_activeJob.VideoPath is null)
        {
            ShowError(LocalizationService.Get("Error.SelectVideo"));
            return;
        }

        if (_activeJob.LocalNfoSaveBlocked)
        {
            ShowError(LocalizationService.Get("Error.LocalNfoBlocked"));
            return;
        }

        SyncActiveSaveConfiguration();
        _activeJob.InitializeSaveConfiguration(CaptureCurrentSaveConfiguration());
        var saveConfiguration = _activeJob.SaveConfiguration!;
        var options = saveConfiguration.SaveOptions;
        var organizationOptions = saveConfiguration.OrganizationOptions;

        SavePlan plan;
        try
        {
            plan = FileOrganizationService.BuildPlan(
                _activeJob.VideoPaths,
                _activeJob.Metadata,
                options,
                organizationOptions,
                _activeJob.CreateLocalSaveContext());
        }
        catch (Exception exception)
        {
            AppLog.Error("无法生成保存预览", exception);
            ShowError(exception.Message);
            return;
        }

        var allowOverwrite = false;
        if (ShouldShowSavePreview(SkipSavePreviewCheckBox.IsChecked == true, plan))
        {
            var preview = new SavePreviewWindow(plan) { Owner = this };
            var returnFocus = Keyboard.FocusedElement;
            var previewAccepted = preview.ShowDialog() == true;
            RestoreKeyboardFocus(returnFocus);
            if (!previewAccepted)
            {
                AppLog.Info("用户在预览阶段取消保存，未更改文件");
                SetStatus(LocalizationService.Get("Status.CanceledNoChanges"), false);
                return;
            }

            allowOverwrite = preview.AllowOverwrite;
        }
        else
        {
            if (plan.HasBlockingConflicts)
            {
                AppLog.Warning("直接保存被影片目标冲突阻止");
                ShowError(string.Join(Environment.NewLine, plan.BlockingConflicts));
                return;
            }

            AppLog.Info("用户选择跳过单片保存预览；当前计划无冲突");
        }

        await RunBusyAsync(LocalizationService.Get("Status.Saving"), async () =>
        {
            _savingAcrossVolumes = plan.RequiresVerifiedVideoCopy;
            var progress = new SaveProgress(this, [plan]);
            var result = await progress.ExecuteAsync(plan, _activeJob.Metadata, allowOverwrite, CurrentOperationToken);
            CancelOperationButton.IsEnabled = false;
            if (result.CleanupIssues.Count > 0)
                ShowCleanupDetails(result.CleanupIssues, committed: true);
            _activeJob.UpdateVideoPaths(result.VideoPaths);
            var outputs = new[] { result.Outputs.NfoPath, result.Outputs.PosterPath, result.Outputs.FanartPath }
                .Where(path => path is not null)
                .Select(Path.GetFileName)
                .ToList();
            if (result.Outputs.ExtrafanartPaths.Count > 0)
            {
                outputs.Add(LocalizationService.Get("Status.ExtrafanartCount", result.Outputs.ExtrafanartPaths.Count));
            }
            var fanartNote = result.Outputs.FanartPath is null
                ? string.Empty
                : result.Outputs.FanartUsedFullCover ? LocalizationService.Get("Status.FanartFromCover") : string.Empty;
            var moveNote = result.VideoMoved
                ? LocalizationService.Get("Status.VideoOrganized", Path.GetFileName(result.VideoPath))
                : string.Empty;
            try { await SelectVideoCoreAsync(result.VideoPath); }
            catch (OperationCanceledException) when (CurrentOperationToken.IsCancellationRequested)
            {
                // Saving has already committed; canceled preview loading is not a canceled save.
            }
            _activeJob.MarkSaveCompleted();
            RemoveCompletedActiveJobFromQueue();
            var outputSummary = outputs.Count == 0
                ? plan.HasActualChanges
                    ? LocalizationService.Get("Status.SidecarsMigrated")
                    : LocalizationService.Get("Status.NoChanges")
                : string.Join(LocalizationService.Get("Common.ListSeparator"), outputs);
            if (plan.OutputGenerationOptions.DownloadExtrafanart && result.Outputs.ExtrafanartPaths.Count == 0)
            {
                outputSummary += LocalizationService.Get("Common.ListSeparator") +
                                 LocalizationService.Get("Status.ExtrafanartSkipped");
            }
            var details = LocalizationService.Get("Status.SaveComplete", outputSummary, fanartNote, moveNote);
            if (result.CleanupIssues.Count == 0 && !plan.HasActualChanges && outputs.Count == 0)
                SetStatus(LocalizationService.Get("Status.NoChanges"), true, details);
            else
                SetSaveResultStatus(1, details, result.CleanupIssues.Count > 0, canceled: false);
        });
    }

    private void SourceItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, ManualSearchItem)) ShowManualWebsites();
        else ManualWebsiteChoices.Visibility = Visibility.Collapsed;
    }

    private void ManualSearch_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ManualWebsiteChoices.IsMouseOver || (e.OriginalSource is Visual hit && ManualWebsiteChoices.IsAncestorOf(hit))) return;
        e.Handled = true;
        ShowManualWebsites();
    }

    private void ManualSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (ManualWebsiteChoices.IsKeyboardFocusWithin || e.Key is not (Key.Right or Key.Enter or Key.Space)) return;
        e.Handled = true;
        ShowManualWebsites(focusFirst: true);
    }

    private void ShowManualWebsites(bool focusFirst = false)
    {
        if (_busy || !SourceComboBox.IsDropDownOpen || !ManualSearchItem.IsEnabled) return;
        ManualWebsiteChoices.Visibility = Visibility.Visible;
        if (focusFirst) ((Button)ManualWebsiteChoices.Children[0]).Focus();
    }

    private void ManualWebsites_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Escape)
        {
            e.Handled = true;
            ManualWebsiteChoices.Visibility = Visibility.Collapsed;
            ManualSearchItem.Focus();
        }
    }

    private void SourceDropDown_Closed(object? sender, EventArgs e) => ManualWebsiteChoices.Visibility = Visibility.Collapsed;

    private void SourceSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized || _restoringSourceSelection) return;
        if (ReferenceEquals(SourceComboBox.SelectedItem, ManualSearchItem))
        {
            // Manual lookup is a one-time command. Keep the automatic provider for subsequent queries.
            var openChooser = SourceComboBox.IsDropDownOpen && ManualSearchItem.IsEnabled;
            _restoringSourceSelection = true;
            try
            {
                SourceComboBox.SelectedItem = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault() ?? SourceComboBox.Items[0];
            }
            finally { _restoringSourceSelection = false; }

            if (openChooser)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    if (!IsVisible || _closeRequested || _busy || !ManualSearchItem.IsEnabled) return;
                    SourceComboBox.IsDropDownOpen = true;
                    ShowManualWebsites(focusFirst: true);
                });
            }
            return;
        }

        RefreshSourceSelectionUi();
    }

    private void RefreshSourceSelectionUi()
    {
        if (!_uiInitialized || SourceProfileButton is null || SourceProfileDivider is null ||
            SourceComboBox is null)
        {
            return;
        }

        var source = GetSelectedSourceMode();
        var profileVisibility = source == MetadataSearchSourceModes.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        SourceProfileButton.Visibility = profileVisibility;
        SourceProfileDivider.Visibility = profileVisibility;
        var summary = source switch
        {
            MetadataSearchSourceModes.LibreDmm => LocalizationService.Get("Main.Source.LibreDmmTooltip"),
            MetadataSearchSourceModes.R18Dev => LocalizationService.Get("Main.Source.R18Tooltip"),
            MetadataSearchSourceModes.Custom => LocalizationService.Get(
                "Main.Source.Summary.Custom",
                GetSourceProfileDisplayName(_customSourceProfile.TitleSource),
                GetSourceProfileDisplayName(_customSourceProfile.ArtworkSource)),
            MetadataSearchSourceModes.Manual => LocalizationService.Get("Main.Source.Summary.Manual"),
            _ => LocalizationService.Get(
                "Main.Source.Summary.Provider",
                (SourceComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? source)
        };
        SourceComboBox.ToolTip = summary;
        RefreshSearchToolbarLayout();
        RefreshQueueUi();
    }

    private static string GetSourceProfileDisplayName(string sourceName) => sourceName switch
    {
        MetadataSourcePreferenceProfile.LibreDmm => "LibreDMM",
        MetadataSourcePreferenceProfile.R18Dev => "R18.dev",
        MetadataSourcePreferenceProfile.BestArtworkResolution =>
            LocalizationService.Get("SourceProfile.BestArtworkResolution"),
        _ => sourceName
    };

    private void SearchToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshSearchToolbarLayout();
        }
    }

    private void RefreshSearchToolbarLayout()
    {
        if (!_uiInitialized || SearchToolbarLayout is null || SearchToolbar.ActualWidth <= 0)
        {
            return;
        }

        var width = SearchToolbarLayout.ActualWidth;
        if (width <= 0) return;
        var label = GetSelectedSourceMode() == MetadataSearchSourceModes.LibreDmm
            ? "LibreDMM" : (SourceComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        if (!Equals(SourceComboBox.Resources["SearchSourceSelectionLabel"], label))
            SourceComboBox.Resources["SearchSourceSelectionLabel"] = label;

        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        MovieIdTextBlock.Measure(unconstrained);
        AutomaticSourceText.Measure(unconstrained);
        SearchButton.Measure(unconstrained);
        SourceSelectorHost.Measure(unconstrained);
        // The single row includes a 100-DIP ID field and four gaps (8, 6, 12, 8).
        var singleRowWidth = 134 + MovieIdTextBlock.DesiredSize.Width + AutomaticSourceText.DesiredSize.Width +
            SearchButton.DesiredSize.Width + SourceSelectorHost.DesiredSize.Width;
        var wrapped = width < Math.Ceiling(singleRowWidth);
        if (_searchToolbarWrapped == wrapped) return;
        _searchToolbarWrapped = wrapped;
        var columns = SearchToolbarLayout.ColumnDefinitions;
        var star = new GridLength(1, GridUnitType.Star);
        columns[2].Width = wrapped ? star : new GridLength(100);
        columns[5].Width = new GridLength(wrapped ? 0 : 12);
        columns[7].Width = new GridLength(wrapped ? 0 : 8);
        columns[8].Width = wrapped ? new GridLength(0) : GridLength.Auto;
        SearchToolbarLayout.RowDefinitions[1].Height = new GridLength(wrapped ? 8 : 0);
        IdTextBox.Width = wrapped ? double.NaN : 100;
        Grid.SetRow(AutomaticSourceText, wrapped ? 2 : 0);
        Grid.SetColumn(AutomaticSourceText, wrapped ? 0 : 6);
        Grid.SetRow(SourceSelectorHost, wrapped ? 2 : 0);
        Grid.SetColumn(SourceSelectorHost, wrapped ? 2 : 8);
        Grid.SetColumnSpan(SourceSelectorHost, wrapped ? 3 : 1);
    }

    private void SourceProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SourceProfileWindow(_customSourceProfile) { Owner = this };
        var returnFocus = Keyboard.FocusedElement;
        var profileAccepted = dialog.ShowDialog() == true;
        RestoreKeyboardFocus(returnFocus);
        if (!profileAccepted)
        {
            return;
        }

        _customSourceProfile = MetadataSourcePreferenceProfile.Normalize(dialog.Profile);
        AppLog.Info(
            $"已更新自定义多来源规则 title={_customSourceProfile.TitleSource} " +
            $"default={_customSourceProfile.OriginalTitleSource}");
        RefreshSourceSelectionUi();
        SetStatus(LocalizationService.Get("Status.SourceProfileUpdated"), true);
    }

    private async void SaveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedJobs = _movieQueue.Where(job => job.IsSelectedForBatch).ToArray();
        if (selectedJobs.Length == 0)
        {
            SetStatus(LocalizationService.Get("Status.NoBatchSelection"), false);
            return;
        }

        if (selectedJobs.Contains(_activeJob))
        {
            SyncActiveSaveConfiguration();
            _activeJob.InitializeSaveConfiguration(CaptureCurrentSaveConfiguration());
        }

        var previewItems = new List<BatchSavePreviewItem>();
        var previewIssues = new List<BatchSavePreviewIssue>();
        foreach (var job in selectedJobs)
        {
            if (job.VideoPath is null)
            {
                previewIssues.Add(new BatchSavePreviewIssue(
                    job,
                    LocalizationService.Get("Error.SelectVideo")));
                continue;
            }

            if (job.LocalNfoSaveBlocked)
            {
                previewIssues.Add(new BatchSavePreviewIssue(
                    job,
                    LocalizationService.Get("Error.LocalNfoBlocked")));
                continue;
            }

            if (job.SaveConfiguration is null)
            {
                previewIssues.Add(new BatchSavePreviewIssue(
                    job,
                    LocalizationService.Get("Error.MissingJobSaveConfiguration")));
                continue;
            }

            try
            {
                var configuration = job.SaveConfiguration;
                var plan = FileOrganizationService.BuildPlan(
                    job.VideoPaths,
                    job.Metadata,
                    configuration.SaveOptions,
                    configuration.OrganizationOptions,
                    job.CreateLocalSaveContext());
                if (plan.HasBlockingConflicts)
                {
                    previewIssues.Add(new BatchSavePreviewIssue(
                        job,
                        string.Join(Environment.NewLine, plan.BlockingConflicts)));
                    continue;
                }

                previewItems.Add(new BatchSavePreviewItem(job, plan, job.ReviewRevision));
            }
            catch (Exception exception)
            {
                AppLog.Warning($"批量保存预检失败 id={job.Metadata.Id} path={job.VideoPath}", exception);
                previewIssues.Add(new BatchSavePreviewIssue(job, exception.Message));
            }
        }

        var originalPreviewItems = previewItems.ToArray();
        var combinedGroups = new Dictionary<MovieJob, IReadOnlyList<BatchSavePreviewItem>>();
        var duplicateGroups = DuplicateMovieResolution.Groups(previewItems);
        foreach (var group in duplicateGroups)
        {
            var chooser = new DuplicateMovieWindow(group) { Owner = this };
            var returnFocus = Keyboard.FocusedElement;
            var accepted = chooser.ShowDialog() == true;
            RestoreKeyboardFocus(returnFocus);
            if (!accepted)
            {
                SetStatus(LocalizationService.Get("Status.CanceledNoChanges"), false);
                return;
            }
            foreach (var item in group) previewItems.Remove(item);
            if (!chooser.AsParts) previewItems.AddRange(chooser.ChosenItems);
            else
            {
                try
                {
                    var combined = DuplicateMovieResolution.Combine(chooser.ChosenItems);
                    if (combined.Plan.HasBlockingConflicts)
                        throw new IOException(string.Join(Environment.NewLine, combined.Plan.BlockingConflicts));
                    previewItems.Add(combined);
                    combinedGroups[combined.Job] = chooser.ChosenItems;
                }
                catch (Exception exception)
                {
                    foreach (var item in group) previewIssues.Add(new(item.Job, exception.Message));
                }
            }
        }
        var participatingJobs = previewItems.Select(item => item.Job)
            .Concat(combinedGroups.Values.SelectMany(group => group).Select(item => item.Job)).ToHashSet();
        var protectedItems = originalPreviewItems.Where(item => !participatingJobs.Contains(item.Job)).ToArray();

        // Individually safe plans may still overwrite or retire another movie's files.
        // Remove every affected plan before the user can accept or skip the preview.
        var batchConflicts = BatchSavePathConflicts.Find(previewItems, protectedItems);
        foreach (var conflict in batchConflicts)
        {
            previewIssues.Add(new BatchSavePreviewIssue(conflict.Key,
                LocalizationService.Get("Error.BatchOutputConflict", string.Join(Environment.NewLine, conflict.Value))));
        }
        previewItems.RemoveAll(item => batchConflicts.ContainsKey(item.Job));

        if (previewItems.Count == 0 && previewIssues.Count == 0)
        {
            SetStatus(LocalizationService.Get("Status.CanceledNoChanges"), false);
            return;
        }

        var previewAllowOverwrite = false;
        IReadOnlyList<BatchSavePreviewItem> itemsToSave = previewItems;
        if (ShouldShowBatchSavePreview(
                duplicateGroups.Count == 0 && SkipSavePreviewCheckBox.IsChecked == true,
                previewItems,
                previewIssues))
        {
            var preview = new BatchSavePreviewWindow(previewItems, previewIssues) { Owner = this };
            var returnFocus = Keyboard.FocusedElement;
            var previewAccepted = preview.ShowDialog() == true;
            RestoreKeyboardFocus(returnFocus);
            if (!previewAccepted)
            {
                AppLog.Info("用户取消批量保存预览，未更改文件");
                SetStatus(LocalizationService.Get("Status.CanceledNoChanges"), false);
                return;
            }

            itemsToSave = preview.Items;
            previewAllowOverwrite = preview.AllowOverwrite;
        }
        else
        {
            AppLog.Info($"用户选择跳过批量保存预览；当前批次无冲突 count={previewItems.Count}");
        }

        var preparedItems = itemsToSave.Select(item => new PreparedMovieSave(
            item.Job,
            item.Plan,
            item.ReviewRevision,
            item.Plan.SaveOptions.OverwriteExisting ||
            (previewAllowOverwrite && item.Plan.OverwriteConflicts.Count > 0))).ToArray();
        await RunBusyAsync(LocalizationService.Get("Status.BatchSaving", preparedItems.Length), async () =>
        {
            _savingAcrossVolumes = preparedItems.Any(item => item.Plan.RequiresVerifiedVideoCopy);
            var progress = new SaveProgress(this, preparedItems.Select(item => item.Plan).ToArray());
            var result = await BatchSaveCoordinator.ExecuteAsync(
                preparedItems,
                async (item, cancellationToken) =>
                {
                    if (combinedGroups.TryGetValue(item.Job, out var parts))
                    {
                        foreach (var part in parts.Where(part => part.Job != item.Job))
                            part.Job.BeginSave(part.ReviewRevision);
                    }
                    return await progress.ExecuteAsync(item.Plan, item.Job.Metadata, item.AllowOverwrite, cancellationToken, batch: true);
                },
                CurrentOperationToken,
                async item =>
                {
                    var related = combinedGroups.TryGetValue(item.Item.Job, out var parts)
                        ? parts.Select(part => part.Job).ToArray() : [item.Item.Job];
                    foreach (var job in related.Where(job => job != item.Item.Job))
                    {
                        if (item.Status is BatchSaveItemStatus.Completed) job.MarkSaveCompleted();
                        else if (item.Status is BatchSaveItemStatus.Canceled) job.MarkSaveCanceled();
                        else if (item.Error is not null) job.MarkSaveFailed(item.Error, true);
                    }
                    if (item.Status is BatchSaveItemStatus.Completed)
                    {
                        await RemoveCompletedQueueJobsAsync(related);
                    }
                });

            // The batch coordinator reports recovery failures as results, not exceptions.
            // Keep the window open even if a close request canceled the batch.
            var cleanupIssues = result.Items.SelectMany(item => item.SaveResult?.CleanupIssues ?? []).ToArray();
            if (result.FailedCount > 0 || cleanupIssues.Length > 0)
            {
                _closeRequested = false;
            }
            var firstFailure = result.Items.FirstOrDefault(item => item.Error is not null)?.Error;
            if (firstFailure is not null) throw firstFailure;
            if (cleanupIssues.Length > 0) ShowCleanupDetails(cleanupIssues, committed: true);

            if (_activeJob.VideoPath is not null)
            {
                FileNameText.Text = _activeJob.FileName;
                FilePathText.Text = _activeJob.VideoPath;
                if (!RestoreCachedPreview(_activeJob))
                {
                    ClearPosterPreview();
                    ClearFanartPreview();
                    if (!CurrentOperationToken.IsCancellationRequested)
                    {
                        try { await LoadSelectedArtworkPreviewAsync(); }
                        catch (OperationCanceledException) when (CurrentOperationToken.IsCancellationRequested) { }
                    }
                    CacheActivePreview();
                }

                RefreshTargetLocationPreview();
            }

            AppLog.Info(
                $"批量保存结束 requested={preparedItems.Length} completed={result.CompletedCount} " +
                $"failed={result.FailedCount} canceled={result.CanceledCount} notStarted={result.NotStartedCount}");
            var details = LocalizationService.Get("Status.BatchSaveComplete", result.CompletedCount,
                result.FailedCount, result.CanceledCount, result.NotStartedCount);
            SetSaveResultStatus(result.CompletedCount, details, cleanupIssues.Length > 0,
                result.CanceledCount > 0 || (result.NotStartedCount > 0 && CurrentOperationToken.IsCancellationRequested));
        });
    }

    private static bool ShouldShowSavePreview(bool skipRequested, SavePlan plan) =>
        !skipRequested ||
        plan.HasBlockingConflicts ||
        plan.OverwriteConflicts.Count > 0;

    private static bool ShouldShowBatchSavePreview(
        bool skipRequested,
        IReadOnlyList<BatchSavePreviewItem> items,
        IReadOnlyList<BatchSavePreviewIssue> issues) =>
        !skipRequested ||
        items.Count == 0 ||
        issues.Count > 0 ||
        items.Any(item =>
            item.Plan.HasBlockingConflicts ||
            item.Plan.OverwriteConflicts.Count > 0);

    private void RemoveCompletedActiveJobFromQueue()
    {
        if (!_movieQueue.Remove(_activeJob))
        {
            return;
        }

        AppLog.Info($"单片保存完成，已从批量队列移除 path={_activeJob.VideoPath}");
        RefreshQueueUi();
    }

    private Task SelectVideoAsync(string path) =>
        RunBusyAsync(LocalizationService.Get("Status.InspectingLocal"), () => SelectVideoCoreAsync(path));

    private async Task AddMovieFilesAsync(IEnumerable<string> paths)
    {
        var focusIdAfterImport = false;
        await RunBusyAsync(LocalizationService.Get("Status.InspectingLocal"), async () =>
        {
            var plan = await MovieQueueImport.PrepareAsync(paths.ToArray(),
                _movieQueue.SelectMany(job => job.VideoPaths).ToArray(), CurrentOperationToken);
            var requestedSets = plan.FileSets;
            var duplicateCount = plan.DuplicatePathCount;
            var groups = _movieQueue.ToDictionary(job => job.MovieFileGroupKey, StringComparer.OrdinalIgnoreCase);
            MovieJob? firstAdded = null;
            var failedCount = 0;
            var addedJobCount = 0;
            foreach (var batch in requestedSets.Chunk(64))
            {
                CurrentOperationToken.ThrowIfCancellationRequested();
                var newSets = batch.Where(set => !groups.ContainsKey(set.GroupKey)).ToArray();
                IReadOnlyList<MovieQueueLoadItem> loaded = newSets.Length == 0
                    ? []
                    : await MovieQueueImport.LoadBatchAsync(newSets, CurrentOperationToken);
                var results = loaded.ToDictionary(item => item.FileSet.GroupKey, StringComparer.OrdinalIgnoreCase);
                foreach (var fileSet in batch)
                {
                    if (groups.TryGetValue(fileSet.GroupKey, out var existingJob))
                    {
                        if (fileSet.VideoPaths.Any(path => !existingJob.VideoPaths.Contains(path, StringComparer.OrdinalIgnoreCase)))
                            existingJob.AddVideoPaths(fileSet.VideoPaths);
                        firstAdded ??= existingJob;
                        continue;
                    }
                    var item = results[fileSet.GroupKey];
                    if (item.Error is not null)
                    {
                        failedCount++;
                        AppLog.Warning($"影片加入队列失败 path={fileSet.PrimaryPath}", item.Error);
                        continue;
                    }
                    var job = item.Job!;
                    job.InitializeSaveConfiguration(CaptureCurrentSaveConfiguration());
                    AttachMovieJob(job);
                    _movieQueue.Add(job);
                    groups.Add(fileSet.GroupKey, job);
                    _jobLoadResults[job] = item.Result!;
                    firstAdded ??= job;
                    addedJobCount++;
                    LogMovieJobLoad(job, item.Result!);
                }
            }

            if (requestedSets.Count - failedCount > 1) _showImportedBatch = true;
            RefreshQueueUi();
            if (firstAdded is not null)
            {
                await ActivateMovieJobAsync(firstAdded);
                focusIdAfterImport = _movieQueue.Count == 1 &&
                                     requestedSets.Count == 1 &&
                                     string.IsNullOrWhiteSpace(firstAdded.Metadata.Id);
            }

            SetStatus(
                LocalizationService.Get("Status.AddedMovies", addedJobCount, duplicateCount),
                failedCount == 0);
        });

        if (focusIdAfterImport)
        {
            RefreshWorkspaceDetails();
            IdTextBox.Focus();
            IdTextBox.SelectAll();
        }
    }

    private async void MovieQueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingQueueSelection || _busy || MovieQueueList.SelectedItem is not MovieJob selectedJob ||
            ReferenceEquals(selectedJob, _activeJob))
        {
            return;
        }

        var restoreQueueFocus = MovieQueueList.IsKeyboardFocusWithin ||
            _queueNavigationKey is not null || _pendingQueueFocusVersion == _queueFocusVersion;
        var focusVersion = _queueFocusVersion;
        if (restoreQueueFocus)
        {
            _pendingQueueFocusVersion = focusVersion;
        }
        _navigationPreviewCancellation?.Cancel();
        using var navigation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _navigationPreviewCancellation = navigation;
        try
        {
            if (restoreQueueFocus) RestoreQueueFocus(selectedJob, focusVersion);
            await ActivateMovieJobCoreAsync(selectedJob, true, navigation.Token);
        }
        catch (OperationCanceledException) when (navigation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Warning("Queue preview failed.", exception);
            if (!navigation.IsCancellationRequested) SetStatus(exception.Message, false);
        }
        finally
        {
            if (ReferenceEquals(_navigationPreviewCancellation, navigation)) _navigationPreviewCancellation = null;
        }
    }

    private void NavigateQueue(Key key)
    {
        if (_busy || MovieQueueList.Items.Count == 0)
        {
            return;
        }

        var currentIndex = MovieQueueList.SelectedIndex;
        var nextIndex = key == Key.Up
            ? Math.Max(0, currentIndex - 1)
            : Math.Min(MovieQueueList.Items.Count - 1, currentIndex + 1);
        if (nextIndex == currentIndex)
        {
            return;
        }

        MovieQueueList.SelectedIndex = nextIndex;
        MovieQueueList.ScrollIntoView(MovieQueueList.SelectedItem);
    }

    private void RestoreQueueFocus(MovieJob selectedJob, int focusVersion)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (focusVersion != _queueFocusVersion || _pendingQueueFocusVersion != focusVersion ||
                _lifetimeCancellation.IsCancellationRequested ||
                !QueueIsVisible || _busy ||
                !ReferenceEquals(MovieQueueList.SelectedItem, selectedJob))
            {
                return;
            }

            MovieQueueList.UpdateLayout();
            _pendingQueueFocusVersion = null;
            if (MovieQueueList.ItemContainerGenerator.ContainerFromItem(selectedJob) is ListBoxItem item)
            {
                item.Focus();
                Keyboard.Focus(item);
            }
            else
            {
                MovieQueueList.Focus();
            }
        });
    }

    private void SelectAllQueue_Click(object sender, RoutedEventArgs e) =>
        SetBatchSelection(true);

    private void SelectNoneQueue_Click(object sender, RoutedEventArgs e) =>
        SetBatchSelection(false);

    private void SetBatchSelection(bool isSelected) => SelectMatchingJobs(_ => isSelected);

    private void SelectRecognizedQueue_Click(object sender, RoutedEventArgs e) =>
        SelectMatchingJobs(job => !string.IsNullOrWhiteSpace(job.Metadata.Id));

    private void SelectPendingQueue_Click(object sender, RoutedEventArgs e) =>
        SelectMatchingJobs(job => !HasQueueIssue(job));

    private void SelectIssuesQueue_Click(object sender, RoutedEventArgs e) =>
        SelectMatchingJobs(HasQueueIssue);

    private void SelectMatchingJobs(Func<MovieJob, bool> include)
    {
        if (_busy) return;
        _rangeAnchor = null;
        foreach (var job in _movieQueue)
            job.IsSelectedForBatch = include(job);
        _queueRefresh.Flush();
    }

    private async void RemoveSelectedQueue_Click(object sender, RoutedEventArgs e)
    {
        var selectedJobs = _movieQueue.Where(job => job.IsSelectedForBatch).ToArray();
        await RemoveQueueJobsAsync(selectedJobs);
    }

    private async void ClearQueue_Click(object sender, RoutedEventArgs e) =>
        await RemoveQueueJobsAsync(_movieQueue.ToArray());

    private async void RemoveCurrentVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _activeJob.VideoPath is null)
        {
            return;
        }

        var removedJob = _activeJob;
        await RunBusyAsync(
            LocalizationService.Get("Status.InspectingLocal"),
            () => RemoveQueueJobsCoreAsync([removedJob]));
        SetStatus(LocalizationService.Get("Status.RemovedCurrentMovie"), true);
    }

    private async Task RemoveQueueJobsAsync(IReadOnlyCollection<MovieJob> jobs)
    {
        if (_busy)
        {
            return;
        }

        var targets = jobs.Where(_movieQueue.Contains).Distinct().ToArray();
        if (targets.Length == 0)
        {
            SetStatus(LocalizationService.Get("Status.NoBatchSelection"), false);
            return;
        }

        await RunBusyAsync(
            LocalizationService.Get("Status.InspectingLocal"),
            () => RemoveQueueJobsCoreAsync(targets));
        SetStatus(LocalizationService.Get("Status.RemovedQueueItems", targets.Length), true);
    }

    private async Task RemoveCompletedQueueJobsAsync(IReadOnlyCollection<MovieJob> jobs)
    {
        var completedJobs = jobs
            .Where(job => _movieQueue.Contains(job) && job.SaveState is MovieSaveState.Completed)
            .Distinct()
            .ToArray();
        if (completedJobs.Length == 0)
        {
            return;
        }

        await RemoveQueueJobsCoreAsync(completedJobs, loadReplacementPreview: false);
        AppLog.Info($"保存完成，已从队列移除 count={completedJobs.Length}");
    }

    private async Task RemoveQueueJobsCoreAsync(
        IReadOnlyCollection<MovieJob> targets,
        bool loadReplacementPreview = true)
    {
        var activeIndex = _movieQueue.IndexOf(_activeJob);
        var activeRemoved = targets.Contains(_activeJob);
        foreach (var job in targets)
        {
            _movieQueue.Remove(job);
            _jobLoadResults.Remove(job);
            _jobPreviews.Remove(job);
        }

        if (activeRemoved)
        {
            var replacement = _movieQueue.Count == 0
                ? new MovieJob()
                : _movieQueue[Math.Clamp(activeIndex, 0, _movieQueue.Count - 1)];
            AttachMovieJob(replacement);
            await ActivateMovieJobAsync(replacement, loadReplacementPreview);
        }

        foreach (var job in targets)
        {
            DetachMovieJob(job);
            job.Dispose();
        }

        RefreshQueueUi();
    }

    private Task ActivateMovieJobAsync(MovieJob job, bool loadArtworkPreview = true)
    {
        _navigationPreviewCancellation?.Cancel();
        return ActivateMovieJobCoreAsync(job, loadArtworkPreview, CurrentOperationToken);
    }

    private async Task ActivateMovieJobCoreAsync(MovieJob job, bool loadArtworkPreview, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(job);
        var statusRevision = _statusRevision;
        var previous = _activeJob;
        var switchedMovie = !ReferenceEquals(previous, job);
        if (switchedMovie)
        {
            CacheActivePreview();
            IsEditingMetadata = false;
            previous.MetadataPropertyChanged -= Metadata_PropertyChanged;
            previous.MetadataSelectionChanged -= MetadataReview_SelectionChanged;
            _activeJob = job;
            _activeJob.MetadataPropertyChanged += Metadata_PropertyChanged;
            _activeJob.MetadataSelectionChanged += MetadataReview_SelectionChanged;
        }

        _changingQueueSelection = true;
        try
        {
            MovieQueueList.SelectedItem = _movieQueue.Contains(job) ? job : null;
        }
        finally
        {
            _changingQueueSelection = false;
        }

        RefreshActiveJobPresentation();
        if (switchedMovie)
        {
            MetadataScrollViewer.ScrollToTop();
        }
        if (job.VideoPath is not null)
        {
            job.InitializeSaveConfiguration(CaptureCurrentSaveConfiguration());
            ApplySaveConfiguration(job.SaveConfiguration!);
        }
        if (!ReferenceEquals(previous, job) && !_movieQueue.Contains(previous))
        {
            _jobLoadResults.Remove(previous);
            _jobPreviews.Remove(previous);
            DetachMovieJob(previous);
            previous.Dispose();
        }

        _queueRefresh.Flush();
        if (job.VideoPath is null)
        {
            _artworkPreviewDeferred = false;
            FileNameText.Text = LocalizationService.Get("Main.NoVideo");
            FilePathText.Text = LocalizationService.Get("Main.VideoStaysHere");
            ClearPosterPreview();
            ClearFanartPreview();
            SetStatus(LocalizationService.Get("Desk.Ready"), true);
        }
        else
        {
            FileNameText.Text = job.FileName;
            FilePathText.Text = job.VideoPath;
            if (!RestoreCachedPreview(job))
            {
                _artworkPreviewDeferred = !loadArtworkPreview;
                ClearPosterPreview();
                ClearFanartPreview();
                if (loadArtworkPreview)
                {
                    await LoadArtworkPreviewAsync(token);
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(job, _activeJob)) return;
                    CacheActivePreview();
                }
            }

            if (statusRevision == _statusRevision && _jobLoadResults.TryGetValue(job, out var loadResult))
            {
                PresentMovieJobLoadStatus(job, loadResult);
            }
        }
    }

    private void RefreshQueueUi()
    {
        if (_uiInitialized) _queueRefresh.Flush(refreshView: true);
    }

    private void RefreshQueueUiCore(bool refreshView)
    {
        if (!_uiInitialized)
        {
            return;
        }

        if (_movieQueue.Count < 2) _showImportedBatch = false;
        var showQueue = QueueIsVisible;
        UpdateQueueLayout();
        if (refreshView) _movieQueueView.Refresh();
        QueuePanel.Visibility = showQueue ? Visibility.Visible : Visibility.Collapsed;
        var sourceMode = GetSelectedSourceMode();
        var canSearch = MetadataSearchSourceModes.AutomaticSources(sourceMode).Count > 0;
        var usesR18 = MetadataSearchSourceModes.AutomaticSources(sourceMode)
            .Contains(MetadataSearchSourceModes.R18Dev);
        var selectedCount = 0;
        var issueCount = 0;
        var searchableCount = 0;
        foreach (var job in _movieQueue)
        {
            if (job.IsSelectedForBatch) selectedCount++;
            if (HasQueueIssue(job)) issueCount++;
            if (job.CanBatchSearch) searchableCount++;
        }
        var singleCanSearch = (_movieQueue.Count == 0 || _activeJob.IsSelectedForBatch) && !string.IsNullOrWhiteSpace(_activeJob.Metadata.Id);
        SearchQueueButton.IsEnabled = !_busy && canSearch && (UseBatchActions ? searchableCount > 0 : singleCanSearch);
        SearchButton.IsEnabled = !_busy && canSearch && !string.IsNullOrWhiteSpace(_activeJob.Metadata.Id);
        ManualSearchItem.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(MovieIdParser.Normalize(_activeJob.Metadata.Id));
        if (_movieQueue.Count == 0)
            SearchQueueButton.Content = LocalizationService.Get("Main.Search");
        else if (UseBatchActions && selectedCount > 0 && searchableCount == 0)
            SearchQueueButton.Content = LocalizationService.Get("Desk.NoPendingLookup");
        else
            SearchQueueButton.Content = LocalizationService.Get(UseBatchActions ? "Desk.SearchCheckedCount" : "Desk.SearchCount",
                UseBatchActions ? searchableCount : singleCanSearch ? 1 : 0);
        QueueCheckAll.IsChecked = selectedCount == 0 ? false : selectedCount == _movieQueue.Count ? true : null;
        QueueCheckAll.IsEnabled = !_busy && _movieQueue.Count > 0;
        SaveSelectedButton.Visibility = UseBatchActions ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = UseBatchActions ? Visibility.Collapsed : Visibility.Visible;
        RefreshSaveAvailability();
        RefreshWorkspaceDetails();
        QueueFilterAllItem.DataContext = _movieQueue.Count;
        QueueFilterPendingItem.DataContext = _movieQueue.Count - issueCount;
        QueueFilterIssuesItem.DataContext = issueCount;
        SearchQueueButton.ToolTip = canSearch
            ? LocalizationService.Get("Main.SearchQueueSourceTooltip", GetAutomaticSourceDisplayName(sourceMode))
            : LocalizationService.Get("Status.ManualMode");
        if (UseBatchActions && canSearch)
            SearchQueueButton.ToolTip += Environment.NewLine + LocalizationService.Get(
                usesR18 ? "Main.BatchSource.R18Hint" : "Main.BatchSource.DmmHint");
        SaveSelectedButton.IsEnabled = !_busy && selectedCount > 0;
        SelectRecognizedQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0;
        SelectPendingQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0;
        SelectIssuesQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0;
        SelectNoneQueueButton.IsEnabled = !_busy && selectedCount > 0;
        RemoveSelectedQueueButton.IsEnabled = !_busy && selectedCount > 0;
        ClearQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0;
        var saveLabel = LocalizationService.Get(SkipSavePreviewCheckBox.IsChecked == true ? "Desk.SaveCount" : "Desk.PreviewCount", selectedCount);
        SaveSelectedButton.Content = saveLabel;
        SaveButton.Content = saveLabel;
        ApplySelectionSettingsButton.Visibility = UseBatchActions ? Visibility.Visible : Visibility.Collapsed;
        ApplySelectionSettingsButton.IsEnabled = !_busy && selectedCount > 0;
        ApplySelectionSettingsButton.Content = LocalizationService.Get("Desk.ApplySelected", selectedCount);
        SingleSavePanel.Visibility = selectedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueSplitter.Visibility = showQueue ? Visibility.Visible : Visibility.Collapsed;
        QueueCountText.Text = _movieQueue.Count.ToString();
        QueueCountText.ToolTip = LocalizationService.Get(
            "Main.QueueSummary",
            selectedCount,
            _movieQueue.Count,
            issueCount);
        RefreshRetryFailedSourcesUi();
        MinWidth = 930;
    }

    private async Task ImportDiscoveredPathsAsync(IReadOnlyList<string> paths)
    {
        MovieInputDiscoveryResult? result = null;
        await RunBusyAsync(LocalizationService.Get("Discovery.Scanning"), async () =>
        {
            result = await MovieInputDiscovery.DiscoverAsync(
                paths,
                includeSubdirectories: true,
                CurrentOperationToken);
        });
        if (result is null)
        {
            return;
        }

        if (result.MovieFileSets.Count == 0 && result.Diagnostics.Count == 0)
        {
            ShowError(LocalizationService.Get("Error.NoSupportedDroppedFiles"));
            return;
        }

        if (result.MovieFileSets.Count == 1 && result.Diagnostics.Count == 0)
        {
            await AddMovieFilesAsync(result.MovieFileSets[0].VideoPaths);
            return;
        }

        var discovery = new MovieDiscoveryWindow(
            paths,
            _movieQueue.SelectMany(job => job.VideoPaths),
            includeSubdirectories: true,
            initialResult: result)
        {
            Owner = this
        };
        // A long scan can leave another window active when the modal opens.
        var reviewWasActive = false;
        discovery.Closing += (_, _) => reviewWasActive = discovery.IsActive;
        var accepted = discovery.ShowDialog() == true;
        if (reviewWasActive && !IsActive) Activate();
        if (accepted)
        {
            await AddMovieFilesAsync(discovery.SelectedVideoPaths);
        }
    }

    private bool FilterQueueItem(object item)
    {
        if (item is not MovieJob job)
        {
            return false;
        }

        var filter = (QueueFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        return filter switch
        {
            "pending" => !HasQueueIssue(job),
            "issues" => HasQueueIssue(job),
            _ => true
        };
    }

    private static bool HasQueueIssue(MovieJob job) =>
        job.HasLowResolutionCover ||
        job.LocalNfoSaveBlocked ||
        job.HasFailedSources ||
        string.IsNullOrWhiteSpace(job.Metadata.Id) ||
        job.SearchState is MovieSearchState.SearchFailed ||
        job.SaveState is MovieSaveState.SaveFailed or MovieSaveState.Conflict;

    private void QueueFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshQueueUi();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && MovieQueueList.IsKeyboardFocusWithin && !_busy)
        {
            SelectAllQueue_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Up or Key.Down && Keyboard.Modifiers == ModifierKeys.None &&
            QueueIsVisible && !HasOpenTransientSurface())
        {
            if (MovieQueueList.IsKeyboardFocusWithin || _queueNavigationKey is not null ||
                _pendingQueueFocusVersion == _queueFocusVersion)
            {
                // Keep held arrows within the list until the user starts another gesture.
                _queueNavigationKey = e.Key;
                e.Handled = true;
                NavigateQueue(e.Key);
                return;
            }
        }
        else
        {
            // Tab, modifiers, menu commands, etc. express a new keyboard intent.
            ReleaseQueueNavigationFocus();
        }

        var shortcutButton = ResolveMainShortcut(e.Key, Keyboard.Modifiers);
        if (shortcutButton is null)
        {
            return;
        }

        e.Handled = true;
        shortcutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_queueNavigationKey == e.Key)
        {
            _queueNavigationKey = null;
            e.Handled = true;
            // A released key ends the held gesture, but does not cancel the pending return
            // to the queue. Until then a fresh arrow still belongs to that queue focus.
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        ReleaseQueueNavigationFocus();

    private void Window_Deactivated(object? sender, EventArgs e) =>
        ReleaseQueueNavigationFocus();

    private void ReleaseQueueNavigationFocus()
    {
        _queueNavigationKey = null;
        _pendingQueueFocusVersion = null;
        _queueFocusVersion++;
    }

    private Button? ResolveMainShortcut(Key key, ModifierKeys modifiers)
    {
        if (key is not Key.S || modifiers != ModifierKeys.Control || _busy || HasOpenTransientSurface())
        {
            return null;
        }

        if (UseBatchActions)
        {
            return SaveSelectedButton.Visibility == Visibility.Visible &&
                   SaveSelectedButton.IsEnabled &&
                   _movieQueue.Any(job => job.IsSelectedForBatch)
                ? SaveSelectedButton
                : null;
        }

        return SaveButton.Visibility == Visibility.Visible &&
               SaveButton.IsEnabled &&
               _activeJob.VideoPath is not null
            ? SaveButton
            : null;
    }

    private bool HasOpenTransientSurface() =>
        QueueOptions.IsOpen || AppearancePopup.IsOpen || AppearanceChoice.IsDropDownOpen ||
        LanguageComboBox.IsDropDownOpen ||
        QueueFilterComboBox.IsDropDownOpen ||
        SourceComboBox.IsDropDownOpen ||
        TargetModeComboBox.IsDropDownOpen ||
        RecentRootsButton.ContextMenu?.IsOpen == true ||
        ArtworkSourceButton.ContextMenu?.IsOpen == true ||
        GetSourceBadgeControls().Any(item => item.Badge.ContextMenu?.IsOpen == true);

    private void IdTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || Keyboard.Modifiers != ModifierKeys.None || _busy) return;
        if (GetSelectedSourceMode() == MetadataSearchSourceModes.Manual)
        {
            e.Handled = true;
            SetStatus(LocalizationService.Get("Status.ManualMode"), true);
            return;
        }
        if (!SearchButton.IsEnabled) return;

        e.Handled = true;
        SearchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void RestoreKeyboardFocus(IInputElement? target)
    {
        if (target is UIElement { IsEnabled: true, IsVisible: true } element)
        {
            Keyboard.Focus(element);
        }
    }

    private void AttachMovieJob(MovieJob job)
    {
        job.PropertyChanged -= MovieJob_PropertyChanged;
        job.PropertyChanged += MovieJob_PropertyChanged;
    }

    private void DetachMovieJob(MovieJob job)
    {
        job.PropertyChanged -= MovieJob_PropertyChanged;
        _coverResolutionMonitor.Forget(job);
    }

    private void MovieJob_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MovieJob.CanBatchSearch) or
            nameof(MovieJob.IsSelectedForBatch) or
            nameof(MovieJob.LastSearchAttempts) or
            nameof(MovieJob.SearchState) or
            nameof(MovieJob.LocalNfoSaveBlocked) or
            nameof(MovieJob.HasLowResolutionCover) or
            nameof(MovieJob.SaveState))
        {
            _queueRefresh.Request((QueueFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() is "pending" or "issues");
        }

        if (ReferenceEquals(sender, _activeJob) &&
            eventArgs.PropertyName is nameof(MovieJob.SearchState) or nameof(MovieJob.LastSearchAttempts))
        {
            RefreshRetryFailedSourcesUi();
        }

        if (sender is MovieJob coverJob && eventArgs.PropertyName is
            nameof(MovieJob.CoverResolutionRevision) or nameof(MovieJob.SearchState))
            _coverResolutionMonitor.Refresh(coverJob);
        if (ReferenceEquals(sender, _activeJob) && eventArgs.PropertyName == nameof(MovieJob.CoverResolution))
            RefreshArtworkPreviewStates();

    }

    private async void SearchQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        var sourceMode = GetSelectedSourceMode();
        if (MetadataSearchSourceModes.AutomaticSources(sourceMode).Count == 0)
        {
            SetStatus(LocalizationService.Get("Status.ManualMode"), false);
            return;
        }
        var sourceProfile = CaptureSearchSourceProfile(sourceMode);
        var candidates = _movieQueue.Where(job => job.CanBatchSearch).ToArray();
        if (candidates.Length == 0)
        {
            SetStatus(LocalizationService.Get("Status.NoBatchSearchCandidates"), false);
            return;
        }

        await RunBusyAsync(LocalizationService.Get("Status.BatchSearchingSource", candidates.Length,
            GetAutomaticSourceDisplayName(sourceMode)), async () =>
        {
            var result = await BatchMetadataSearchCoordinator.SearchAsync(
                candidates,
                _libreDmmClient,
                _r18DevClient,
                CurrentOperationToken,
                sourceProfile: sourceProfile,
                bestArtworkSourceResolver: ResolveBestArtworkSourceAsync,
                sourceMode: sourceMode,
                progress: BeginSearchProgress());
            InvalidateSucceededBatchSearchPreviews(result);
            if (_activeJob.SearchState is MovieSearchState.NeedsReview)
            {
                RefreshActiveJobPresentation();
                await LoadSelectedArtworkPreviewAsync();
                CacheActivePreview();
            }

            ShowBatchSearchSummary(result, "search");
        });
    }

    private void ShowBatchSearchSummary(BatchMetadataSearchResult result, string mode)
    {
        if (!CurrentOperationToken.IsCancellationRequested)
            foreach (var item in result.Items.Where(item => item.Status is
                BatchMetadataSearchItemStatus.Succeeded or BatchMetadataSearchItemStatus.PartiallySucceeded))
                _coverResolutionMonitor.Track(item.Job);
        AppLog.Info($"批量 metadata 搜索结束 mode={mode} requested={result.Items.Count} success={result.SucceededCount} " +
            $"partial={result.PartialCount} failed={result.FailedCount} canceled={result.CanceledCount} notStarted={result.NotStartedCount}");
        var details = LocalizationService.Get("Status.BatchSourcesComplete", result.SucceededCount, result.PartialCount,
            result.FailedCount, result.CanceledCount, result.NotStartedCount);
        string? notice = (result.FailedCount, result.PartialCount) switch
        {
            (> 0, > 0) => LocalizationService.Get("Footer.NeedReview", result.FailedCount + result.PartialCount),
            (> 0, _) => LocalizationService.Get("Footer.LookupFailed", result.FailedCount),
            (_, > 0) => LocalizationService.Get("Queue.Search.Partial"),
            _ => null
        };
        SetResultStatus("Footer.Found", result.SucceededCount + result.PartialCount, details, notice,
            result.CanceledCount > 0 || result.NotStartedCount > 0);
    }

    private void InvalidateSucceededBatchSearchPreviews(BatchMetadataSearchResult result)
    {
        foreach (var item in result.Items.Where(item =>
                     item.Status is BatchMetadataSearchItemStatus.Succeeded or BatchMetadataSearchItemStatus.PartiallySucceeded))
        {
            _jobPreviews.Remove(item.Job);
        }
    }

    private static IReadOnlyList<MetadataSourceSearchAttempt> GetSearchAttempts(Exception exception) =>
        exception switch
        {
            MultiSourceSearchException multiSourceException => multiSourceException.Attempts,
            MultiSourceMergeException mergeException => mergeException.Attempts,
            _ => []
        };

    private void CacheActivePreview()
    {
        if (_activeJob.VideoPath is null || !_activePreviewReady)
        {
            return;
        }

        _jobPreviews.Store(_activeJob, new MoviePreviewState(
            PosterImage.Source,
            FanartImage.Source,
            _fanartDimensions,
            DropHint.Visibility));
    }

    private bool RestoreCachedPreview(MovieJob job)
    {
        _artworkPreviewDeferred = false;
        _activePreviewReady = false;
        if (!_jobPreviews.TryGetValue(job, out var preview))
        {
            return false;
        }

        PosterImage.Source = preview.Poster;
        FanartImage.Source = preview.Fanart;
        _fanartDimensions = preview.FanartDimensions;
        DropHint.Visibility = preview.DropHintVisibility;
        _activePreviewReady = true;
        RefreshArtworkPreviewStates();
        return true;
    }

    internal async Task HandleStartupVideoRequestAsync(StartupVideoRequest request)
    {
        if (request.Kind == StartupVideoRequestKind.None)
        {
            return;
        }

        if (request.Kind == StartupVideoRequestKind.Invalid)
        {
            var message = request.ErrorMessage ?? LocalizationService.Get("Error.StartupUnreadable");
            AppLog.Warning($"启动影片参数被拒绝 reason={message}");
            ShowError(message);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.VideoPath))
        {
            var message = LocalizationService.Get("Error.StartupNoPath");
            AppLog.Warning(message);
            ShowError(message);
            return;
        }

        AppLog.Info($"从启动参数载入影片 path={request.VideoPath}");
        await SelectVideoAsync(request.VideoPath);
    }

    private void TargetMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshTargetLocationUi();
            SyncActiveSaveConfiguration();
            RefreshSaveSettingsPresentation();
        }
    }

    private void TargetOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshTargetLocationPreview();
            SyncActiveSaveConfiguration();
            RefreshSaveSettingsPresentation();
        }
    }

    private void SaveConfiguration_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiInitialized)
        {
            if (ReferenceEquals(sender, DownloadExtrafanartCheckBox) &&
                DownloadExtrafanartCheckBox.IsChecked != true)
            {
                ReplaceLocalExtrafanartCheckBox.IsChecked = false;
            }

            SyncActiveSaveConfiguration();
            RefreshSaveSettingsPresentation();
        }
    }

    private void SavePreferencePresentation_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshSaveSettingsPresentation();
            RefreshQueueUi();
        }
    }

    private void ToggleSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _saveSettingsExpanded = !_saveSettingsExpanded;
        PositionOutputDrawer();
        RefreshSaveSettingsPresentation();
        if (_saveSettingsExpanded) CloseOutputButton.Focus();
        else SaveSettingsToggleButton.Focus();
    }

    private void PositionOutputDrawer()
    {
        var widthForFields = DeskLayout.ActualWidth - QueueColumn.ActualWidth - QueueSpacerColumn.ActualWidth -
            ArtworkColumn.ActualWidth - DeskLayout.ColumnDefinitions[3].ActualWidth -
            OutputDrawer.Width - OutputDrawer.Margin.Left;
        var overlay = widthForFields < 370;
        Grid.SetColumn(OutputDrawer, overlay ? 0 : 5);
        Grid.SetColumnSpan(OutputDrawer, overlay ? 6 : 1);
    }

    private void RefreshSaveSettingsPresentation()
    {
        if (!_uiInitialized || SaveSettingsOptionsPanel is null)
        {
            return;
        }

        OutputDrawer.Visibility = _saveSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
        SaveSettingsOptionsPanel.Visibility = _saveSettingsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        SaveSettingsToggleButton.SetResourceReference(BackgroundProperty, _saveSettingsExpanded ? "SelectionBrush" : "WindowBrush");
        SaveSettingsToggleButton.ToolTip = LocalizationService.Get(
            _saveSettingsExpanded
                ? "Main.SaveSettings.Collapse"
                : "Main.SaveSettings.Expand");
    }

    private void IncludeIdInTitle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiInitialized || _applyingSaveConfiguration)
        {
            return;
        }

        var includeIdInTitle = IncludeIdInTitleCheckBox.IsChecked == true;
        foreach (var job in _movieQueue.Append(_activeJob).Distinct())
        {
            if (job.SaveConfiguration is not { } configuration ||
                configuration.SaveOptions.IncludeIdInTitle == includeIdInTitle)
            {
                continue;
            }

            job.UpdateSaveConfiguration(configuration with
            {
                SaveOptions = configuration.SaveOptions with
                {
                    IncludeIdInTitle = includeIdInTitle
                }
            });
        }

        RefreshSaveSettingsPresentation();
    }

    private void CustomRootText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshTargetLocationPreview();
            SyncActiveSaveConfiguration();
        }
    }

    private void CustomRootTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        RememberCurrentCustomRoot();
        RefreshTargetLocationPreview();
    }

    private void CustomRootTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter)
        {
            return;
        }

        RememberCurrentCustomRoot();
        RefreshTargetLocationPreview();
        e.Handled = true;
    }

    private void ChooseTargetFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("Dialog.ChooseLibraryRoot"),
            Multiselect = false
        };
        var currentRoot = CustomRootTextBox.Text.Trim();
        var sourceDirectory = _activeJob.VideoPath is null ? null : Path.GetDirectoryName(_activeJob.VideoPath);
        var initialDirectory = Directory.Exists(currentRoot)
            ? currentRoot
            : Directory.Exists(_lastValidCustomRootDirectory)
                ? _lastValidCustomRootDirectory
                : sourceDirectory;
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _lastValidCustomRootDirectory = dialog.FolderName;
            RememberCustomRoot(dialog.FolderName);
            CustomRootTextBox.Text = dialog.FolderName;
            CustomRootTextBox.CaretIndex = CustomRootTextBox.Text.Length;
            AppLog.Info($"选择自定义目标根目录 path={dialog.FolderName}");
        }
    }

    private void RecentRoots_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_recentCustomRootDirectories.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = RecentRootsButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Style = (Style)FindResource("CandidateContextMenu")
        };

        foreach (var path in _recentCustomRootDirectories)
        {
            var pathItem = new MenuItem
            {
                Header = new TextBlock
                {
                    Text = path,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 490
                },
                Tag = path,
                ToolTip = path,
                Style = (Style)FindResource("CandidateMenuItem")
            };
            pathItem.Click += (_, _) => SelectRecentCustomRoot(path);
            menu.Items.Add(pathItem);
        }

        var currentCanBeRemoved = CustomRootHistory.TryNormalizePath(
                                      CustomRootTextBox.Text,
                                      out var currentRoot) &&
                                  _recentCustomRootDirectories.Contains(
                                      currentRoot,
                                      StringComparer.OrdinalIgnoreCase);
        var removeItem = new MenuItem
        {
            Header = new TextBlock
            {
                Text = LocalizationService.Get("Menu.RemoveCurrentRoot"),
                Foreground = new SolidColorBrush(currentCanBeRemoved
                    ? Color.FromRgb(255, 157, 166)
                    : Color.FromRgb(112, 128, 148))
            },
            Tag = "remove-current",
            IsEnabled = currentCanBeRemoved,
            Style = (Style)FindResource("CandidateMenuItem")
        };
        removeItem.Click += (_, _) => RemoveCurrentRecentRoot();
        menu.Items.Add(removeItem);

        var clearItem = new MenuItem
        {
            Header = new TextBlock
            {
                Text = LocalizationService.Get("Menu.ClearRecentRoots"),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 157, 166))
            },
            Tag = "clear-all",
            Style = (Style)FindResource("CandidateMenuItem")
        };
        clearItem.Click += (_, _) => ClearRecentRoots();
        menu.Items.Add(clearItem);

        RecentRootsButton.ContextMenu = menu;
        menu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private void SelectRecentCustomRoot(string path)
    {
        RememberCustomRoot(path);
        _lastValidCustomRootDirectory = path;
        CustomRootTextBox.Text = path;
        CustomRootTextBox.CaretIndex = CustomRootTextBox.Text.Length;
        AppLog.Info($"选择最近自定义目标根目录 available={Directory.Exists(path)} path={path}");
    }

    private void RemoveCurrentRecentRoot()
    {
        if (!CustomRootHistory.TryNormalizePath(CustomRootTextBox.Text, out var currentRoot))
        {
            return;
        }

        _recentCustomRootDirectories.RemoveAll(path =>
            path.Equals(currentRoot, StringComparison.OrdinalIgnoreCase));
        RefreshRecentRootsButton();
        AppLog.Info($"移除最近自定义目标根目录 path={currentRoot}");
        SetStatus(LocalizationService.Get("Status.RecentRootRemoved"), true);
    }

    private void ClearRecentRoots()
    {
        _recentCustomRootDirectories.Clear();
        RefreshRecentRootsButton();
        AppLog.Info("清空最近自定义目标根目录");
        SetStatus(LocalizationService.Get("Status.RecentRootsCleared"), true);
    }

    private void RememberCurrentCustomRoot()
    {
        if (CustomRootHistory.TryNormalizePath(CustomRootTextBox.Text, out var currentRoot))
        {
            RememberCustomRoot(currentRoot);
        }
    }

    private void RememberCustomRoot(string path)
    {
        var normalized = CustomRootHistory.Normalize(_recentCustomRootDirectories, path);
        _recentCustomRootDirectories.Clear();
        _recentCustomRootDirectories.AddRange(normalized);
        _customRootAvailabilityCheckPath = CustomRootHistory.TryNormalizePath(path, out var normalizedPath)
            ? normalizedPath
            : null;
        RefreshRecentRootsButton();
    }

    private void RefreshRecentRootsButton()
    {
        if (!_uiInitialized)
        {
            return;
        }

        RecentRootsButton.Content = _recentCustomRootDirectories.Count == 0
            ? LocalizationService.Get("Main.RecentRoots")
            : LocalizationService.Get("Main.RecentRootsCount", _recentCustomRootDirectories.Count);
        RecentRootsButton.IsEnabled = _recentCustomRootDirectories.Count > 0;
    }

    private OrganizationOptions GetOrganizationOptions() =>
        new(
            GetSelectedTargetMode(),
            RenameVideoCheckBox.IsChecked == true,
            GetSelectedTargetMode() is OrganizationTargetMode.CustomRootNumberFolder
                ? CustomRootTextBox.Text
                : null,
            GetCrossVolumeVerificationMode());

    private MovieSaveConfiguration CaptureCurrentSaveConfiguration() => new(
        new SaveOptions(
            WriteNfoCheckBox.IsChecked == true,
            DownloadPosterCheckBox.IsChecked == true,
            DownloadFanartCheckBox.IsChecked == true,
            DownloadExtrafanartCheckBox.IsChecked == true,
            IncludeIdInTitleCheckBox.IsChecked == true,
            OverwriteExisting: false,
            ReplaceLocalExtrafanart: DownloadExtrafanartCheckBox.IsChecked == true &&
                                     ReplaceLocalExtrafanartCheckBox.IsChecked == true),
        GetOrganizationOptions());

    private void SyncActiveSaveConfiguration()
    {
        if (_applyingSaveConfiguration || _activeJob.VideoPath is null)
        {
            return;
        }

        _activeJob.UpdateSaveConfiguration(CaptureCurrentSaveConfiguration());
    }

    private void ApplySaveConfiguration(MovieSaveConfiguration configuration)
    {
        _applyingSaveConfiguration = true;
        try
        {
            WriteNfoCheckBox.IsChecked = configuration.SaveOptions.WriteNfo;
            DownloadPosterCheckBox.IsChecked = configuration.SaveOptions.DownloadPoster;
            DownloadFanartCheckBox.IsChecked = configuration.SaveOptions.DownloadFanart;
            DownloadExtrafanartCheckBox.IsChecked = configuration.SaveOptions.DownloadExtrafanart;
            ReplaceLocalExtrafanartCheckBox.IsChecked = configuration.SaveOptions.ReplaceLocalExtrafanart;
            RenameVideoCheckBox.IsChecked = configuration.OrganizationOptions.RenameVideo;
            SkipCrossVolumeVerificationCheckBox.IsChecked =
                configuration.OrganizationOptions.CrossVolumeVerification is CrossVolumeVerificationMode.FileSizeOnly;
            CustomRootTextBox.Text = configuration.OrganizationOptions.CustomRootDirectory ?? string.Empty;
            TargetModeComboBox.SelectedItem = TargetModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    configuration.OrganizationOptions.TargetMode.ToString(),
                    StringComparison.Ordinal))
                ?? TargetModeComboBox.Items[0];
        }
        finally
        {
            _applyingSaveConfiguration = false;
        }

        RefreshTargetLocationUi();
        RefreshSaveSettingsPresentation();
    }

    private CrossVolumeVerificationMode GetCrossVolumeVerificationMode() =>
        SkipCrossVolumeVerificationCheckBox.IsChecked == true
            ? CrossVolumeVerificationMode.FileSizeOnly
            : CrossVolumeVerificationMode.FullSha256;

    private OrganizationTargetMode GetSelectedTargetMode()
    {
        var tag = (TargetModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<OrganizationTargetMode>(tag, out var mode)
            ? mode
            : OrganizationTargetMode.VideoDirectory;
    }

    private void RefreshTargetLocationUi()
    {
        var customMode = GetSelectedTargetMode() is OrganizationTargetMode.CustomRootNumberFolder;
        CustomTargetPanel.Visibility = customMode ? Visibility.Visible : Visibility.Collapsed;
        if (customMode && string.IsNullOrWhiteSpace(CustomRootTextBox.Text) &&
            !string.IsNullOrWhiteSpace(_lastValidCustomRootDirectory))
        {
            CustomRootTextBox.Text = _lastValidCustomRootDirectory;
        }
        RefreshTargetLocationPreview();
    }

    private void RefreshTargetLocationPreview()
    {
        _targetConfigurationError = null;
        SkipCrossVolumeVerificationCheckBox.Visibility = Visibility.Collapsed;
        var customMode = GetSelectedTargetMode() is OrganizationTargetMode.CustomRootNumberFolder;
        if (customMode &&
            !string.IsNullOrWhiteSpace(CustomRootTextBox.Text) &&
            ShouldCheckCustomRootAvailability(CustomRootTextBox.Text) &&
            TryGetUnavailableCustomRootMessage(CustomRootTextBox.Text, out var unavailableMessage))
        {
            _targetConfigurationError = unavailableMessage;
            TargetPathHintText.Text = unavailableMessage;
            TargetPathHintText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            RefreshSaveAvailability();
            return;
        }

        if (_activeJob.VideoPath is null)
        {
            TargetPathHintText.Text = customMode && string.IsNullOrWhiteSpace(CustomRootTextBox.Text)
                ? LocalizationService.Get("Main.TargetHintCustom")
                : LocalizationService.Get("Main.TargetHint");
            TargetPathHintText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            RefreshSaveAvailability();
            return;
        }

        try
        {
            var organizationOptions = GetOrganizationOptions();
            var fileSet = MovieFileSet.Create(_activeJob.VideoPaths);
            var effectiveOrganizationOptions = OrganizationPathPlanner.ResolveEffectiveOptions(
                organizationOptions,
                fileSet.UsesMultipartNaming);
            var pathPlan = OrganizationPathPlanner.Resolve(
                _activeJob.VideoPath,
                _activeJob.Metadata.Id,
                effectiveOrganizationOptions);
            if (pathPlan.UsesCustomRoot)
            {
                _lastValidCustomRootDirectory = pathPlan.TargetRootDirectory;
            }

            var displayedTargetPath = pathPlan.TargetVideoPath;
            var requiresVerifiedCopy = pathPlan.RequiresVerifiedCopy;
            if (fileSet.UsesMultipartNaming)
            {
                var targetBaseName = effectiveOrganizationOptions.RenameVideo
                    ? pathPlan.TargetBaseName
                    : fileSet.MovieBaseName;
                var primaryPart = fileSet.Parts[0];
                var targetFileName = effectiveOrganizationOptions.RenameVideo
                    ? $"{targetBaseName}-cd{primaryPart.PartNumber}{Path.GetExtension(primaryPart.Path)}"
                    : Path.GetFileName(primaryPart.Path);
                displayedTargetPath = Path.Combine(pathPlan.TargetDirectory, targetFileName);
                requiresVerifiedCopy = fileSet.Parts.Any(part =>
                {
                    var partTargetName = effectiveOrganizationOptions.RenameVideo
                        ? $"{targetBaseName}-cd{part.PartNumber}{Path.GetExtension(part.Path)}"
                        : Path.GetFileName(part.Path);
                    return OrganizationPathPlanner.RequiresVerifiedCopy(
                        part.Path,
                        Path.Combine(pathPlan.TargetDirectory, partTargetName));
                });
            }

            TargetPathHintText.Text = LocalizationService.Get("Main.FinalVideo", displayedTargetPath);
            if (requiresVerifiedCopy)
            {
                SkipCrossVolumeVerificationCheckBox.Visibility = Visibility.Visible;
                var fastCopy = GetCrossVolumeVerificationMode() is CrossVolumeVerificationMode.FileSizeOnly;
                TargetPathHintText.Text += Environment.NewLine + LocalizationService.Get(
                    fastCopy ? "Main.FastCopy" : "Main.SafeCopy");
                TargetPathHintText.SetResourceReference(TextBlock.ForegroundProperty, fastCopy ? "BatchSourceWarningBrush" : "AccentTextBrush");
            }
            else
            {
                TargetPathHintText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessTextBrush");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            _targetConfigurationError = exception.Message;
            TargetPathHintText.Text = exception.Message;
            TargetPathHintText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }

        RefreshSaveAvailability();
    }

    private static bool TryGetUnavailableCustomRootMessage(string candidate, out string message)
    {
        if (!CustomRootHistory.TryNormalizePath(candidate, out var normalizedPath) ||
            Directory.Exists(normalizedPath))
        {
            message = string.Empty;
            return false;
        }

        message = LocalizationService.Get("Status.CustomRootUnavailable", normalizedPath);
        return true;
    }

    private bool ShouldCheckCustomRootAvailability(string candidate) =>
        CustomRootHistory.TryNormalizePath(candidate, out var normalizedPath) &&
        normalizedPath.Equals(_customRootAvailabilityCheckPath, StringComparison.OrdinalIgnoreCase);

    private void RefreshSaveAvailability()
    {
        if (!_uiInitialized)
        {
            return;
        }

        SaveButton.IsEnabled = !_busy && _activeJob.IsSelectedForBatch && _movieQueue.Contains(_activeJob) && !_activeJob.LocalNfoSaveBlocked && _targetConfigurationError is null;
        var detail = _activeJob.LocalNfoSaveBlocked
            ? LocalizationService.Get("Status.LocalNfoUnsafe")
            : _targetConfigurationError is not null
                ? _targetConfigurationError
                : _activeJob.LocalMetadataBundle is not null
                    ? _activeJob.LocalMetadataBundle.HasUnknownXml
                        ? LocalizationService.Get("Status.NfoPreserveUnknown")
                        : LocalizationService.Get("Status.NfoManagedOnly")
                    : null;
        var shortcut = LocalizationService.Get("Main.SaveShortcutTooltip");
        SaveButton.ToolTip = string.IsNullOrWhiteSpace(detail)
            ? shortcut
            : detail + Environment.NewLine + shortcut;
    }

    private async Task SelectVideoCoreAsync(string path)
    {
        if (!VideoFileSupport.IsSupportedExistingFile(path))
        {
            ShowError(LocalizationService.Get("Error.UnsupportedVideo"));
            return;
        }

        var result = await MovieJobLoader.LoadAsync(_activeJob, path, WindowsArtworkImageProcessor.Instance, CurrentOperationToken);
        IsEditingMetadata = false;
        MetadataScrollViewer.ScrollToTop();
        _activeJob.InitializeSaveConfiguration(CaptureCurrentSaveConfiguration());
        _jobLoadResults[_activeJob] = result;
        _jobPreviews.Remove(_activeJob);
        if (!_movieQueue.Contains(_activeJob))
        {
            AttachMovieJob(_activeJob);
            _movieQueue.Add(_activeJob);
        }

        LogMovieJobLoad(_activeJob, result);
        RefreshQueueUi();
        await ActivateMovieJobAsync(_activeJob);
    }

    private void LogMovieJobLoad(MovieJob job, MovieJobLoadResult result)
    {
        AppLog.Info($"选择影片 path={job.VideoPath}");
        if (result.MetadataStatus is LocalMetadataLoadStatus.SidecarInspectionFailed)
        {
            AppLog.Warning($"无法检查本地 sidecar path={job.VideoPath}", result.MetadataError);
            return;
        }

        if (result.MetadataStatus is LocalMetadataLoadStatus.Loaded && job.LocalMetadataBundle is not null)
        {
            AppLog.Info(
                $"本地 NFO 载入成功 path={job.LocalMetadataBundle.Sidecars.NfoPath} id={job.Metadata.Id} " +
                $"diagnostics={job.LocalMetadataBundle.Diagnostics.Count}");
        }
        else if (result.MetadataStatus is LocalMetadataLoadStatus.NfoReadFailed)
        {
            AppLog.Warning($"本地 NFO 读取失败 path={result.Sidecars?.NfoPath}", result.MetadataError);
        }

        if (result.ArtworkDiscovery is not null)
        {
            if (result.ArtworkDiscovery.ExtrafanartPaths.Count > 0)
            {
                AppLog.Info($"已发现本地 extrafanart count={result.ArtworkDiscovery.ExtrafanartPaths.Count}");
            }
            foreach (var diagnostic in result.ArtworkDiscovery.Diagnostics)
            {
                AppLog.Warning(diagnostic);
            }
        }
    }

    private void PresentMovieJobLoadStatus(MovieJob job, MovieJobLoadResult result)
    {
        string metadataStatus;
        var statusSuccess = !string.IsNullOrWhiteSpace(job.Metadata.Id);
        switch (result.MetadataStatus)
        {
            case LocalMetadataLoadStatus.SidecarInspectionFailed:
                SetStatus(
                    LocalizationService.Get("Status.LocalCheckFailed", result.MetadataError?.Message ?? string.Empty),
                    false);
                return;
            case LocalMetadataLoadStatus.Loaded when job.LocalMetadataBundle is not null:
                var diagnosticNote = job.LocalMetadataBundle.Diagnostics.Count == 0
                    ? string.Empty
                    : $"；{string.Join("；", job.LocalMetadataBundle.Diagnostics)}";
                metadataStatus = LocalizationService.Get(
                    "Status.LocalNfoLoaded",
                    job.LocalMetadataBundle.Sidecars.NfoPath,
                    diagnosticNote);
                statusSuccess = true;
                break;
            case LocalMetadataLoadStatus.NfoReadFailed:
                metadataStatus = LocalizationService.Get(
                    "Status.LocalNfoFailed",
                    Path.GetFileName(result.Sidecars?.NfoPath),
                    result.MetadataError?.Message ?? string.Empty);
                statusSuccess = false;
                break;
            default:
                metadataStatus = !string.IsNullOrWhiteSpace(job.Metadata.Id)
                    ? LocalizationService.Get("Status.IdRecognized", job.Metadata.Id)
                    : LocalizationService.Get("Status.NoLocalNfoOrId");
                break;
        }

        if (result.ArtworkDiscovery is { } artworkDiscovery)
        {
            string artworkSummary;
            if (job.LocalArtworkCandidate is null)
            {
                artworkSummary = artworkDiscovery.Diagnostics.Count == 0
                    ? string.Empty
                    : LocalizationService.Get("Status.InvalidLocalImages", artworkDiscovery.Diagnostics.Count);
            }
            else
            {
                var availability = (job.LocalArtworkCandidate.HasPoster, job.LocalArtworkCandidate.HasFanart) switch
                {
                    (true, true) => "poster + fanart",
                    (true, false) => LocalizationService.Get("Status.OnlyPoster"),
                    (false, true) => LocalizationService.Get("Status.OnlyFanart"),
                    _ => LocalizationService.Get("Status.NoArtwork")
                };
                var artworkDiagnosticNote = artworkDiscovery.Diagnostics.Count == 0
                    ? string.Empty
                    : LocalizationService.Get("Status.InvalidImagesSuffix", artworkDiscovery.Diagnostics.Count);
                var previewNote = (PosterImage.Source is not null) == job.LocalArtworkCandidate.HasPoster &&
                                  (FanartImage.Source is not null) == job.LocalArtworkCandidate.HasFanart
                    ? string.Empty
                    : LocalizationService.Get("Status.PreviewIncomplete");
                artworkSummary = LocalizationService.Get(
                    "Status.LocalImagesLoaded",
                    availability,
                    artworkDiagnosticNote,
                    previewNote);
                if (previewNote.Length > 0)
                {
                    statusSuccess = false;
                }
            }

            if (artworkDiscovery.ExtrafanartPaths.Count > 0)
            {
                var extraSummary = LocalizationService.Get(
                    "Status.LocalExtrafanartLoaded",
                    artworkDiscovery.ExtrafanartPaths.Count);
                artworkSummary = string.IsNullOrWhiteSpace(artworkSummary)
                    ? extraSummary
                    : artworkSummary + LocalizationService.Get("Common.DetailSeparator") + extraSummary;
            }

            if (!string.IsNullOrWhiteSpace(artworkSummary))
            {
                metadataStatus += LocalizationService.Get("Common.DetailSeparator") + artworkSummary;
            }

            if (artworkDiscovery.Diagnostics.Count > 0)
            {
                statusSuccess = false;
            }
        }

        SetStatus(metadataStatus, statusSuccess);
        if (statusSuccess && (job.LocalMetadataBundle?.Diagnostics.Count ?? 0) == 0)
            StatusText.Text = LocalizationService.Get(result.MetadataStatus == LocalMetadataLoadStatus.Loaded
                ? "Desk.NfoLoaded" : "Desk.Ready");
    }

    private async Task OpenBrowserAsync(BrowserImportTarget target)
    {
        if (_busy)
        {
            return;
        }

        AppLog.Info($"打开手动搜索浏览器 source={target.SourceName}");
        var browser = new BrowserWindow(target, _r18DevClient) { Owner = this };
        if (browser.ShowDialog() == true)
        {
            await RunBusyAsync(LocalizationService.Get("Status.ReadingBrowser"), async () =>
            {
                var result = await ImportBrowserResultAsync(target, browser);
                var promotedFieldCount = _activeJob.ApplyManualWebSource(result);
                RefreshActiveJobPresentation();
                var artworkLoaded = await LoadSelectedArtworkPreviewAsync();
                var localNote = _activeJob.LocalSourceMetadata is null
                    ? string.Empty
                    : LocalizationService.Get("Status.LocalCandidateSuffix");
                var loadedMessage = LocalizationService.Get(
                    "Status.BrowserCandidateLoaded",
                    GetSourceDisplayName(result),
                    promotedFieldCount,
                    localNote);
                SetStatus(
                    artworkLoaded.Poster
                        ? loadedMessage
                        : loadedMessage + LocalizationService.Get("Status.CoverPreviewMissing"),
                    true);
            });
        }
    }

    private async Task<MovieMetadata> ImportBrowserResultAsync(
        BrowserImportTarget target,
        BrowserWindow browser)
    {
        var pageUrl = browser.PageUrl ?? target.InitialUrl;
        if (target.SourceName == BrowserImportRouting.JavLibrary)
        {
            if (string.IsNullOrWhiteSpace(browser.PageHtml))
            {
                throw new InvalidDataException(LocalizationService.Get(
                    "Browser.ImportMissingContent",
                    target.DisplayName));
            }

            return await _javLibraryClient.ParseDetailPageAsync(
                browser.PageHtml,
                pageUrl,
                _activeJob.Metadata.Id,
                CurrentOperationToken);
        }

        if (target.SourceName == MetadataSourcePreferenceProfile.LibreDmm)
        {
            var pageMovieId = BrowserImportRouting.TryExtractMovieId(target.SourceName, pageUrl);
            if (string.IsNullOrWhiteSpace(pageMovieId))
            {
                throw new InvalidDataException(LocalizationService.Get(
                    "Browser.ImportMissingId",
                    target.DisplayName));
            }

            return await _libreDmmClient.SearchAsync(pageMovieId, CurrentOperationToken);
        }

        if (target.SourceName == MetadataSourcePreferenceProfile.R18Dev)
        {
            return await _r18DevClient.ImportDetailPageAsync(
                pageUrl,
                _activeJob.Metadata.Id,
                CurrentOperationToken);
        }

        throw new InvalidDataException(LocalizationService.Get(
            "Browser.ImportUnsupportedSource",
            target.DisplayName));
    }

    private MovieMetadata ApplyOnlineSources(
        MovieMetadata preferredOnlineMetadata,
        IReadOnlyList<MovieMetadata> onlineSources)
    {
        var result = _activeJob.ApplyOnlineSources(preferredOnlineMetadata, onlineSources);
        RefreshActiveJobPresentation();
        return result;
    }

    private string GetSelectedSourceMode() =>
        MetadataSearchSourceModes.Normalize((SourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());

    private void ApplyMetadata(MovieMetadata result, IReadOnlyList<MovieMetadata> sourceResults)
    {
        _activeJob.ApplyMetadata(result, sourceResults);
        RefreshActiveJobPresentation();
    }

    private void RefreshActiveJobPresentation()
    {
        DataContext = _activeJob.Metadata;
        RefreshSourceBadges();
        RefreshArtworkSourceBadge();
        RefreshRetryFailedSourcesUi();
        RefreshTargetLocationPreview();
    }

    private void RefreshRetryFailedSourcesUi()
    {
        if (!_uiInitialized || RetryFailedSourcesButton is null)
        {
            return;
        }

        var batchCount = UseBatchActions
            ? _movieQueue.Count(job => job.IsSelectedForBatch && CanRetrySources(job)) : 0;
        var canRetry = !_busy && (UseBatchActions ? batchCount > 0 : _activeJob.IsSelectedForBatch && CanRetrySources(_activeJob));
        RetryFailedSourcesButton.Content = UseBatchActions
            ? LocalizationService.Get("SearchFeedback.RetryQueue", batchCount)
            : LocalizationService.Get("SearchFeedback.RetryFailed");
        RetryFailedSourcesButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        RetryFailedSourcesButton.IsEnabled = canRetry;
        RetryFailedSourcesButton.ToolTip = LocalizationService.Get("SearchFeedback.RetrySourceScope",
            GetAutomaticSourceDisplayName(GetSelectedSourceMode()));
    }

    private bool CanRetrySources(MovieJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Metadata.Id)) return false;
        // This runs for every selected queue row during statistics refresh. Avoid
        // parsing the ID and allocating a capturing predicate just to inspect attempts.
        var attempts = job.LastSearchAttempts;
        for (var index = 0; index < attempts.Count; index++)
        {
            var attempt = attempts[index];
            if (!attempt.Success && IsRetryableMetadataSource(attempt.SourceName)) return true;
        }
        return false;
    }

    private void Metadata_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MovieMetadata.Id))
        {
            RefreshQueueUi();
            RefreshTargetLocationPreview();
        }
    }

    private void MetadataReview_SelectionChanged(object? sender, MetadataSelectionChangedEventArgs eventArgs)
    {
        RefreshSourceBadge(eventArgs.Field);
    }

    private void RefreshSourceBadges()
    {
        foreach (var (field, _) in GetSourceBadgeControls())
        {
            RefreshSourceBadge(field);
        }
    }

    private void RefreshSourceBadge(MetadataField field)
    {
        var badge = GetSourceBadgeControls()
            .FirstOrDefault(item => item.Field == field)
            .Badge;
        if (badge is null)
        {
            return;
        }

        var candidate = _activeJob.MetadataReview.GetSelectedCandidate(field);
        var candidates = _activeJob.MetadataReview.GetCandidates(field);
        var canChoose = candidates.Any(item => item != candidate);
        var label = candidate is null
            ? LocalizationService.Get("Main.AutomaticSourceShort")
            : GetCandidateSourceDisplayName(candidate.Source);
        badge.Content = canChoose ? $"{label} ▾" : label;
        badge.Visibility = IsEditingMetadata && candidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        badge.IsEnabled = IsEditingMetadata && canChoose;
        badge.ToolTip = canChoose
            ? LocalizationService.Get("Artwork.SourceFieldTooltip", GetFieldDisplayName(field), candidates.Count)
            : candidate is null ? null : LocalizationService.Get("Artwork.Source", label);
        if (!canChoose && !string.IsNullOrWhiteSpace(candidate?.Source.Url))
            badge.ToolTip = $"{badge.ToolTip}\n{candidate.Source.Url}";
    }

    private void SourceBadge_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!IsEditingMetadata || sender is not Button badge ||
            !Enum.TryParse<MetadataField>(badge.Tag?.ToString(), out var field))
        {
            return;
        }

        var candidates = _activeJob.MetadataReview.GetCandidates(field);
        var selected = _activeJob.MetadataReview.GetSelectedCandidate(field);
        if (!candidates.Any(item => item != selected)) return;
        var menu = new ContextMenu
        {
            PlacementTarget = badge,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Style = (Style)FindResource("CandidateContextMenu")
        };

        foreach (var candidate in candidates)
        {
            var candidateSourceName = GetCandidateSourceDisplayName(candidate.Source);
            var sourceText = new TextBlock
            {
                Text = $"{(candidate == selected ? "✓ " : string.Empty)}{candidateSourceName}",
                Foreground = new SolidColorBrush(Color.FromRgb(111, 168, 255)),
                FontWeight = FontWeights.SemiBold
            };
            var valueText = new TextBlock
            {
                Text = BuildCandidatePreview(candidate.Value),
                Foreground = new SolidColorBrush(Color.FromRgb(184, 197, 214)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 490
            };
            var header = new StackPanel();
            header.Children.Add(sourceText);
            header.Children.Add(valueText);

            var menuItem = new MenuItem
            {
                Header = header,
                Tag = candidate,
                Style = (Style)FindResource("CandidateMenuItem"),
                ToolTip = string.IsNullOrWhiteSpace(candidate.Source.Url)
                    ? candidateSourceName
                    : candidate.Source.Url
            };
            menuItem.Click += (_, _) => SelectSourceCandidate(candidate);
            menu.Items.Add(menuItem);
        }

        badge.ContextMenu = menu;
        menu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private void SelectSourceCandidate(MetadataFieldCandidate candidate)
    {
        if (!_activeJob.MetadataReview.SelectCandidate(candidate.Field, candidate.Source.Name))
        {
            return;
        }

        var fieldName = GetFieldDisplayName(candidate.Field);
        var candidateSourceName = GetCandidateSourceDisplayName(candidate.Source);
        AppLog.Info($"字段来源切换 field={candidate.Field} source={candidate.Source.Name}");
        SetStatus(LocalizationService.Get("Status.FieldSourceChanged", fieldName, candidateSourceName), true);
    }

    private void RefreshArtworkSourceBadge()
    {
        var candidate = _activeJob.ArtworkReview.SelectedCandidate;
        var candidates = _activeJob.ArtworkReview.Candidates;
        ArtworkSourceButton.Content = candidate is null
            ? LocalizationService.Get("Artwork.Select")
            : $"{GetCandidateSourceDisplayName(candidate.Source)} ▾";
        ArtworkSourceButton.Visibility = candidate is null && _activeJob.VideoPath is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArtworkSourceButton.IsEnabled = !_busy && (_activeJob.VideoPath is not null || candidate is not null);
        ArtworkSourceButton.ToolTip = candidate is null
            ? LocalizationService.Get("Artwork.SelectTooltip")
            : LocalizationService.Get("Artwork.SourceTooltip", candidates.Count);
    }

    private void ArtworkSourceButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_activeJob.VideoPath is null && _activeJob.ArtworkReview.Candidates.Count == 0)
        {
            return;
        }

        var selected = _activeJob.ArtworkReview.SelectedCandidate;
        var menu = new ContextMenu
        {
            PlacementTarget = ArtworkSourceButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Style = (Style)FindResource("CandidateContextMenu")
        };

        foreach (var candidate in _activeJob.ArtworkReview.Candidates)
        {
            var candidateSourceName = GetCandidateSourceDisplayName(candidate.Source);
            var sourceText = new TextBlock
            {
                Text = $"{(candidate == selected ? "✓ " : string.Empty)}{candidateSourceName}",
                Foreground = new SolidColorBrush(Color.FromRgb(111, 168, 255)),
                FontWeight = FontWeights.SemiBold
            };
            var valueText = new TextBlock
            {
                Text = GetArtworkCandidateDescription(candidate),
                Foreground = new SolidColorBrush(Color.FromRgb(184, 197, 214)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var header = new StackPanel();
            header.Children.Add(sourceText);
            header.Children.Add(valueText);

            var menuItem = new MenuItem
            {
                Header = header,
                Tag = candidate,
                Style = (Style)FindResource("CandidateMenuItem"),
                ToolTip = string.Join(Environment.NewLine, candidate.Urls)
            };
            menuItem.Click += async (_, _) => await SelectArtworkSourceCandidateAsync(candidate);
            menu.Items.Add(menuItem);
        }

        if (_activeJob.VideoPath is not null)
        {
            var chooseSourceText = new TextBlock
            {
                Text = LocalizationService.Get("Menu.ChooseLocalCover"),
                Foreground = new SolidColorBrush(Color.FromRgb(111, 168, 255)),
                FontWeight = FontWeights.SemiBold
            };
            var chooseValueText = new TextBlock
            {
                Text = LocalizationService.Get("Menu.ChooseLocalCoverDescription"),
                Foreground = new SolidColorBrush(Color.FromRgb(184, 197, 214)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var chooseHeader = new StackPanel();
            chooseHeader.Children.Add(chooseSourceText);
            chooseHeader.Children.Add(chooseValueText);
            var chooseItem = new MenuItem
            {
                Header = chooseHeader,
                Tag = "choose-local-cover",
                Style = (Style)FindResource("CandidateMenuItem"),
                ToolTip = LocalizationService.Get("Menu.ImageTypesTooltip")
            };
            chooseItem.Click += async (_, _) => await ChooseManualCoverAsync();
            menu.Items.Add(chooseItem);
        }

        ArtworkSourceButton.ContextMenu = menu;
        menu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private async Task SelectArtworkSourceCandidateAsync(ArtworkCoverCandidate candidate)
    {
        if (!_activeJob.SelectArtworkSource(candidate.Source.Name))
        {
            return;
        }

        RefreshArtworkSourceBadge();
        AppLog.Info($"统一封套来源切换 source={candidate.Source.Name} posterFanartLocked=true");
        var candidateSourceName = GetCandidateSourceDisplayName(candidate.Source);
        await RunBusyAsync(LocalizationService.Get("Status.LoadingCover", candidateSourceName), async () =>
        {
            var preview = await LoadSelectedArtworkPreviewAsync();
            var loaded = preview.Poster && preview.Fanart;
            SetStatus(
                loaded
                    ? LocalizationService.Get("Status.CoverChanged", candidateSourceName)
                    : LocalizationService.Get("Status.CoverIncomplete", candidateSourceName),
                loaded);
        });
    }

    private static string GetArtworkCandidateDescription(ArtworkCoverCandidate candidate)
    {
        if (candidate.IsSidecarPair)
        {
            return (candidate.HasPoster, candidate.HasFanart) switch
            {
                (true, true) => LocalizationService.Get("Artwork.Pair"),
                (true, false) => LocalizationService.Get("Artwork.PosterOnly"),
                (false, true) => LocalizationService.Get("Artwork.FanartOnly"),
                _ => LocalizationService.Get("Artwork.NoLocal")
            };
        }

        return candidate.Source.Name == "manual-cover"
            ? LocalizationService.Get("Artwork.LocalComplete")
            : candidate.Urls.Count > 1
                ? LocalizationService.Get("Artwork.SharedCount", candidate.Urls.Count)
                : LocalizationService.Get("Artwork.Shared");
    }

    private async Task ChooseManualCoverAsync()
    {
        if (_activeJob.VideoPath is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("Dialog.ChooseCover"),
            Filter = LocalizationService.Get("Dialog.ImageFilter"),
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.GetDirectoryName(_activeJob.VideoPath)
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ApplyManualCoverAsync(dialog.FileName);
        }
    }

    private Task ApplyManualCoverAsync(string path) =>
        RunBusyAsync(LocalizationService.Get("Status.ReadingLocalCover"), async () =>
        {
            var bytes = await ArtworkLocationHelper.ReadLocalImageAsync(
                path, WindowsArtworkImageProcessor.Instance,
                CurrentOperationToken);
            var dimensions = PosterImageProcessor.GetDimensions(bytes);
            var manualArtworkCandidate = ArtworkCoverCandidate.CreateCompleteCover(
                new MetadataCandidateSource(
                    "manual-cover",
                    LocalizationService.Get("Artwork.ManualSource"),
                    Path.GetFullPath(path)),
                path);
            _activeJob.SetManualArtwork(manualArtworkCandidate);
            RefreshArtworkSourceBadge();
            var preview = await LoadSelectedArtworkPreviewAsync();
            if (!preview.Poster || !preview.Fanart)
            {
                throw new InvalidDataException(LocalizationService.Get("Error.LocalCoverPreview"));
            }

            AppLog.Info($"手动完整封套载入成功 path={path} size={dimensions.Width}x{dimensions.Height}");
            SetStatus(
                LocalizationService.Get("Status.LocalCoverSelected", Path.GetFileName(path)),
                true);
        });

    private static string BuildCandidatePreview(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return LocalizationService.Get("Common.Empty");
        }

        const int maximumLength = 100;
        return normalized.Length <= maximumLength
            ? normalized
            : $"{normalized[..maximumLength]}…";
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
        _ => field.ToString()
    };

    private IEnumerable<(MetadataField Field, Button Badge)> GetSourceBadgeControls()
    {
        yield return (MetadataField.Title, TitleSourceText);
        yield return (MetadataField.OriginalTitle, OriginalTitleSourceText);
        yield return (MetadataField.ReleaseDate, ReleaseDateSourceText);
        yield return (MetadataField.RuntimeMinutes, RuntimeSourceText);
        yield return (MetadataField.Maker, MakerSourceText);
        yield return (MetadataField.Director, DirectorSourceText);
        yield return (MetadataField.Label, LabelSourceText);
        yield return (MetadataField.Actors, ActorsSourceText);
        yield return (MetadataField.Genres, GenresSourceText);
        yield return (MetadataField.Plot, PlotSourceText);
        yield return (MetadataField.Rating, RatingSourceText);
    }

    private void PosterPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenArtworkViewer(ArtworkViewerItemKind.Poster);
        e.Handled = PosterImage.Source is not null;
    }

    private void FanartPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenArtworkViewer(ArtworkViewerItemKind.Fanart);
        e.Handled = FanartImage.Source is not null;
    }

    private void ArtworkViewerButton_Click(object sender, RoutedEventArgs e) => OpenArtworkViewer(null);

    private async void OpenArtworkViewer(ArtworkViewerItemKind? entryKind)
    {
        var viewerJob = _activeJob;
        var idPrefix = string.IsNullOrWhiteSpace(_activeJob.Metadata.Id)
            ? string.Empty
            : $"{_activeJob.Metadata.Id} · ";
        var localItems = new List<ArtworkViewerItem>();
        var onlineItems = new List<ArtworkViewerItem>();
        var selectedArtwork = _activeJob.ArtworkReview.SelectedCandidate;
        ArtworkViewerItem? selectedPosterItem = null;
        ArtworkViewerItem? selectedFanartItem = null;
        foreach (var candidate in _activeJob.ArtworkReview.Candidates
                     .OrderByDescending(candidate => IsSelectedArtworkCandidate(candidate, selectedArtwork)))
        {
            var isSelectedSource = IsSelectedArtworkCandidate(candidate, selectedArtwork);
            var sourceName = GetCandidateSourceDisplayName(candidate.Source);
            if (candidate.IsSidecarPair)
            {
                AddArtworkViewerSourceItem(
                    candidate,
                    ArtworkViewerItemKind.Poster,
                    candidate.LocalPosterPath,
                    sourceName,
                    isSelectedSource,
                    false,
                    true);
                AddArtworkViewerSourceItem(
                    candidate,
                    ArtworkViewerItemKind.Fanart,
                    candidate.LocalFanartPath,
                    sourceName,
                    isSelectedSource,
                    false,
                    true);
                continue;
            }

            var locations = candidate.FullCoverLocations;
            if (locations.Count == 0)
            {
                continue;
            }

            var isLocalSource = locations.Any(location =>
                ArtworkLocationHelper.TryGetLocalPath(location, out _));
            AddArtworkViewerSourceItem(
                candidate,
                ArtworkViewerItemKind.Poster,
                locations[0],
                sourceName,
                isSelectedSource,
                true,
                false,
                isLocalSource,
                locations);
            AddArtworkViewerSourceItem(
                candidate,
                ArtworkViewerItemKind.Fanart,
                locations[0],
                sourceName,
                isSelectedSource,
                false,
                false,
                isLocalSource,
                locations);
        }

        var selectedSampleLocations = _activeJob.Metadata.ScreenshotUrls
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sampleLocations = _activeJob.AvailableScreenshotUrls
            .Concat(_activeJob.Metadata.ScreenshotUrls)
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sampleSourceNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var sampleSourceDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localSampleLocations = _activeJob.LocalExtrafanartPaths
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _activeJob.SourceResults
                     .Concat(_activeJob.LocalSourceMetadata is null ? [] : [_activeJob.LocalSourceMetadata]))
        {
            AddSampleSourceMembership(source, onlyWhenUnassigned: false);
        }
        AddSampleSourceMembership(_activeJob.Metadata, onlyWhenUnassigned: true);

        void AddSampleSourceMembership(MovieMetadata source, bool onlyWhenUnassigned)
        {
            var candidateSource = MetadataCandidateSource.FromMetadata(source);
            if (string.IsNullOrWhiteSpace(candidateSource.Name) || candidateSource.Name == "unknown")
            {
                return;
            }

            var sampleSourceName = candidateSource.Name == "local-nfo" &&
                                   _activeJob.LocalArtworkCandidate is not null
                ? _activeJob.LocalArtworkCandidate.Source.Name
                : candidateSource.Name;
            var sampleSourceDisplayName = candidateSource.Name == "local-nfo" &&
                                          _activeJob.LocalArtworkCandidate is not null
                ? GetCandidateSourceDisplayName(_activeJob.LocalArtworkCandidate.Source)
                : GetSourceDisplayName(source);
            sampleSourceDisplayNames.TryAdd(sampleSourceName, sampleSourceDisplayName);
            foreach (var screenshotUrl in source.ScreenshotUrls.Where(ArtworkLocationHelper.IsSupported))
            {
                var normalizedLocation = ArtworkLocationHelper.Normalize(screenshotUrl);
                if (onlyWhenUnassigned && sampleSourceNames.ContainsKey(normalizedLocation))
                {
                    continue;
                }

                if (!sampleSourceNames.TryGetValue(normalizedLocation, out var sourceNames))
                {
                    sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    sampleSourceNames[normalizedLocation] = sourceNames;
                }
                sourceNames.Add(sampleSourceName);
            }
        }

        for (var index = 0; index < sampleLocations.Length; index++)
        {
            var isLocal = localSampleLocations.Contains(sampleLocations[index]);
            var relatedSourceNames = !isLocal && sampleSourceNames.TryGetValue(
                sampleLocations[index],
                out var associatedSourceNames)
                    ? associatedSourceNames.ToArray()
                    : [];
            var sourceName = isLocal
                ? LocalizationService.Get("ArtworkViewer.SourceLocal")
                : relatedSourceNames.Length > 0
                    ? string.Join(
                        " / ",
                        relatedSourceNames.Select(name => sampleSourceDisplayNames.TryGetValue(name, out var displayName)
                            ? displayName
                            : name))
                    : string.Empty;
            var sourceSuffix = string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $" · {sourceName}";
            var item = new ArtworkViewerItem(
                $"{idPrefix}{LocalizationService.Get("ArtworkViewer.SampleTitle", index + 1)}{sourceSuffix}",
                null,
                sampleLocations[index],
                isSelectableSample: true,
                isSelected: selectedSampleLocations.Contains(sampleLocations[index]),
                kind: ArtworkViewerItemKind.Extrafanart,
                isLocal: isLocal,
                relatedSourceNames: relatedSourceNames);
            (isLocal ? localItems : onlineItems).Add(item);
        }

        var items = localItems.Concat(onlineItems).ToList();

        if (items.Count == 0)
        {
            SetStatus(LocalizationService.Get("ArtworkViewer.NoImages"), false);
            return;
        }

        var initialItem = entryKind switch
        {
            ArtworkViewerItemKind.Poster => selectedPosterItem,
            ArtworkViewerItemKind.Fanart => selectedFanartItem,
            _ => selectedPosterItem ?? selectedFanartItem
        };
        var selectedIndex = initialItem is null
            ? 0
            : Math.Max(0, items.FindIndex(item => ReferenceEquals(item, initialItem)));
        var viewer = new ArtworkViewerWindow(
            items,
            selectedIndex,
            LoadArtworkViewerImageAsync,
            initialKind: null,
            currentArtworkSourceName: selectedArtwork?.Source.Name)
        {
            Owner = this
        };
        var returnFocus = Keyboard.FocusedElement;
        var selectionApplied = viewer.ShowDialog() == true;
        RestoreKeyboardFocus(returnFocus);
        if (!ReferenceEquals(viewerJob, _activeJob)) return;
        if (selectionApplied)
        {
            _activeJob.Metadata.ScreenshotUrls = viewer.SelectedSampleLocations;
        }

        var artworkSourceChanged = false;
        if (viewer.DeletedLocalArtworkLocations.Count > 0)
        {
            _activeJob.RemoveLocalArtworkLocations(viewer.DeletedLocalArtworkLocations);
        }

        if (selectionApplied &&
            viewer.ArtworkSourceChanged &&
            !string.IsNullOrWhiteSpace(viewer.SelectedArtworkSourceName))
        {
            artworkSourceChanged = _activeJob.SelectArtworkSource(viewer.SelectedArtworkSourceName);
        }

        if (viewer.DeletedLocalArtworkLocations.Count > 0 || artworkSourceChanged)
        {
            var loadingSource = viewerJob.ArtworkReview.SelectedCandidate?.Source;
            await RunBusyAsync(LocalizationService.Get("Status.LoadingCover",
                loadingSource is null ? LocalizationService.Get("ArtworkViewer.SourceLocal") :
                    GetCandidateSourceDisplayName(loadingSource)), async () =>
            {
                if (!ReferenceEquals(viewerJob, _activeJob)) return;
                RefreshActiveJobPresentation();
                await LoadSelectedArtworkPreviewAsync();
                if (!ReferenceEquals(viewerJob, _activeJob)) return;
                CacheActivePreview();
                if (artworkSourceChanged)
                {
                    var source = _activeJob.ArtworkReview.SelectedCandidate?.Source;
                    SetStatus(
                        LocalizationService.Get(
                            "Status.CoverChanged",
                            source is null ? viewer.SelectedArtworkSourceName : GetCandidateSourceDisplayName(source)),
                        true);
                }
                else
                {
                    SetStatus(
                        LocalizationService.Get(
                            "Status.LocalArtworkDeleted",
                            viewer.DeletedLocalArtworkLocations.Count),
                        true);
                }
            });
        }
        else if (selectionApplied)
        {
            SetStatus(
                LocalizationService.Get(
                    "Status.ExtrafanartSelection",
                    viewer.SelectedSampleLocations.Count,
                    sampleLocations.Length),
                true);
        }

        void AddArtworkViewerSourceItem(
            ArtworkCoverCandidate candidate,
            ArtworkViewerItemKind kind,
            string location,
            string sourceDisplayName,
            bool isSelectedSource,
            bool requiresPosterCrop,
            bool canDeleteLocal,
            bool isLocal = true,
            IReadOnlyList<string>? sourceLocations = null)
        {
            if (!ArtworkLocationHelper.IsSupported(location))
            {
                return;
            }

            var roleName = LocalizationService.Get(kind is ArtworkViewerItemKind.Poster
                ? "Main.ArtworkPosterLabel"
                : "Main.ArtworkFanartLabel");
            var item = new ArtworkViewerItem(
                $"{idPrefix}{roleName} · {sourceDisplayName}",
                null, // Main-panel thumbnails must not masquerade as full viewer images/dimensions.
                location,
                kind: kind,
                isLocal: isLocal,
                canDeleteLocal: canDeleteLocal,
                artworkSourceName: candidate.Source.Name,
                requiresPosterCrop: requiresPosterCrop,
                sourceLocations: sourceLocations,
                artworkSourceDisplayName: sourceDisplayName,
                artworkSourceDescription: GetArtworkCandidateDescription(candidate));
            (isLocal ? localItems : onlineItems).Add(item);
            if (!isSelectedSource)
            {
                return;
            }

            if (kind is ArtworkViewerItemKind.Poster)
            {
                selectedPosterItem = item;
            }
            else
            {
                selectedFanartItem = item;
            }
        }
    }

    private static bool IsSelectedArtworkCandidate(
        ArtworkCoverCandidate candidate,
        ArtworkCoverCandidate? selectedCandidate) =>
        selectedCandidate is not null && string.Equals(
            candidate.Source.Name,
            selectedCandidate.Source.Name,
            StringComparison.OrdinalIgnoreCase);

    private async Task<ArtworkViewerLoadedImage> LoadArtworkViewerImageAsync(
        ArtworkViewerItem item,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var location in item.SourceLocations)
        {
            try
            {
                var sourceBytes = await DownloadPreviewImageAsync(location, cancellationToken);
                return await Task.Run(() =>
                {
                    var displayBytes = item.RequiresPosterCrop
                        ? PosterImageProcessor.CreatePosterJpeg(sourceBytes)
                        : item.Kind is ArtworkViewerItemKind.Fanart && item.IsArtworkSourceCandidate
                            ? PosterImageProcessor.CreateFanartJpeg(sourceBytes)
                            : sourceBytes;
                    var dimensions = PosterImageProcessor.GetDimensions(displayBytes);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ArtworkViewerLoadedImage(
                        PosterBitmapFactory.CreateFrozen(displayBytes, Math.Min(1600, dimensions.Width)),
                        dimensions.Width, dimensions.Height);
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or NotSupportedException or FormatException)
            {
                AppLog.Warning($"查看器封套候选载入失败：{location}", exception);
                lastError = exception;
            }
        }

        throw new InvalidDataException(LocalizationService.Get("Error.PreviewDownload"), lastError);
    }

    private Task<(bool Poster, bool Fanart)> LoadSelectedArtworkPreviewAsync()
    {
        _navigationPreviewCancellation?.Cancel();
        return LoadArtworkPreviewAsync(CurrentOperationToken);
    }

    private async Task<(bool Poster, bool Fanart)> LoadArtworkPreviewAsync(CancellationToken token)
    {
        var request = new ArtworkPreviewRequest(_activeJob, _activeJob.Metadata,
            _activeJob.CoverResolutionRevision, token);
        _artworkPreviewDeferred = false;
        // A canceled or interrupted load must not become a reusable blank/partial preview
        // when switching movies. Completed loads with genuinely missing artwork remain cacheable.
        _activePreviewReady = false;
        _jobPreviews.Remove(_activeJob);
        try
        {
            var result = await LoadSelectedArtworkPreviewCoreAsync(request);
            if (CanApplyArtworkPreview(request)) _activePreviewReady = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!_lifetimeCancellation.IsCancellationRequested && request.Token == CurrentOperationToken &&
                ReferenceEquals(request.Job, _activeJob)) RefreshArtworkPreviewStates();
            throw;
        }
    }

    private sealed record ArtworkPreviewRequest(MovieJob Job, MovieMetadata Metadata, long Revision, CancellationToken Token);

    private bool CanApplyArtworkPreview(ArtworkPreviewRequest request) =>
        !request.Token.IsCancellationRequested && !_lifetimeCancellation.IsCancellationRequested &&
        ReferenceEquals(request.Job, _activeJob) && ReferenceEquals(request.Metadata, request.Job.Metadata) &&
        request.Revision == request.Job.CoverResolutionRevision;

    private async Task<(bool Poster, bool Fanart)> LoadSelectedArtworkPreviewCoreAsync(ArtworkPreviewRequest request)
    {
        var candidate = request.Job.ArtworkReview.SelectedCandidate;
        if (candidate is null)
        {
            ClearPosterPreview();
            ClearFanartPreview();
            return (false, false);
        }

        ShowArtworkLoadingState();

        if (!candidate.IsSidecarPair)
        {
            return (
                await LoadPosterPreviewAsync(request),
                await LoadFanartPreviewAsync(request));
        }

        var posterLoaded = await LoadLocalSidecarPreviewAsync(
            candidate.LocalPosterPath,
            isPoster: true, request);
        var fanartLoaded = await LoadLocalSidecarPreviewAsync(
            candidate.LocalFanartPath,
            isPoster: false, request);
        return (posterLoaded, fanartLoaded);
    }

    private async Task<bool> LoadLocalSidecarPreviewAsync(string path, bool isPoster, ArtworkPreviewRequest request)
    {
        var job = request.Job;
        var revision = request.Revision;
        var token = request.Token;
        token.ThrowIfCancellationRequested();
        if (!CanApplyArtworkPreview(request)) return false;
        if (!ArtworkLocationHelper.TryGetLocalPath(path, out var localPath))
        {
            if (isPoster)
            {
                ClearPosterPreview();
            }
            else
            {
                ClearFanartPreview();
                _coverResolutionMonitor.Observe(job, revision, new(string.Empty, 0, 0));
            }
            return false;
        }

        try
        {
            var image = await ArtworkPreviewLoader.LoadLocalAsync(
                localPath,
                token);
            token.ThrowIfCancellationRequested();
            if (!CanApplyArtworkPreview(request)) return false;
            if (isPoster)
            {
                PosterImage.Source = image.Bitmap;
            }
            else
            {
                FanartImage.Source = image.Bitmap;
                _fanartDimensions = (image.Width, image.Height);
                _coverResolutionMonitor.Observe(job, revision, new(localPath, image.Width, image.Height));
            }
            RefreshArtworkPreviewStates();
            return true;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                NotSupportedException or FormatException)
        {
            AppLog.Warning($"本地 {(isPoster ? "poster" : "fanart")} 预览失败：{localPath}", exception);
            if (!CanApplyArtworkPreview(request)) return false;
            if (isPoster)
            {
                ClearPosterPreview();
            }
            else
            {
                ClearFanartPreview();
                _coverResolutionMonitor.Observe(job, revision, new(string.Empty, 0, 0));
            }
            return false;
        }
    }

    private async Task<bool> LoadPosterPreviewAsync(ArtworkPreviewRequest request)
    {
        request.Token.ThrowIfCancellationRequested();
        if (!CanApplyArtworkPreview(request)) return false;
        var metadata = request.Metadata;
        var candidates = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            ClearPosterPreview();
            return false;
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var imageBytes = await DownloadPreviewImageAsync(candidate, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (!CanApplyArtworkPreview(request)) return false;
                var bitmap = await Task.Run(() => PosterBitmapFactory.CreateFrozen(
                    PosterImageProcessor.CreatePosterJpeg(imageBytes), ArtworkPreviewLoader.MainPreviewWidth), request.Token);
                if (!CanApplyArtworkPreview(request)) return false;
                PosterImage.Source = bitmap;
                RefreshArtworkPreviewStates();
                return true;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or NotSupportedException or FormatException)
            {
                AppLog.Warning($"poster 预览候选失败：{candidate}", exception);
            }
        }

        if (CanApplyArtworkPreview(request)) ClearPosterPreview();
        return false;
    }

    private async Task<bool> LoadFanartPreviewAsync(ArtworkPreviewRequest request)
    {
        var job = request.Job;
        var metadata = request.Metadata;
        var revision = request.Revision;
        var token = request.Token;
        token.ThrowIfCancellationRequested();
        if (!CanApplyArtworkPreview(request)) return false;
        var candidates = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try
            {
                var imageBytes = await DownloadPreviewImageAsync(candidate, token);
                var (dimensions, bitmap) = await Task.Run(() => (
                    PosterImageProcessor.GetDimensions(imageBytes),
                    PosterBitmapFactory.CreateFrozen(PosterImageProcessor.CreateFanartJpeg(imageBytes),
                        ArtworkPreviewLoader.MainPreviewWidth)), token);
                token.ThrowIfCancellationRequested();
                if (!CanApplyArtworkPreview(request)) return false;
                FanartImage.Source = bitmap;
                _fanartDimensions = dimensions;
                if (ReferenceEquals(metadata, job.Metadata))
                    _coverResolutionMonitor.Observe(job, revision, new(candidate, dimensions.Width, dimensions.Height));
                RefreshArtworkPreviewStates();
                return true;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or NotSupportedException or FormatException)
            {
                AppLog.Warning($"fanart 预览候选失败：{candidate}", exception);
            }
        }

        if (!CanApplyArtworkPreview(request)) return false;
        ClearFanartPreview();
        if (ReferenceEquals(metadata, job.Metadata))
            _coverResolutionMonitor.Observe(job, revision, new(string.Empty, 0, 0));
        return false;
    }

    private async Task<byte[]> DownloadPreviewImageAsync(
        string url,
        CancellationToken? cancellationToken = null)
    {
        var token = cancellationToken ?? CurrentOperationToken;
        if (ArtworkLocationHelper.TryGetLocalPath(url, out var localPath))
        {
            return await ArtworkLocationHelper.ReadLocalImageAsync(
                localPath, WindowsArtworkImageProcessor.Instance,
                token);
        }

        var cacheKey = ArtworkLocationHelper.Normalize(url);
        if (_previewImageCache.TryGetValue(cacheKey, out var cachedBytes))
        {
            return cachedBytes;
        }

        Exception? lastError = null;
        foreach (var candidate in DmmImageUrlHelper.GetDownloadCandidates(url).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                    (uri.Host.EndsWith(".dmm.co.jp", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(".dmm.com", StringComparison.OrdinalIgnoreCase)))
                {
                    request.Headers.Referrer = new Uri("https://www.dmm.co.jp/");
                }

                using var response = await _previewHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    token);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(token);
                if (bytes.Length < 128)
                {
                    throw new InvalidDataException(LocalizationService.Get("Error.ImageTooSmall"));
                }

                var dimensions = PosterImageProcessor.GetDimensions(bytes);
                CachePreviewDimensions(cacheKey, new(dimensions.Width, dimensions.Height));
                CachePreviewImage(cacheKey, bytes);
                return bytes;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or NotSupportedException or FormatException)
            {
                AppLog.Warning($"图片预览下载候选失败：{candidate}", exception);
                lastError = exception;
            }
        }

        throw new InvalidDataException(LocalizationService.Get("Error.PreviewDownload"), lastError);
    }

    private void CachePreviewImage(string cacheKey, byte[] bytes)
    {
        if (!_previewImageCache.TryAdd(cacheKey, bytes))
        {
            return;
        }

        _previewImageCacheOrder.Enqueue(cacheKey);
        while (_previewImageCache.Count > 12 && _previewImageCacheOrder.TryDequeue(out var expiredKey))
        {
            _previewImageCache.TryRemove(expiredKey, out _);
        }
    }

    private void CachePreviewDimensions(string key, ArtworkPixelSize size)
    {
        if (!_previewImageDimensions.TryAdd(key, size)) return;
        _previewDimensionOrder.Enqueue(key);
        while (_previewImageDimensions.Count > 256 && _previewDimensionOrder.TryDequeue(out var expired))
            _previewImageDimensions.TryRemove(expired, out _);
    }

    private async Task<ArtworkPixelSize> ProbeCoverResolutionAsync(string location, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ArtworkLocationHelper.TryGetLocalPath(location, out var path))
        {
            var image = await ArtworkLocationHelper.ReadLocalImageWithDimensionsAsync(path, WindowsArtworkImageProcessor.Instance, token);
            return new(image.Width, image.Height);
        }
        var key = ArtworkLocationHelper.Normalize(location);
        if (_previewImageDimensions.TryGetValue(key, out var size)) return size;
        var bytes = await DownloadPreviewImageAsync(location, token);
        if (_previewImageDimensions.TryGetValue(key, out size)) return size;
        var dimensions = PosterImageProcessor.GetDimensions(bytes);
        return new(dimensions.Width, dimensions.Height);
    }

    private void RefreshCoverCheckButton()
    {
        if (!_uiInitialized || _busy || _activeOperationCancellation is not null || _closeRequested) return;
        var count = _coverResolutionMonitor.PendingCount;
        CancelOperationButton.Content = LocalizationService.Get("CoverCheck.Stop", count);
        CancelOperationButton.IsEnabled = count > 0;
        CancelOperationButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Task<string?> ResolveBestArtworkSourceAsync(
        IReadOnlyList<MovieMetadata> sources,
        CancellationToken cancellationToken) =>
        ResolveBestArtworkSourceAsync(_customSourceProfile, sources, cancellationToken);

    private async Task<string?> ResolveBestArtworkSourceAsync(
        MetadataSourcePreferenceProfile? sourceProfile,
        IReadOnlyList<MovieMetadata> sources,
        CancellationToken cancellationToken)
    {
        if (sourceProfile?.ArtworkSource != MetadataSourcePreferenceProfile.BestArtworkResolution)
        {
            return null;
        }

        var selection = await ArtworkResolutionSelector.SelectBestAsync(
            sources,
            async (location, token) =>
            {
                var bytes = await DownloadPreviewImageAsync(location, token);
                var dimensions = PosterImageProcessor.GetDimensions(bytes);
                return new ArtworkPixelSize(dimensions.Width, dimensions.Height);
            },
            cancellationToken);
        if (selection is null)
        {
            return null;
        }

        AppLog.Info(
            $"自定义图片来源按分辨率选择 source={selection.SourceName} " +
            $"width={selection.Size.Width} height={selection.Size.Height}");
        return selection.SourceName;
    }

    private void ClearPosterPreview()
    {
        PosterImage.Source = null;
        RefreshArtworkPreviewStates();
    }

    private void ClearFanartPreview()
    {
        FanartImage.Source = null;
        _fanartDimensions = null;
        FanartHintText.Text = string.Empty;
        RefreshArtworkPreviewStates();
    }

    private void ShowArtworkLoadingState()
    {
        PosterImage.Source = null;
        FanartImage.Source = null;
        _fanartDimensions = null;
        FanartHintText.Text = string.Empty;
        DropHint.Visibility = Visibility.Collapsed;
        PosterPreviewStateText.Text = LocalizationService.Get("ArtworkPreview.Loading");
        FanartPreviewStateText.Text = LocalizationService.Get("ArtworkPreview.Loading");
        PosterPreviewStatePanel.Visibility = Visibility.Visible;
        FanartPreviewStateText.Visibility = Visibility.Visible;
        PosterPreviewBorder.Cursor = Cursors.Arrow;
        FanartPreviewBorder.Cursor = Cursors.Arrow;
        SizeArtwork();
    }

    private void RefreshArtworkPreviewStates()
    {
        if (!_uiInitialized || PosterPreviewStatePanel is null)
        {
            return;
        }

        var hasVideo = _activeJob.VideoPath is not null;
        RefreshCoverCheckButton();
        FanartHintText.Text = _fanartDimensions is { } dimensions
            ? LocalizationService.Get("Artwork.Dimensions", dimensions.Width, dimensions.Height)
            : string.Empty;
        if (_activeJob.HasLowResolutionCover)
            FanartHintText.Text = LocalizationService.Get("CoverCheck.Low") + " · " + _activeJob.CoverResolution!.Dimensions;
        FanartHintText.Visibility = string.IsNullOrEmpty(FanartHintText.Text) ? Visibility.Collapsed : Visibility.Visible;
        FanartHintText.SetResourceReference(TextBlock.ForegroundProperty,
            _activeJob.HasLowResolutionCover ? "BatchSourceWarningBrush" : "MutedBrush");
        DropHint.Visibility = !hasVideo && PosterImage.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        var candidate = _activeJob.ArtworkReview.SelectedCandidate;
        var posterDeferred = _artworkPreviewDeferred && candidate?.HasPoster == true;
        var fanartDeferred = _artworkPreviewDeferred && candidate?.HasFanart == true;
        PosterPreviewStateText.Text = LocalizationService.Get(posterDeferred
            ? "ArtworkPreview.Deferred" : "ArtworkPreview.Unavailable");
        FanartPreviewStateText.Text = LocalizationService.Get(fanartDeferred
            ? "ArtworkPreview.Deferred" : "ArtworkPreview.Unavailable");
        PosterPreviewStateText.ToolTip = posterDeferred ? LocalizationService.Get("ArtworkPreview.DeferredHint") : null;
        FanartPreviewStateText.ToolTip = fanartDeferred ? LocalizationService.Get("ArtworkPreview.DeferredHint") : null;
        PosterPreviewStatePanel.Visibility = hasVideo && PosterImage.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        FanartPreviewStateText.Visibility = hasVideo && FanartImage.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        PosterPreviewBorder.Cursor = PosterImage.Source is null ? Cursors.Arrow : Cursors.Hand;
        FanartPreviewBorder.Cursor = FanartImage.Source is null ? Cursors.Arrow : Cursors.Hand;
        SizeArtwork();
        ArtworkViewerButton.IsEnabled = PosterImage.Source is not null ||
                                        FanartImage.Source is not null ||
                                        _activeJob.AvailableScreenshotUrls.Count > 0;
    }

    private static string GetSourceDisplayName(MovieMetadata metadata) =>
        GetLocalizedSourceDisplayName(
            metadata.SourceName,
            string.IsNullOrWhiteSpace(metadata.SourceDisplayName) ? metadata.SourceName : metadata.SourceDisplayName);

    private static string GetCandidateSourceDisplayName(MetadataCandidateSource source) =>
        GetLocalizedSourceDisplayName(source.Name, source.DisplayName);

    private static string GetLocalizedSourceDisplayName(string sourceName, string fallback) =>
        sourceName.ToLowerInvariant() switch
        {
            "manual" => LocalizationService.Get("Source.Manual"),
            "manual-cover" => LocalizationService.Get("Artwork.ManualSource"),
            "local-nfo" => LocalizationService.Get("Source.LocalNfo"),
            "local-images" => LocalizationService.Get("Source.LocalImages"),
            "unknown" => LocalizationService.Get("Source.Unknown"),
            _ => fallback
        };

    private static HttpClient CreatePreviewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,ja;q=0.7,en;q=0.5");
        return client;
    }

    private static string GetLocalizedTransactionProgress(FileTransactionProgress update) =>
        update.Stage switch
        {
            FileTransactionStage.Preparing => LocalizationService.Get("Progress.Preparing"),
            FileTransactionStage.CopyingMovie => LocalizationService.Get("Progress.Copying"),
            FileTransactionStage.VerifyingSource => LocalizationService.Get("Progress.VerifyingSource"),
            FileTransactionStage.VerifyingMovie => LocalizationService.Get("Progress.Verifying"),
            FileTransactionStage.Committing => LocalizationService.Get("Progress.Committing"),
            FileTransactionStage.RetiringSource => LocalizationService.Get("Progress.RetiringSource"),
            FileTransactionStage.RetiringSourceFast => LocalizationService.Get("Progress.RetiringSourceFast"),
            FileTransactionStage.Completed => LocalizationService.Get("Progress.Completed"),
            _ => update.Message
        };

    private async Task RunBusyAsync(string message, Func<Task> operation)
    {
        if (_busy || _closeRequested)
        {
            return;
        }

        _busy = true;
        _coverResolutionMonitor.Pause(true);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _activeOperationCancellation = operationCancellation;
        SearchButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        ArtworkSourceButton.IsEnabled = false;
        ChooseVideoButton.IsEnabled = false;
        ManualSearchItem.IsEnabled = false;
        SearchQueueButton.IsEnabled = false;
        SaveSelectedButton.IsEnabled = false;
        SelectRecognizedQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0;
        SelectPendingQueueButton.IsEnabled = false;
        SelectIssuesQueueButton.IsEnabled = false;
        SelectNoneQueueButton.IsEnabled = false;
        RemoveSelectedQueueButton.IsEnabled = false;
        ClearQueueButton.IsEnabled = false;
        RemoveCurrentVideoButton.IsEnabled = false;
        MovieQueueList.IsEnabled = false;
        MetadataPanel.IsEnabled = false;
        ArtworkPanel.IsEnabled = false;
        CancelOperationButton.IsEnabled = true;
        CancelOperationButton.SetResourceReference(ContentControl.ContentProperty, "Main.CancelOperation");
        CancelOperationButton.Visibility = Visibility.Visible;
        OperationProgressBar.Value = 0;
        OperationProgressBar.IsIndeterminate = true;
        ProgressPercent.Visibility = Visibility.Collapsed;
        _ = ShowActivityIfStillBusyAsync(operationCancellation);
        Mouse.OverrideCursor = Cursors.Wait;
        SetStatus(message, null);

        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            AppLog.Info("当前操作已取消；文件事务恢复及清理结果见对应事务日志");
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                SetStatus(LocalizationService.Get("Status.OperationCanceled"), false);
            }
        }
        catch (Exception exception)
        {
            EndTransferClosePrompt();
            // Keep recovery errors visible; do not close on a failed/incomplete rollback.
            _closeRequested = false;
            AppLog.Error(message, exception);
            if (exception is TemporaryCleanupException cleanup)
                ShowCleanupDetails(cleanup.Issues, committed: false, cleanup.InnerException);
            else if (exception is FileRecoveryException recovery)
            {
                var summary = LocalizationService.Get("Cleanup.RecoveryFailed");
                SetStatus(summary, false);
                new DiscoveryDetailsWindow(summary + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, recovery.RecoveryPaths) + Environment.NewLine + recovery,
                    LocalizationService.Get("Cleanup.Title")) { Owner = this }.ShowDialog();
            }
            else
                ShowError(GetLocalizedExceptionMessage(exception));
        }
        finally
        {
            EndTransferClosePrompt();
            Mouse.OverrideCursor = null;
            SearchButton.IsEnabled = true;
            ChooseVideoButton.IsEnabled = true;
            MovieQueueList.IsEnabled = true;
            MetadataPanel.IsEnabled = true;
            ArtworkPanel.IsEnabled = true;
            CancelOperationButton.Visibility = Visibility.Collapsed;
            OperationProgressPanel.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = false;
            _activeOperationCancellation = null;
            _busy = false;
            if (operationCancellation.IsCancellationRequested) _coverResolutionMonitor.CancelAll();
            _coverResolutionMonitor.Pause(false);
            RefreshCoverCheckButton();
            RefreshSaveAvailability();
            RefreshArtworkSourceBadge();
            RefreshQueueUi();
            RefreshWorkspacePresentation();
            RefreshRetryFailedSourcesUi();
            if (_closeRequested)
            {
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
                {
                    if (_closeRequested && !_busy)
                    {
                        Close();
                    }
                });
            }
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_activeOperationCancellation is null)
        {
            _coverResolutionMonitor.CancelAll();
            SetStatus(LocalizationService.Get("CoverCheck.Stopped"), null);
            return;
        }
        if (_activeOperationCancellation is null || _activeOperationCancellation.IsCancellationRequested)
        {
            return;
        }

        CancelOperationButton.IsEnabled = false;
        SetStatus(LocalizationService.Get("Status.Canceling"), null);
        _activeOperationCancellation.Cancel();
        _coverResolutionMonitor.CancelAll();
    }

    private void SetSaveResultStatus(int completed, string details, bool cleanupPending, bool canceled)
    {
        if (cleanupPending)
            details += Environment.NewLine + LocalizationService.Get("Cleanup.Committed");
        SetResultStatus("Footer.Saved", completed, details,
            cleanupPending ? LocalizationService.Get("Footer.CleanupPending") : null, canceled);
    }

    private void SetResultStatus(string countKey, int completed, string details, string? notice = null, bool canceled = false)
    {
        var summary = new List<string>();
        if (completed > 0 || (notice is null && !canceled))
            summary.Add(LocalizationService.Get(countKey, completed));
        if (notice is not null) summary.Add(notice);
        if (canceled) summary.Add(LocalizationService.Get("Footer.Canceled"));
        SetStatus(string.Join(" · ", summary), notice is null && !canceled, details);
    }

    private void SetStatus(string message, bool? success, string? details = null)
    {
        _statusRevision++;
        StatusText.Text = message;
        StatusText.ToolTip = details ?? message;
        if (success is null && _busy)
        {
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TabBrush");
            return;
        }
        StatusDot.Fill = new SolidColorBrush(success switch
        {
            true => Color.FromRgb(114, 227, 166),
            false => Color.FromRgb(255, 183, 77),
            null => Color.FromRgb(79, 140, 255)
        });
    }

    private void ShowError(string message)
    {
        SetStatus(message.Replace(Environment.NewLine, " "), false);
        MessageBox.Show(this, message, "JAV Metadata Lite", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowCleanupDetails(IReadOnlyList<TemporaryCleanupIssue> issues, bool committed, Exception? originalError = null)
    {
        _closeRequested = false;
        EndTransferClosePrompt();
        var summary = LocalizationService.Get(committed ? "Cleanup.Committed" : "Cleanup.Incomplete");
        SetStatus(summary, false);
        var details = summary + Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine + Environment.NewLine, issues.Select(issue => issue.Path + Environment.NewLine + issue.Error));
        if (originalError is not null)
            details += Environment.NewLine + Environment.NewLine + originalError;
        new DiscoveryDetailsWindow(details, LocalizationService.Get("Cleanup.Title")) { Owner = this }.ShowDialog();
    }

    private static string GetLocalizedExceptionMessage(Exception exception) =>
        exception is MetadataSourceTimeoutException timeout
            ? LocalizationService.Get(
                "Error.SourceTimeout",
                timeout.SourceDisplayName,
                Math.Ceiling(timeout.Timeout.TotalSeconds))
            : exception is MetadataSourceRateLimitException
                ? LocalizationService.Get("Error.SourceRateLimited")
                : exception.Message;

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppLog.LogDirectory}\"",
                UseShellExecute = true
            });
            SetStatus(LocalizationService.Get("Status.LogDirectory", AppLog.LogDirectory), true);
        }
        catch (Exception exception)
        {
            AppLog.Error("无法打开日志目录", exception);
            ShowError(LocalizationService.Get("Error.OpenLogs", exception.Message));
        }
    }

    private static bool TryGetDroppedPaths(IDataObject data, out string[] paths)
    {
        paths = [];
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] files ||
            files.Length == 0)
        {
            return false;
        }

        paths = files
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return paths.Length > 0;
    }

    private void EndTransferClosePrompt()
    {
        _savingAcrossVolumes = false;
        var dialog = _transferCloseDialog;
        _transferCloseDialog = null;
        if (dialog?.IsVisible == true) dialog.Close();
    }

    private void ConfirmCloseDuringTransfer()
    {
        if (_transferCloseDialog is not null) return;
        var dialog = new AppDialogWindow(LocalizationService.Get("TransferClose.Title"),
            LocalizationService.Get("TransferClose.Message"), LocalizationService.Get("TransferClose.Stop"));
        dialog.CancelButton.Content = LocalizationService.Get("TransferClose.Continue");
        _transferCloseDialog = dialog;
        // Let WPF finish canceling this Closing event before starting a modal dispatcher loop.
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_transferCloseDialog != dialog) return;
            dialog.Owner = this;
            Mouse.OverrideCursor = null;
            try
            {
                if (dialog.ShowDialog() == true && _transferCloseDialog == dialog)
                {
                    _closeRequested = true;
                    CancelOperation_Click(this, new RoutedEventArgs());
                }
            }
            finally
            {
                if (_transferCloseDialog == dialog)
                {
                    _transferCloseDialog = null;
                    if (_busy) Mouse.OverrideCursor = Cursors.Wait;
                }
            }
        });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            if (_closeRequested) return;
            if (_savingAcrossVolumes)
            {
                ConfirmCloseDuringTransfer();
                return;
            }
            _closeRequested = true;
            CancelOperation_Click(this, new RoutedEventArgs());
            return;
        }
        AppLog.Info("JavMetaLite 关闭");
        _navigationPreviewCancellation?.Cancel();
        _coverResolutionMonitor.Dispose();
        PersistPreferencesOnClose();
        _activeJob.MetadataPropertyChanged -= Metadata_PropertyChanged;
        _activeJob.MetadataSelectionChanged -= MetadataReview_SelectionChanged;
        foreach (var job in _movieQueue.Where(job => !ReferenceEquals(job, _activeJob)))
        {
            DetachMovieJob(job);
            job.Dispose();
        }
        DetachMovieJob(_activeJob);
        _activeJob.Dispose();
        _queueRefresh.Cancel();
        _jobPreviews.Clear();
        _activeOperationCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _javLibraryClient.Dispose();
        _libreDmmClient.Dispose();
        _r18DevClient.CoolingDown -= R18CoolingDown;
        _r18DevClient.Dispose();
        _outputService.Dispose();
        _previewHttpClient.Dispose();
    }
}
