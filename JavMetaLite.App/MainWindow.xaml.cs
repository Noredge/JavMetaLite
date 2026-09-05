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

    private enum WorkspaceMode
    {
        Single,
        Batch
    }

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
    private bool _uiInitialized;
    private bool _preferencesLoaded;
    private bool _preferencesCanOverwrite = true;
    private bool _changingQueueSelection;
    private Key? _queueNavigationKey;
    private int _queueFocusVersion;
    private int? _pendingQueueFocusVersion;
    private bool _applyingSaveConfiguration;
    private bool _saveSettingsExpanded;
    private string? _searchToolbarLayoutKey;
    private bool _compactSearchToolbar;
    private WorkspaceMode? _lastSizedWorkspaceMode;
    private WorkspaceMode _workspaceMode = WorkspaceMode.Single;

    private CancellationToken CurrentOperationToken =>
        _activeOperationCancellation?.Token ?? _lifetimeCancellation.Token;

    public MainWindow() : this(new AppPreferencesStore())
    {
    }

    internal MainWindow(AppPreferencesStore preferencesStore)
    {
        _preferencesStore = preferencesStore;
        _outputService = new OutputService();
        _fileOrganizationService = new FileOrganizationService(_outputService);
        LocalizationService.ApplyLanguage(LocalizationService.CurrentLanguageCode);
        InitializeComponent();
        _coverResolutionMonitor = new CoverResolutionMonitor(Dispatcher,
            (locations, token) => CoverResolutionCheck.MeasureAsync(locations, ProbeCoverResolutionAsync, token),
            RefreshCoverCheckButton);
        _r18DevClient.CoolingDown += R18CoolingDown;
        WindowVisualTheme.ApplyDarkTitleBar(this);
        SelectLanguageItem(LocalizationService.CurrentLanguageCode);
        _activeJob.MetadataPropertyChanged += Metadata_PropertyChanged;
        _activeJob.MetadataSelectionChanged += MetadataReview_SelectionChanged;
        AttachMovieJob(_activeJob);
        _movieQueueView = CollectionViewSource.GetDefaultView(_movieQueue);
        _queueRefresh = new QueueRefreshCoordinator(Dispatcher, RefreshQueueUiCore);
        _movieQueueView.Filter = FilterQueueItem;
        MovieQueueList.ItemsSource = _movieQueueView;
        _uiInitialized = true;
        DataContext = _activeJob.Metadata;
        RefreshSourceBadges();
        RefreshArtworkSourceBadge();
        RefreshManualWebLookupUi();
        RefreshSourceSelectionUi();
        RefreshRetryFailedSourcesUi();
        RefreshArtworkPreviewStates();
        RefreshWorkspaceModeUi();
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
        RefreshManualWebLookupUi();
        RefreshSourceSelectionUi();
        RefreshArtworkPreviewStates();
        RefreshWorkspaceModeUi();
    }

    private void SingleMode_Click(object sender, RoutedEventArgs e) =>
        SetWorkspaceMode(WorkspaceMode.Single);

    private void BatchMode_Click(object sender, RoutedEventArgs e) =>
        SetWorkspaceMode(WorkspaceMode.Batch);

    private void SetWorkspaceMode(WorkspaceMode mode)
    {
        if (_workspaceMode == mode && _uiInitialized)
        {
            RefreshWorkspaceModeUi();
            RefreshQueueUi();
            return;
        }

        _workspaceMode = mode;
        if (!_uiInitialized)
        {
            return;
        }

        RefreshWorkspaceModeUi();
        RefreshQueueUi();
        AppLog.Info($"切换工作模式 mode={mode}");
    }

    private void RefreshWorkspaceModeUi()
    {
        if (!_uiInitialized)
        {
            return;
        }

        var isBatch = _workspaceMode is WorkspaceMode.Batch;
        if (_lastSizedWorkspaceMode != _workspaceMode)
        {
            QueueColumn.Width = isBatch ? new GridLength(220) : new GridLength(0);
            QueueSpacerColumn.Width = isBatch ? new GridLength(10) : new GridLength(0);
            ArtworkColumn.Width = new GridLength(isBatch ? 250 : 300);
            _lastSizedWorkspaceMode = _workspaceMode;
        }

        SingleModeButton.IsChecked = !isBatch;
        BatchModeButton.IsChecked = isBatch;
        ScanFolderButton.Visibility = isBatch ? Visibility.Visible : Visibility.Collapsed;
        SingleSavePanel.Visibility = isBatch ? Visibility.Collapsed : Visibility.Visible;
        RemoveCurrentVideoButton.Visibility = !isBatch && _activeJob.VideoPath is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemoveCurrentVideoButton.IsEnabled = !_busy && _activeJob.VideoPath is not null;
        ChooseVideoButton.Content = LocalizationService.Get(isBatch ? "Main.AddMovies" : "Main.ChooseSingleMovie");
        SubtitleText.Text = LocalizationService.Get(isBatch ? "Main.Subtitle.Batch" : "Main.Subtitle.Single");
        RefreshSearchToolbarLayout();
        RefreshSaveSettingsPresentation();
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("Dialog.ChooseVideo"),
            Filter = LocalizationService.Get("Dialog.VideoFilter"),
            Multiselect = _workspaceMode is WorkspaceMode.Batch,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            await AddMovieFilesAsync(dialog.FileNames);
        }
    }

    private async void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        SetWorkspaceMode(WorkspaceMode.Batch);
        var folderDialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("Dialog.ChooseScanRoot"),
            Multiselect = false
        };
        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        var discovery = new MovieDiscoveryWindow(
            folderDialog.FolderName,
            _movieQueue.SelectMany(job => job.VideoPaths))
        {
            Owner = this
        };
        if (discovery.ShowDialog() == true)
        {
            await AddMovieFilesAsync(discovery.SelectedVideoPaths);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedPaths(e.Data, out var paths) &&
                    paths.Length > 0 &&
                    paths.All(path =>
                        VideoFileSupport.IsSupportedExistingFile(path) || Directory.Exists(path))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedPaths(e.Data, out var paths))
        {
            return;
        }

        try
        {
            if (paths.All(VideoFileSupport.IsSupportedExistingFile))
            {
                await AddMovieFilesAsync(paths);
            }
            else
            {
                await ImportDiscoveredPathsAsync(paths);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"无法读取拖入的影片项目 paths={string.Join(" | ", paths)}", exception);
            ShowError(LocalizationService.Get("Error.ReadVideoFolder", exception.Message));
        }
        finally
        {
            e.Handled = true;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
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
                SetStatus(
                    artworkLoaded.Poster
                        ? $"{localPrefix}{imageSummary}{(artworkLoaded.Fanart ? string.Empty : LocalizationService.Get("Status.FanartPreviewMissing"))}{degradedNote}"
                        : $"{localPrefix}{LocalizationService.Get("Status.CoverPreviewMissing")}{degradedNote}",
                    string.IsNullOrWhiteSpace(failedSources));
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

    private void Browser_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MovieIdParser.Normalize(_activeJob.Metadata.Id)))
        {
            ShowError(LocalizationService.Get("Error.EnterId"));
            return;
        }

        ShowBrowserSourceMenu();
    }

    private async void RetryFailedSources_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        if (_workspaceMode == WorkspaceMode.Batch)
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
                sourceMode: sourceMode);
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

    private void ShowBrowserSourceMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = BrowserImportButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Style = (Style)FindResource("CandidateContextMenu")
        };

        foreach (var target in BrowserImportRouting.BuildTargets(
                     _activeJob.Metadata.Id,
                     _activeJob.SourceResults))
        {
            var selectedCount = CountSelectedSourceItems(target.SourceName);
            var sourceText = new TextBlock
            {
                Text = LocalizationService.Get("Browser.SourceAction", target.DisplayName),
                Foreground = new SolidColorBrush(Color.FromRgb(111, 168, 255)),
                FontWeight = FontWeights.SemiBold
            };
            var detailText = new TextBlock
            {
                Text = selectedCount > 0
                    ? LocalizationService.Get("Browser.SourceInUse", selectedCount)
                    : target.HasExistingResult
                        ? LocalizationService.Get("Browser.SourceCandidate")
                        : LocalizationService.Get("Browser.SourceOpen"),
                Foreground = new SolidColorBrush(Color.FromRgb(184, 197, 214)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var header = new StackPanel();
            header.Children.Add(sourceText);
            header.Children.Add(detailText);
            var menuItem = new MenuItem
            {
                Header = header,
                Tag = target,
                Style = (Style)FindResource("CandidateMenuItem"),
                ToolTip = target.InitialUrl
            };
            menuItem.Click += async (_, _) => await OpenBrowserAsync(target);
            menu.Items.Add(menuItem);
        }

        BrowserImportButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

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
            var transactionProgress = new Progress<FileTransactionProgress>(update =>
                SetStatus(GetLocalizedTransactionProgress(update), null));
            var result = await _fileOrganizationService.ExecuteAsync(
                plan,
                _activeJob.Metadata,
                allowOverwrite,
                CurrentOperationToken,
                transactionProgress);
            CancelOperationButton.IsEnabled = false;
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
            await SelectVideoCoreAsync(result.VideoPath);
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
            SetStatus(LocalizationService.Get("Status.SaveComplete", outputSummary, fanartNote, moveNote), true);
        });
    }

    private void SourceSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized)
        {
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

        // Both workspace modes share three tiers. Reserve the gear area in every source
        // mode so switching sources never moves controls or changes the row count.
        var wrapped = SearchToolbar.ActualWidth < 540;
        _compactSearchToolbar = SearchToolbar.ActualWidth < 650;
        var showAutomaticSourceLabel = SearchToolbar.ActualWidth >= 720;
        RefreshManualWebLookupUi();
        var selectedSourceLabel = _compactSearchToolbar && GetSelectedSourceMode() == MetadataSearchSourceModes.LibreDmm
            ? "LibreDMM"
            : (SourceComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
        if (!Equals(SourceComboBox.Resources["SearchSourceSelectionLabel"], selectedSourceLabel))
            SourceComboBox.Resources["SearchSourceSelectionLabel"] = selectedSourceLabel;
        var layoutKey = $"{wrapped}:{_compactSearchToolbar}:{showAutomaticSourceLabel}";
        if (string.Equals(_searchToolbarLayoutKey, layoutKey, StringComparison.Ordinal))
        {
            return;
        }

        _searchToolbarLayoutKey = layoutKey;
        SearchToolbarLayout.ColumnDefinitions.Clear();
        static void Place(UIElement element, int row, int column, int columnSpan = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
        }

        void AddColumn(GridLength width, double minWidth = 0) =>
            SearchToolbarLayout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width,
                MinWidth = minWidth
            });

        if (wrapped)
        {
            AddColumn(GridLength.Auto);
            AddColumn(new GridLength(8));
            AddColumn(new GridLength(1, GridUnitType.Star), 100);
            AddColumn(new GridLength(8));
            AddColumn(GridLength.Auto);

            SearchToolbarLayout.RowDefinitions[0].Height = GridLength.Auto;
            SearchToolbarLayout.RowDefinitions[1].Height = new GridLength(8);
            SearchToolbarLayout.RowDefinitions[2].Height = GridLength.Auto;
            AutomaticSourceText.Visibility = Visibility.Collapsed;
            IdTextBox.Width = double.NaN;
            IdTextBox.MinWidth = 70;
            SourceComboBox.Width = double.NaN;
            SourceComboBox.MinWidth = 100;
            SourceSelectorHost.MinWidth = 100;

            Place(MovieIdTextBlock, 0, 0);
            Place(IdTextBox, 0, 2);
            Place(SearchButton, 0, 4);
            Place(BrowserImportButton, 2, 0);
            Place(AutomaticSourceText, 2, 2);
            Place(SourceSelectorHost, 2, 2, 3);
            Place(BatchSourceHint, 3, 0, 5);
            return;
        }

        AddColumn(GridLength.Auto);
        AddColumn(new GridLength(_compactSearchToolbar ? 6 : 10));
        AddColumn(new GridLength(100));
        AddColumn(new GridLength(_compactSearchToolbar ? 6 : 8));
        AddColumn(GridLength.Auto);
        AddColumn(new GridLength(_compactSearchToolbar ? 6 : 8));
        AddColumn(GridLength.Auto);
        AddColumn(new GridLength(_compactSearchToolbar ? 8 : 12));
        AddColumn(showAutomaticSourceLabel ? GridLength.Auto : new GridLength(0));
        AddColumn(showAutomaticSourceLabel ? new GridLength(7) : new GridLength(0));
        AddColumn(new GridLength(1, GridUnitType.Star), 120);

        SearchToolbarLayout.RowDefinitions[0].Height = GridLength.Auto;
        SearchToolbarLayout.RowDefinitions[1].Height = new GridLength(0);
        SearchToolbarLayout.RowDefinitions[2].Height = GridLength.Auto;
        AutomaticSourceText.Visibility = showAutomaticSourceLabel ? Visibility.Visible : Visibility.Collapsed;
        IdTextBox.Width = 100;
        IdTextBox.MinWidth = 100;
        SourceComboBox.Width = double.NaN;
        SourceComboBox.MinWidth = 120;
        SourceSelectorHost.MinWidth = 120;

        Place(MovieIdTextBlock, 0, 0);
        Place(IdTextBox, 0, 2);
        Place(SearchButton, 0, 4);
        Place(BrowserImportButton, 0, 6);
        Place(AutomaticSourceText, 0, 8);
        Place(SourceSelectorHost, 0, 10);
        Place(BatchSourceHint, 3, 0, 11);
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

        // Individually safe plans may still overwrite or retire another movie's files.
        // Remove every affected plan before the user can accept or skip the preview.
        var batchConflicts = BatchSavePathConflicts.Find(previewItems);
        foreach (var conflict in batchConflicts)
        {
            previewIssues.Add(new BatchSavePreviewIssue(conflict.Key,
                LocalizationService.Get("Error.BatchOutputConflict", string.Join(Environment.NewLine, conflict.Value))));
        }
        previewItems.RemoveAll(item => batchConflicts.ContainsKey(item.Job));

        var previewAllowOverwrite = false;
        IReadOnlyList<BatchSavePreviewItem> itemsToSave = previewItems;
        if (ShouldShowBatchSavePreview(
                SkipSavePreviewCheckBox.IsChecked == true,
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
            var result = await BatchSaveCoordinator.ExecuteAsync(
                preparedItems,
                async (item, cancellationToken) =>
                {
                    var progress = new Progress<FileTransactionProgress>(update =>
                        SetStatus(
                            LocalizationService.Get(
                                "Status.BatchSaveProgress",
                                item.Job.Metadata.Id,
                                GetLocalizedTransactionProgress(update)),
                            null));
                    return await _fileOrganizationService.ExecuteAsync(
                        item.Plan,
                        item.Job.Metadata,
                        item.AllowOverwrite,
                        cancellationToken,
                        progress);
                },
                CurrentOperationToken,
                async item =>
                {
                    if (item.Status is BatchSaveItemStatus.Completed)
                    {
                        await RemoveCompletedQueueJobsAsync([item.Item.Job]);
                    }
                });

            // The batch coordinator reports recovery failures as results, not exceptions.
            // Keep the window open even if a close request canceled the batch.
            if (result.FailedCount > 0)
            {
                _closeRequested = false;
            }

            if (_activeJob.VideoPath is not null)
            {
                FileNameText.Text = _activeJob.FileName;
                FilePathText.Text = _activeJob.VideoPath;
                if (!RestoreCachedPreview(_activeJob))
                {
                    ClearPosterPreview();
                    ClearFanartPreview();
                    await LoadSelectedArtworkPreviewAsync();
                    CacheActivePreview();
                }

                RefreshTargetLocationPreview();
            }

            AppLog.Info(
                $"批量保存结束 requested={preparedItems.Length} completed={result.CompletedCount} " +
                $"failed={result.FailedCount} canceled={result.CanceledCount} notStarted={result.NotStartedCount}");
            SetStatus(
                LocalizationService.Get(
                    "Status.BatchSaveComplete",
                    result.CompletedCount,
                    result.FailedCount,
                    result.CanceledCount,
                    result.NotStartedCount),
                result.FailedCount == 0 && result.CanceledCount == 0);
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
            if (requestedSets.Count > 1) SetWorkspaceMode(WorkspaceMode.Batch);
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

            RefreshQueueUi();
            if (firstAdded is not null)
            {
                await ActivateMovieJobAsync(firstAdded);
                focusIdAfterImport = _workspaceMode is WorkspaceMode.Single &&
                                     requestedSets.Count == 1 &&
                                     string.IsNullOrWhiteSpace(firstAdded.Metadata.Id);
            }

            SetStatus(
                LocalizationService.Get("Status.AddedMovies", addedJobCount, duplicateCount),
                failedCount == 0);
        });

        if (focusIdAfterImport)
        {
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
        await RunBusyAsync(
            LocalizationService.Get("Status.InspectingLocal"),
            () => ActivateMovieJobAsync(selectedJob));
        if (restoreQueueFocus)
        {
            RestoreQueueFocus(selectedJob, focusVersion);
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
                _workspaceMode is not WorkspaceMode.Batch || _busy ||
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

    private void SetBatchSelection(bool isSelected)
    {
        if (_busy)
        {
            return;
        }

        foreach (var job in _movieQueue)
        {
            job.IsSelectedForBatch = isSelected;
        }

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

    private async Task ActivateMovieJobAsync(MovieJob job, bool loadArtworkPreview = true)
    {
        ArgumentNullException.ThrowIfNull(job);
        var previous = _activeJob;
        var switchedMovie = !ReferenceEquals(previous, job);
        if (switchedMovie)
        {
            CacheActivePreview();
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
        if (job.VideoPath is null)
        {
            _artworkPreviewDeferred = false;
            FileNameText.Text = LocalizationService.Get("Main.NoVideo");
            FilePathText.Text = LocalizationService.Get("Main.VideoStaysHere");
            ClearPosterPreview();
            ClearFanartPreview();
            SetStatus(LocalizationService.Get("Main.StatusReadyInput"), true);
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
                    await LoadSelectedArtworkPreviewAsync();
                    CacheActivePreview();
                }
            }

            if (_jobLoadResults.TryGetValue(job, out var loadResult))
            {
                PresentMovieJobLoadStatus(job, loadResult);
            }
        }

        if (!ReferenceEquals(previous, job) && !_movieQueue.Contains(previous))
        {
            _jobLoadResults.Remove(previous);
            _jobPreviews.Remove(previous);
            DetachMovieJob(previous);
            previous.Dispose();
        }

        _queueRefresh.Flush();
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

        var showQueue = _workspaceMode is WorkspaceMode.Batch;
        if (refreshView) _movieQueueView.Refresh();
        QueuePanel.Visibility = showQueue ? Visibility.Visible : Visibility.Collapsed;
        var sourceMode = GetSelectedSourceMode();
        var canSearch = MetadataSearchSourceModes.AutomaticSources(sourceMode).Count > 0;
        var usesR18 = MetadataSearchSourceModes.AutomaticSources(sourceMode)
            .Contains(MetadataSearchSourceModes.R18Dev);
        BatchSourceHint.Visibility = showQueue && canSearch ? Visibility.Visible : Visibility.Collapsed;
        BatchSourceHint.Text = LocalizationService.Get(usesR18
            ? "Main.BatchSource.R18Hint"
            : "Main.BatchSource.DmmHint");
        BatchSourceHint.Foreground = (Brush)FindResource(usesR18 ? "BatchSourceWarningBrush" : "MutedBrush");
        var selectedCount = 0;
        var issueCount = 0;
        var hasSearchableJob = false;
        foreach (var job in _movieQueue)
        {
            if (job.IsSelectedForBatch) selectedCount++;
            if (HasQueueIssue(job)) issueCount++;
            hasSearchableJob = hasSearchableJob || job.CanBatchSearch;
        }
        SearchQueueButton.IsEnabled = !_busy && canSearch && hasSearchableJob;
        SearchQueueButton.ToolTip = canSearch
            ? LocalizationService.Get("Main.SearchQueueSourceTooltip", GetAutomaticSourceDisplayName(sourceMode))
            : LocalizationService.Get("Status.ManualMode");
        SaveSelectedButton.IsEnabled = !_busy && selectedCount > 0;
        SelectAllQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0 && selectedCount < _movieQueue.Count;
        SelectNoneQueueButton.IsEnabled = !_busy && selectedCount > 0;
        RemoveSelectedQueueButton.IsEnabled = !_busy && selectedCount > 0;
        ClearQueueButton.IsEnabled = !_busy && _movieQueue.Count > 0;
        SaveSelectedButton.Content = LocalizationService.Get(
            selectedCount > 0 && selectedCount == _movieQueue.Count
                ? "Main.SaveAllCount"
                : "Main.SaveSelectedCount",
            selectedCount);
        QueueSplitter.Visibility = showQueue ? Visibility.Visible : Visibility.Collapsed;
        QueueCountText.Text = LocalizationService.Get(
            "Main.QueueSummary",
            selectedCount,
            _movieQueue.Count,
            issueCount);
        var visibleCount = MovieQueueList.Items.Count;
        PreviousQueueButton.IsEnabled = !_busy && visibleCount > 1;
        NextQueueButton.IsEnabled = !_busy && visibleCount > 1;
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

        if (result.MovieFileSets.Count == 0)
        {
            ShowError(LocalizationService.Get("Error.NoSupportedDroppedFiles"));
            return;
        }

        if (result.MovieFileSets.Count == 1 && result.Diagnostics.Count == 0)
        {
            AppLog.Info(
                $"智能导入识别单一影片 inputs={paths.Count} parts={result.MovieFileSets[0].Parts.Count}");
            await AddMovieFilesAsync(result.MovieFileSets[0].VideoPaths);
            return;
        }

        SetWorkspaceMode(WorkspaceMode.Batch);
        var discovery = new MovieDiscoveryWindow(
            paths,
            _movieQueue.SelectMany(job => job.VideoPaths),
            includeSubdirectories: true,
            initialResult: result)
        {
            Owner = this
        };
        if (discovery.ShowDialog() == true)
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

    private void PreviousQueue_Click(object sender, RoutedEventArgs e) => NavigateQueue(-1);

    private void NextQueue_Click(object sender, RoutedEventArgs e) => NavigateQueue(1);

    private void NavigateQueue(int offset)
    {
        if (_busy)
        {
            return;
        }

        var visibleJobs = MovieQueueList.Items.Cast<MovieJob>().ToArray();
        if (visibleJobs.Length < 2)
        {
            return;
        }

        var currentIndex = Array.IndexOf(visibleJobs, _activeJob);
        var nextIndex = currentIndex < 0
            ? offset > 0 ? 0 : visibleJobs.Length - 1
            : (currentIndex + offset + visibleJobs.Length) % visibleJobs.Length;
        MovieQueueList.SelectedItem = visibleJobs[nextIndex];
        MovieQueueList.ScrollIntoView(visibleJobs[nextIndex]);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down && Keyboard.Modifiers == ModifierKeys.None &&
            _workspaceMode is WorkspaceMode.Batch && !HasOpenTransientSurface())
        {
            if (MovieQueueList.IsKeyboardFocusWithin || _queueNavigationKey is not null ||
                _pendingQueueFocusVersion == _queueFocusVersion)
            {
                // Disabling the queue during preview loading can move focus to a ComboBox.
                // Own this gesture at window level BEFORE selection starts loading. Busy
                // repeats are consumed, never queued or passed to the temporary focus target.
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

        if (_workspaceMode is WorkspaceMode.Batch)
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
        LanguageComboBox.IsDropDownOpen ||
        QueueFilterComboBox.IsDropDownOpen ||
        SourceComboBox.IsDropDownOpen ||
        TargetModeComboBox.IsDropDownOpen ||
        BrowserImportButton.ContextMenu?.IsOpen == true ||
        RecentRootsButton.ContextMenu?.IsOpen == true ||
        ArtworkSourceButton.ContextMenu?.IsOpen == true ||
        GetSourceBadgeControls().Any(item => item.Badge.ContextMenu?.IsOpen == true);

    private void IdTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || Keyboard.Modifiers != ModifierKeys.None || _busy || !SearchButton.IsEnabled)
        {
            return;
        }

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
                sourceMode: sourceMode);
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
        SetStatus(LocalizationService.Get("Status.BatchSourcesComplete", result.SucceededCount, result.PartialCount,
                result.FailedCount, result.CanceledCount, result.NotStartedCount),
            result.PartialCount == 0 && result.FailedCount == 0 && result.CanceledCount == 0 && result.NotStartedCount == 0);
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
        }
    }

    private void ToggleSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _saveSettingsExpanded = !_saveSettingsExpanded;
        RefreshSaveSettingsPresentation();
    }

    private void RefreshSaveSettingsPresentation()
    {
        if (!_uiInitialized || SaveSettingsOptionsPanel is null || SaveSettingsSummaryText is null)
        {
            return;
        }

        var outputCount = new[]
        {
            WriteNfoCheckBox.IsChecked,
            DownloadPosterCheckBox.IsChecked,
            DownloadFanartCheckBox.IsChecked,
            DownloadExtrafanartCheckBox.IsChecked
        }.Count(value => value == true);
        var outputSummary = outputCount == 0
            ? LocalizationService.Get("Main.SaveSettings.NoOutput")
            : LocalizationService.Get("Main.SaveSettings.OutputCount", outputCount);
        var titleSummary = IncludeIdInTitleCheckBox.IsChecked == true
            ? LocalizationService.Get("Main.SaveSettings.TitleWithId")
            : LocalizationService.Get("Main.SaveSettings.TitleWithoutId");
        var previewSummary = SkipSavePreviewCheckBox.IsChecked == true
            ? LocalizationService.Get("Main.SaveSettings.SkipPreview")
            : LocalizationService.Get("Main.SaveSettings.ShowPreview");

        SaveSettingsTitleText.Text = LocalizationService.Get(
            _workspaceMode is WorkspaceMode.Batch
                ? "Main.SaveSettings.Batch"
                : "Main.SaveSettings.Single");
        SaveSettingsSummaryText.Text = LocalizationService.Get(
            "Main.SaveSettings.Summary",
            outputSummary,
            titleSummary,
            previewSummary);
        SaveSettingsOptionsPanel.Visibility = _saveSettingsExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ((RotateTransform)SaveSettingsChevronPath.RenderTransform).Angle =
            _saveSettingsExpanded ? 90 : 0;
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
            TargetPathHintText.Foreground = new SolidColorBrush(Color.FromRgb(255, 157, 166));
            RefreshSaveAvailability();
            return;
        }

        if (_activeJob.VideoPath is null)
        {
            TargetPathHintText.Text = customMode && string.IsNullOrWhiteSpace(CustomRootTextBox.Text)
                ? LocalizationService.Get("Main.TargetHintCustom")
                : LocalizationService.Get("Main.TargetHint");
            TargetPathHintText.Foreground = new SolidColorBrush(Color.FromRgb(147, 164, 184));
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
                TargetPathHintText.Foreground = new SolidColorBrush(
                    fastCopy
                        ? Color.FromRgb(255, 209, 138)
                        : Color.FromRgb(141, 184, 255));
            }
            else
            {
                TargetPathHintText.Foreground = new SolidColorBrush(Color.FromRgb(114, 227, 166));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            _targetConfigurationError = exception.Message;
            TargetPathHintText.Text = exception.Message;
            TargetPathHintText.Foreground = new SolidColorBrush(Color.FromRgb(255, 157, 166));
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

        SaveButton.IsEnabled = !_busy && !_activeJob.LocalNfoSaveBlocked && _targetConfigurationError is null;
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

        var result = await MovieJobLoader.LoadAsync(_activeJob, path, CurrentOperationToken);
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
    }

    private async Task OpenBrowserAsync(BrowserImportTarget target)
    {
        if (_busy)
        {
            return;
        }

        var browserTarget = target;
        if (BrowserImportRouting.RequiresProviderResolution(target))
        {
            var resolutionCompleted = false;
            await RunBusyAsync(
                LocalizationService.Get("Status.LocatingBrowserPage", target.DisplayName),
                async () =>
                {
                    try
                    {
                        browserTarget = await BrowserImportRouting.ResolveWithProviderAsync(
                            target,
                            _r18DevClient,
                            CurrentOperationToken);
                        AppLog.Info(
                            $"手动网页查询已解析真实详情页 source={target.SourceName} " +
                            $"id={target.RequestedMovieId} url={browserTarget.InitialUrl}");
                    }
                    catch (OperationCanceledException) when (CurrentOperationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        AppLog.Warning(
                            $"手动网页查询未能预先解析详情页，回退网站手动查询 " +
                            $"source={target.SourceName} id={target.RequestedMovieId}",
                            exception);
                    }

                    resolutionCompleted = true;
                });
            if (!resolutionCompleted)
            {
                return;
            }
        }

        var browser = new BrowserWindow(browserTarget) { Owner = this };
        if (browser.ShowDialog() == true)
        {
            await RunBusyAsync(LocalizationService.Get("Status.ReadingBrowser"), async () =>
            {
                var result = await ImportBrowserResultAsync(browserTarget, browser);
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

    private void RefreshManualWebLookupUi()
    {
        if (!_uiInitialized || BrowserImportButton is null)
        {
            return;
        }

        BrowserImportButton.Content = LocalizationService.Get(_compactSearchToolbar
            ? "Main.BrowserImportChooseCompact"
            : "Main.BrowserImportChoose");
        BrowserImportButton.ToolTip = LocalizationService.Get("Main.BrowserImportChooseTooltip");
    }

    private int CountSelectedSourceItems(string sourceName)
    {
        var selectedFieldCount = Enum.GetValues<MetadataField>().Count(field =>
            string.Equals(
                _activeJob.MetadataReview.GetSelectedCandidate(field)?.Source.Name,
                sourceName,
                StringComparison.OrdinalIgnoreCase));
        var artworkSelected = string.Equals(
            _activeJob.ArtworkReview.SelectedCandidate?.Source.Name,
            sourceName,
            StringComparison.OrdinalIgnoreCase);
        return selectedFieldCount + (artworkSelected ? 1 : 0);
    }

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

        var batchCount = _workspaceMode == WorkspaceMode.Batch
            ? _movieQueue.Count(job => job.IsSelectedForBatch && CanRetrySources(job)) : 0;
        var canRetry = !_busy && (_workspaceMode == WorkspaceMode.Batch ? batchCount > 0 : CanRetrySources(_activeJob));
        RetryFailedSourcesButton.Content = _workspaceMode == WorkspaceMode.Batch
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
        var candidateSourceName = candidate is null
            ? string.Empty
            : GetCandidateSourceDisplayName(candidate.Source);
        badge.Content = candidate is null
            ? string.Empty
            : candidates.Count > 1
                ? $"{candidateSourceName} ▾"
                : candidateSourceName;
        badge.Visibility = candidate is null ? Visibility.Collapsed : Visibility.Visible;
        badge.IsEnabled = candidates.Count > 1;
        badge.ToolTip = candidate is null
            ? null
            : candidates.Count > 1
                ? LocalizationService.Get(
                    "Artwork.SourceFieldTooltip",
                    GetFieldDisplayName(field),
                    candidates.Count)
                : string.IsNullOrWhiteSpace(candidate.Source.Url)
                    ? LocalizationService.Get("Artwork.Source", candidateSourceName)
                    : $"{LocalizationService.Get("Artwork.Source", candidateSourceName)}\n{candidate.Source.Url}";
    }

    private void SourceBadge_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button badge ||
            !Enum.TryParse<MetadataField>(badge.Tag?.ToString(), out var field))
        {
            return;
        }

        var candidates = _activeJob.MetadataReview.GetCandidates(field);
        if (candidates.Count <= 1)
        {
            return;
        }

        var selected = _activeJob.MetadataReview.GetSelectedCandidate(field);
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
                path,
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

    private async Task<(bool Poster, bool Fanart)> LoadSelectedArtworkPreviewAsync()
    {
        var request = new ArtworkPreviewRequest(_activeJob, _activeJob.Metadata,
            _activeJob.CoverResolutionRevision, CurrentOperationToken);
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
            if (ReferenceEquals(request.Job, _activeJob)) RefreshArtworkPreviewStates();
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
                var posterBytes = PosterImageProcessor.CreatePosterJpeg(imageBytes);
                PosterImage.Source = PosterBitmapFactory.CreateFrozen(posterBytes, ArtworkPreviewLoader.MainPreviewWidth);
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
                var dimensions = PosterImageProcessor.GetDimensions(imageBytes);
                token.ThrowIfCancellationRequested();
                if (!CanApplyArtworkPreview(request)) return false;
                FanartImage.Source = PosterBitmapFactory.CreateFrozen(PosterImageProcessor.CreateFanartJpeg(imageBytes), ArtworkPreviewLoader.MainPreviewWidth);
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
                localPath,
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
            var image = await ArtworkLocationHelper.ReadLocalImageWithDimensionsAsync(path, token);
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
        FanartHintText.Foreground = _activeJob.HasLowResolutionCover
            ? Brushes.Orange : (Brush)FindResource("MutedBrush");
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
            FileTransactionStage.CopyingMovie when update.TotalBytes > 0 =>
                LocalizationService.Get("Progress.CopyingPercent", update.Percentage),
            FileTransactionStage.CopyingMovie => LocalizationService.Get("Progress.Copying"),
            FileTransactionStage.VerifyingMovie when update.TotalBytes > 0 =>
                LocalizationService.Get("Progress.VerifyingPercent", update.Percentage),
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
        ScanFolderButton.IsEnabled = false;
        SingleModeButton.IsEnabled = false;
        BatchModeButton.IsEnabled = false;
        SearchQueueButton.IsEnabled = false;
        SaveSelectedButton.IsEnabled = false;
        SelectAllQueueButton.IsEnabled = false;
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
        Mouse.OverrideCursor = Cursors.Wait;
        SetStatus(message, null);

        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            AppLog.Info("当前操作已取消，文件事务已执行安全恢复");
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                SetStatus(LocalizationService.Get("Status.OperationCanceled"), false);
            }
        }
        catch (Exception exception)
        {
            // Keep recovery errors visible; do not close on a failed/incomplete rollback.
            _closeRequested = false;
            AppLog.Error(message, exception);
            ShowError(GetLocalizedExceptionMessage(exception));
        }
        finally
        {
            Mouse.OverrideCursor = null;
            SearchButton.IsEnabled = true;
            ChooseVideoButton.IsEnabled = true;
            ScanFolderButton.IsEnabled = true;
            SingleModeButton.IsEnabled = true;
            BatchModeButton.IsEnabled = true;
            MovieQueueList.IsEnabled = true;
            MetadataPanel.IsEnabled = true;
            ArtworkPanel.IsEnabled = true;
            CancelOperationButton.Visibility = Visibility.Collapsed;
            _activeOperationCancellation = null;
            _busy = false;
            if (operationCancellation.IsCancellationRequested) _coverResolutionMonitor.CancelAll();
            _coverResolutionMonitor.Pause(false);
            RefreshCoverCheckButton();
            RefreshSaveAvailability();
            RefreshArtworkSourceBadge();
            RefreshQueueUi();
            RefreshWorkspaceModeUi();
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

    private void SetStatus(string message, bool? success)
    {
        StatusText.Text = message;
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            _closeRequested = true;
            CancelOperation_Click(this, new RoutedEventArgs());
            return;
        }
        AppLog.Info("JavMetaLite 关闭");
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
