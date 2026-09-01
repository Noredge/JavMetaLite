using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using Microsoft.Win32;

namespace JavMetaLite.App;

public partial class MainWindow : Window
{
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
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private MovieMetadata _metadata = new();
    private MetadataReviewSession? _metadataReview;
    private ArtworkCoverReviewSession? _artworkCoverReview;
    private LocalMetadataBundle? _localMetadataBundle;
    private MovieMetadata? _localSourceMetadata;
    private ArtworkCoverCandidate? _localArtworkCandidate;
    private ArtworkCoverCandidate? _manualArtworkCandidate;
    private IReadOnlyList<MovieMetadata> _currentSourceResults = [];
    private string? _preferredArtworkSourceName;
    private string? _videoPath;
    private string? _lastValidCustomRootDirectory;
    private string? _customRootAvailabilityCheckPath;
    private readonly List<string> _recentCustomRootDirectories = [];
    private string? _targetConfigurationError;
    private bool _localNfoSaveBlocked;
    private bool _busy;
    private bool _uiInitialized;
    private bool _preferencesLoaded;
    private bool _preferencesCanOverwrite = true;

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
        WindowVisualTheme.ApplyDarkTitleBar(this);
        SelectLanguageItem(LocalizationService.CurrentLanguageCode);
        _uiInitialized = true;
        ApplyMetadata(_metadata, []);
        RefreshTargetLocationUi();
        AppLog.Info("JavMetaLite v1.1.0 启动");
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
                $"已恢复保存偏好 target={result.Preferences.TargetMode} " +
                $"rename={result.Preferences.RenameVideo} directOverwrite={result.Preferences.DirectSaveOverwrite} " +
                $"crossVolumeVerification={result.Preferences.CrossVolumeVerification} " +
                $"customRoot={result.Preferences.CustomRootDirectory}");
            SetStatus(
                result.Preferences.DirectSaveOverwrite
                    ? LocalizationService.Get("Status.PreferencesDirect")
                    : LocalizationService.Get("Status.PreferencesRestored"),
                !result.Preferences.DirectSaveOverwrite);
        }
    }

    private void ApplyPreferences(AppPreferences preferences)
    {
        ApplyLanguagePreference(preferences.UiLanguage);
        DirectSaveOverwriteCheckBox.IsChecked = preferences.DirectSaveOverwrite;
        SkipCrossVolumeVerificationCheckBox.IsChecked =
            preferences.CrossVolumeVerification is CrossVolumeVerificationMode.FileSizeOnly;
        RememberPreferencesCheckBox.IsChecked = preferences.RememberSavePreferences;
        WriteNfoCheckBox.IsChecked = preferences.WriteNfo;
        DownloadPosterCheckBox.IsChecked = preferences.DownloadPoster;
        DownloadFanartCheckBox.IsChecked = preferences.DownloadFanart;
        DownloadExtrafanartCheckBox.IsChecked = preferences.DownloadExtrafanart;
        RenameVideoCheckBox.IsChecked = preferences.RenameVideo;
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
    }

    private AppPreferences CapturePreferences()
    {
        return new AppPreferences
        {
            UiLanguage = LocalizationService.CurrentLanguageCode,
            RememberSavePreferences = RememberPreferencesCheckBox.IsChecked == true,
            DirectSaveOverwrite = DirectSaveOverwriteCheckBox.IsChecked == true,
            CrossVolumeVerification = GetCrossVolumeVerificationMode(),
            TargetMode = GetSelectedTargetMode(),
            CustomRootDirectory = string.IsNullOrWhiteSpace(CustomRootTextBox.Text)
                ? _lastValidCustomRootDirectory
                : CustomRootTextBox.Text,
            RecentCustomRootDirectories = _recentCustomRootDirectories.ToArray(),
            RenameVideo = RenameVideoCheckBox.IsChecked == true,
            WriteNfo = WriteNfoCheckBox.IsChecked == true,
            DownloadPoster = DownloadPosterCheckBox.IsChecked == true,
            DownloadFanart = DownloadFanartCheckBox.IsChecked == true,
            DownloadExtrafanart = DownloadExtrafanartCheckBox.IsChecked == true
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
                    $"directOverwrite={preferences.DirectSaveOverwrite} path={_preferencesStore.SettingsPath}");
            }
            else
            {
                AppLog.Info($"未启用保存偏好记忆，仅保留界面语言 language={preferences.UiLanguage}");
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

        RefreshRecentRootsButton();
        RefreshTargetLocationUi();
        RefreshSourceBadges();
        RefreshArtworkSourceBadge();
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("Dialog.ChooseVideo"),
            Filter = LocalizationService.Get("Dialog.VideoFilter"),
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            await SelectVideoAsync(dialog.FileName);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetSingleDroppedPath(e.Data, out var path) &&
                    (VideoFileSupport.IsSupportedExistingFile(path) || Directory.Exists(path))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetSingleDroppedPath(e.Data, out var path))
        {
            return;
        }

        try
        {
            var resolution = VideoFileSupport.ResolveInputPath(path);
            if (resolution.Success)
            {
                if (Directory.Exists(path))
                {
                    AppLog.Info($"从拖入番号文件夹解析影片 folder={path} video={resolution.VideoPath}");
                }
                await SelectVideoAsync(resolution.VideoPath!);
            }
            else
            {
                ShowError(LocalizationService.Get(resolution.Status switch
                {
                    VideoInputPathStatus.FolderHasNoVideo => "Error.FolderHasNoVideo",
                    VideoInputPathStatus.FolderHasMultipleVideos => "Error.FolderHasMultipleVideos",
                    _ => "Error.UnsupportedVideo"
                }));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"无法读取拖入的影片文件夹 path={path}", exception);
            ShowError(LocalizationService.Get("Error.ReadVideoFolder", exception.Message));
        }
        finally
        {
            e.Handled = true;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_metadata.Id))
        {
            ShowError(LocalizationService.Get("Error.EnterId"));
            return;
        }

        var source = (SourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        if (source == "manual")
        {
            SetStatus(LocalizationService.Get("Status.ManualMode"), true);
            return;
        }

        string? browserFallbackUrl = null;
        var busyMessage = source == "auto"
            ? LocalizationService.Get("Status.SearchingMulti")
            : LocalizationService.Get("Status.SearchingSingle");
        await RunBusyAsync(busyMessage, async () =>
        {
            try
            {
                var outcome = await SearchFromSelectedSourceAsync(source, _metadata.Id);
                var result = ApplyOnlineSources(outcome.Metadata, outcome.Sources);
                var successfulSources = string.Join(
                    "+",
                    outcome.Sources.Select(GetSourceDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                var failedSources = string.Join(
                    "+",
                    outcome.Attempts.Where(attempt => !attempt.Success).Select(attempt => attempt.SourceDisplayName));
                AppLog.Info(
                    $"metadata 搜索成功 sources={successfulSources} failedSources={failedSources} id={result.Id} " +
                    $"contentId={result.ContentId} screenshots={result.ScreenshotUrls.Count} " +
                    $"reviewSources={outcome.Sources.Count} onlineDefault=true localCandidate={_localSourceMetadata is not null}");
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
                var localPrefix = _localSourceMetadata is null
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
                browserFallbackUrl = exception.Url;
                SetStatus(LocalizationService.Get("Status.BrowserVerification"), false);
            }
        });

        if (browserFallbackUrl is not null)
        {
            OpenBrowser(browserFallbackUrl);
        }
    }

    private async Task<MetadataSearchOutcome> SearchFromSelectedSourceAsync(string source, string id)
    {
        AppLog.Info($"开始搜索 metadata source={source} id={id}");
        try
        {
            if (source == "libredmm")
            {
                return MetadataSearchOutcome.FromSingleAttempt(
                    await MetadataSearchCoordinator.SearchSingleAsync(
                        id,
                        _libreDmmClient,
                        CurrentOperationToken));
            }

            if (source == "r18dev")
            {
                return MetadataSearchOutcome.FromSingleAttempt(
                    await MetadataSearchCoordinator.SearchSingleAsync(
                        id,
                        _r18DevClient,
                        CurrentOperationToken));
            }

            if (source == "javlibrary")
            {
                return MetadataSearchOutcome.FromSingleAttempt(
                    await MetadataSearchCoordinator.SearchSingleAsync(
                        id,
                        _javLibraryClient,
                        CurrentOperationToken));
            }

            var multiSourceResult = await MetadataSearchCoordinator.SearchAllAsync(
                id,
                _libreDmmClient,
                _r18DevClient,
                CurrentOperationToken);
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
        var url = JavLibraryClient.BuildSearchUrl(_metadata.Id);
        OpenBrowser(url);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null)
        {
            ShowError(LocalizationService.Get("Error.SelectVideo"));
            return;
        }

        if (_localNfoSaveBlocked)
        {
            ShowError(LocalizationService.Get("Error.LocalNfoBlocked"));
            return;
        }

        var options = new SaveOptions(
            WriteNfoCheckBox.IsChecked == true,
            DownloadPosterCheckBox.IsChecked == true,
            DownloadFanartCheckBox.IsChecked == true,
            DownloadExtrafanartCheckBox.IsChecked == true,
            DirectSaveOverwriteCheckBox.IsChecked == true);

        var organizationOptions = GetOrganizationOptions();

        SavePlan plan;
        try
        {
            plan = FileOrganizationService.BuildPlan(
                _videoPath,
                _metadata,
                options,
                organizationOptions,
                new LocalSaveContext(
                    _localMetadataBundle,
                    _localArtworkCandidate,
                    _artworkCoverReview?.SelectedCandidate));
        }
        catch (Exception exception)
        {
            AppLog.Error("无法生成保存预览", exception);
            ShowError(exception.Message);
            return;
        }

        var allowOverwrite = options.OverwriteExisting;
        if (options.RequiresPreview)
        {
            var preview = new SavePreviewWindow(plan) { Owner = this };
            if (preview.ShowDialog() != true)
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

            AppLog.Info("用户启用直接保存并覆盖，跳过变更预览");
        }

        await RunBusyAsync(LocalizationService.Get("Status.Saving"), async () =>
        {
            var transactionProgress = new Progress<FileTransactionProgress>(update =>
                SetStatus(GetLocalizedTransactionProgress(update), null));
            var result = await _fileOrganizationService.ExecuteAsync(
                plan,
                _metadata,
                allowOverwrite,
                CurrentOperationToken,
                transactionProgress);
            CancelOperationButton.IsEnabled = false;
            _videoPath = result.VideoPath;
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
            var outputSummary = outputs.Count == 0
                ? plan.HasActualChanges
                    ? LocalizationService.Get("Status.SidecarsMigrated")
                    : LocalizationService.Get("Status.NoChanges")
                : string.Join(LocalizationService.Get("Common.ListSeparator"), outputs);
            SetStatus(LocalizationService.Get("Status.SaveComplete", outputSummary, fanartNote, moveNote), true);
        });
    }

    private Task SelectVideoAsync(string path) =>
        RunBusyAsync(LocalizationService.Get("Status.InspectingLocal"), () => SelectVideoCoreAsync(path));

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
        }
    }

    private void TargetOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshTargetLocationPreview();
        }
    }

    private void CustomRootText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_uiInitialized)
        {
            RefreshTargetLocationPreview();
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
        var sourceDirectory = _videoPath is null ? null : Path.GetDirectoryName(_videoPath);
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

        if (_videoPath is null)
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
            var pathPlan = OrganizationPathPlanner.Resolve(
                _videoPath,
                _metadata.Id,
                GetOrganizationOptions());
            if (pathPlan.UsesCustomRoot)
            {
                _lastValidCustomRootDirectory = pathPlan.TargetRootDirectory;
            }

            TargetPathHintText.Text = LocalizationService.Get("Main.FinalVideo", pathPlan.TargetVideoPath);
            if (pathPlan.RequiresVerifiedCopy)
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

        SaveButton.IsEnabled = !_busy && !_localNfoSaveBlocked && _targetConfigurationError is null;
        SaveButton.ToolTip = _localNfoSaveBlocked
            ? LocalizationService.Get("Status.LocalNfoUnsafe")
            : _targetConfigurationError is not null
                ? _targetConfigurationError
                : _localMetadataBundle is not null
                    ? _localMetadataBundle.HasUnknownXml
                        ? LocalizationService.Get("Status.NfoPreserveUnknown")
                        : LocalizationService.Get("Status.NfoManagedOnly")
                    : null;
    }

    private async Task SelectVideoCoreAsync(string path)
    {
        if (!VideoFileSupport.IsSupportedExistingFile(path))
        {
            ShowError(LocalizationService.Get("Error.UnsupportedVideo"));
            return;
        }

        _videoPath = path;
        _localMetadataBundle = null;
        _localSourceMetadata = null;
        _localArtworkCandidate = null;
        _manualArtworkCandidate = null;
        _preferredArtworkSourceName = null;
        _localNfoSaveBlocked = false;
        AppLog.Info($"选择影片 path={path}");
        FileNameText.Text = Path.GetFileName(path);
        FilePathText.Text = path;
        var id = MovieIdParser.TryExtract(path);
        ApplyMetadata(new MovieMetadata { Id = id ?? string.Empty }, []);
        ClearPosterPreview();
        ClearFanartPreview();

        LocalSidecarPaths sidecars;
        try
        {
            sidecars = LocalSidecarLocator.Locate(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"无法检查本地 sidecar path={path}", exception);
            SetStatus(LocalizationService.Get("Status.LocalCheckFailed", exception.Message), false);
            return;
        }

        string metadataStatus;
        var statusSuccess = !string.IsNullOrWhiteSpace(id);
        if (!sidecars.HasNfo)
        {
            metadataStatus = !string.IsNullOrWhiteSpace(id)
                ? LocalizationService.Get("Status.IdRecognized", id)
                : LocalizationService.Get("Status.NoLocalNfoOrId");
        }
        else
        {
            _localNfoSaveBlocked = true;
            try
            {
                var bundle = await NfoReader.ReadAsync(sidecars, CurrentOperationToken);
                var composition = LocalMetadataReviewComposer.CreateLocal(bundle.Metadata);
                var editable = composition.Metadata;
                if (string.IsNullOrWhiteSpace(editable.Id))
                {
                    editable.Id = id ?? string.Empty;
                }

                _localMetadataBundle = bundle;
                _localSourceMetadata = composition.Sources[0];
                _localNfoSaveBlocked = false;
                ApplyMetadata(editable, composition.Sources);
                AppLog.Info(
                    $"本地 NFO 载入成功 path={bundle.Sidecars.NfoPath} id={editable.Id} " +
                    $"diagnostics={bundle.Diagnostics.Count}");
                var diagnosticNote = bundle.Diagnostics.Count == 0
                    ? string.Empty
                    : $"；{string.Join("；", bundle.Diagnostics)}";
                metadataStatus = LocalizationService.Get(
                    "Status.LocalNfoLoaded",
                    bundle.Sidecars.NfoPath,
                    diagnosticNote);
                statusSuccess = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AppLog.Warning($"本地 NFO 读取失败 path={sidecars.NfoPath}", exception);
                metadataStatus = LocalizationService.Get(
                    "Status.LocalNfoFailed",
                    Path.GetFileName(sidecars.NfoPath),
                    exception.Message);
                statusSuccess = false;
            }
        }

        var artworkDiscovery = await DiscoverLocalArtworkAsync(sidecars);
        if (!string.IsNullOrWhiteSpace(artworkDiscovery.Summary))
        {
            metadataStatus += LocalizationService.Get("Common.DetailSeparator") + artworkDiscovery.Summary;
        }
        if (artworkDiscovery.HasErrors)
        {
            statusSuccess = false;
        }

        SetStatus(metadataStatus, statusSuccess);
    }

    private async Task<(string Summary, bool HasErrors)> DiscoverLocalArtworkAsync(LocalSidecarPaths sidecars)
    {
        var discovery = await LocalArtworkDiscovery.DiscoverAsync(
            sidecars,
            CurrentOperationToken);
        foreach (var diagnostic in discovery.Diagnostics)
        {
            AppLog.Warning(diagnostic);
        }

        _localArtworkCandidate = discovery.Candidate;
        if (_localArtworkCandidate is null)
        {
            RebuildArtworkReview();
            var invalidSummary = discovery.Diagnostics.Count == 0
                ? string.Empty
                : LocalizationService.Get("Status.InvalidLocalImages", discovery.Diagnostics.Count);
            return (invalidSummary, discovery.Diagnostics.Count > 0);
        }

        _preferredArtworkSourceName = _localArtworkCandidate.Source.Name;
        RebuildArtworkReview();
        var loaded = await LoadSelectedArtworkPreviewAsync();
        var availability = (_localArtworkCandidate.HasPoster, _localArtworkCandidate.HasFanart) switch
        {
            (true, true) => "poster + fanart",
            (true, false) => LocalizationService.Get("Status.OnlyPoster"),
            (false, true) => LocalizationService.Get("Status.OnlyFanart"),
            _ => LocalizationService.Get("Status.NoArtwork")
        };
        var diagnosticNote = discovery.Diagnostics.Count == 0
            ? string.Empty
            : LocalizationService.Get("Status.InvalidImagesSuffix", discovery.Diagnostics.Count);
        var previewNote = loaded.Poster == _localArtworkCandidate.HasPoster &&
                          loaded.Fanart == _localArtworkCandidate.HasFanart
            ? string.Empty
            : LocalizationService.Get("Status.PreviewIncomplete");
        return (LocalizationService.Get("Status.LocalImagesLoaded", availability, diagnosticNote, previewNote),
            discovery.Diagnostics.Count > 0 || previewNote.Length > 0);
    }

    private async void OpenBrowser(string url)
    {
        if (_busy)
        {
            return;
        }

        var browser = new BrowserWindow(url) { Owner = this };
        if (browser.ShowDialog() == true && !string.IsNullOrWhiteSpace(browser.PageHtml))
        {
            await RunBusyAsync(LocalizationService.Get("Status.ReadingBrowser"), async () =>
            {
                var result = await _javLibraryClient.ParseDetailPageAsync(
                    browser.PageHtml,
                    browser.PageUrl ?? url,
                    _metadata.Id,
                    CurrentOperationToken);
                ApplyOnlineSources(result, [result]);
                var artworkLoaded = await LoadSelectedArtworkPreviewAsync();
                var localNote = _localSourceMetadata is null
                    ? string.Empty
                    : LocalizationService.Get("Status.LocalCandidateSuffix");
                var loadedMessage = LocalizationService.Get("Status.BrowserLoaded", result.Id, localNote);
                SetStatus(
                    artworkLoaded.Poster
                        ? loadedMessage
                        : loadedMessage + LocalizationService.Get("Status.CoverPreviewMissing"),
                    true);
            });
        }
    }

    private MovieMetadata ApplyOnlineSources(
        MovieMetadata preferredOnlineMetadata,
        IReadOnlyList<MovieMetadata> onlineSources)
    {
        var retainedManualCandidates = CaptureManualCandidates();
        _preferredArtworkSourceName = MetadataCandidateSource
            .FromMetadata(preferredOnlineMetadata)
            .Name;
        if (_localSourceMetadata is null)
        {
            ApplyMetadataCore(preferredOnlineMetadata, onlineSources, retainedManualCandidates);
            return _metadata;
        }

        var localForMerge = LocalMetadataReviewComposer.CreateLocal(_localSourceMetadata).Metadata;
        localForMerge.Id = _metadata.Id;
        var composition = LocalMetadataReviewComposer.ComposeWithOnline(
            localForMerge,
            preferredOnlineMetadata,
            onlineSources);
        ApplyMetadataCore(composition.Metadata, composition.Sources, retainedManualCandidates);
        return _metadata;
    }

    private MetadataFieldCandidate[] CaptureManualCandidates() =>
        _metadataReview is null
            ? []
            : Enum.GetValues<MetadataField>()
                .SelectMany(field => _metadataReview.GetCandidates(field))
                .Where(candidate => candidate.Source.IsManual)
                .ToArray();

    private void ApplyMetadata(MovieMetadata result, IReadOnlyList<MovieMetadata> sourceResults) =>
        ApplyMetadataCore(result, sourceResults, []);

    private void ApplyMetadataCore(
        MovieMetadata result,
        IReadOnlyList<MovieMetadata> sourceResults,
        IReadOnlyList<MetadataFieldCandidate> retainedManualCandidates)
    {
        _metadata.PropertyChanged -= Metadata_PropertyChanged;
        if (_metadataReview is not null)
        {
            _metadataReview.SelectionChanged -= MetadataReview_SelectionChanged;
            _metadataReview.Dispose();
        }

        _metadata = result;
        _metadata.PropertyChanged += Metadata_PropertyChanged;
        DataContext = _metadata;
        _currentSourceResults = sourceResults.ToArray();
        _metadataReview = MetadataReviewSession.Create(result, _currentSourceResults.ToArray());
        _metadataReview.SelectionChanged += MetadataReview_SelectionChanged;
        foreach (var manualCandidate in retainedManualCandidates)
        {
            var selectedCandidate = _metadataReview.GetSelectedCandidate(manualCandidate.Field);
            _metadataReview.SetManualValue(manualCandidate.Field, manualCandidate.Value);
            if (selectedCandidate is not null)
            {
                _metadataReview.SelectCandidate(manualCandidate.Field, selectedCandidate.Source.Name);
            }
        }
        RebuildArtworkReview();
        RefreshSourceBadges();
        RefreshTargetLocationPreview();
    }

    private void Metadata_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MovieMetadata.Id))
        {
            RefreshTargetLocationPreview();
        }
    }

    private void RebuildArtworkReview()
    {
        var additionalCandidates = new[] { _localArtworkCandidate, _manualArtworkCandidate }
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
        _artworkCoverReview = ArtworkCoverReviewSession.CreateWithAdditionalCandidates(
            _metadata,
            additionalCandidates,
            _preferredArtworkSourceName,
            _currentSourceResults.ToArray());
        if (_artworkCoverReview.SelectedCandidate is not null)
        {
            _preferredArtworkSourceName = _artworkCoverReview.SelectedCandidate.Source.Name;
        }
        RefreshArtworkSourceBadge();
    }

    private void MetadataReview_SelectionChanged(object? sender, MetadataSelectionChangedEventArgs eventArgs) =>
        RefreshSourceBadge(eventArgs.Field);

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

        var candidate = _metadataReview?.GetSelectedCandidate(field);
        var candidates = _metadataReview?.GetCandidates(field) ?? [];
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
            !Enum.TryParse<MetadataField>(badge.Tag?.ToString(), out var field) ||
            _metadataReview is null)
        {
            return;
        }

        var candidates = _metadataReview.GetCandidates(field);
        if (candidates.Count <= 1)
        {
            return;
        }

        var selected = _metadataReview.GetSelectedCandidate(field);
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
        if (_metadataReview?.SelectCandidate(candidate.Field, candidate.Source.Name) != true)
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
        var candidate = _artworkCoverReview?.SelectedCandidate;
        var candidates = _artworkCoverReview?.Candidates ?? [];
        ArtworkSourceButton.Content = candidate is null
            ? LocalizationService.Get("Artwork.Select")
            : $"{GetCandidateSourceDisplayName(candidate.Source)} ▾";
        ArtworkSourceButton.Visibility = candidate is null && _videoPath is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArtworkSourceButton.IsEnabled = !_busy && (_videoPath is not null || candidate is not null);
        ArtworkSourceButton.ToolTip = candidate is null
            ? LocalizationService.Get("Artwork.SelectTooltip")
            : LocalizationService.Get("Artwork.SourceTooltip", candidates.Count);
    }

    private void ArtworkSourceButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_artworkCoverReview is null ||
            (_videoPath is null && _artworkCoverReview.Candidates.Count == 0))
        {
            return;
        }

        var selected = _artworkCoverReview.SelectedCandidate;
        var menu = new ContextMenu
        {
            PlacementTarget = ArtworkSourceButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Style = (Style)FindResource("CandidateContextMenu")
        };

        foreach (var candidate in _artworkCoverReview.Candidates)
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

        if (_videoPath is not null)
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
        if (_artworkCoverReview?.SelectSource(candidate.Source.Name) != true)
        {
            return;
        }

        _preferredArtworkSourceName = candidate.Source.Name;
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
        if (_videoPath is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("Dialog.ChooseCover"),
            Filter = LocalizationService.Get("Dialog.ImageFilter"),
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.GetDirectoryName(_videoPath)
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
            _manualArtworkCandidate = ArtworkCoverCandidate.CreateCompleteCover(
                new MetadataCandidateSource(
                    "manual-cover",
                    LocalizationService.Get("Artwork.ManualSource"),
                    Path.GetFullPath(path)),
                path);
            _preferredArtworkSourceName = _manualArtworkCandidate.Source.Name;
            RebuildArtworkReview();
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
        MetadataField.Series => LocalizationService.Get("Field.Series"),
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
        yield return (MetadataField.Series, SeriesSourceText);
        yield return (MetadataField.Actors, ActorsSourceText);
        yield return (MetadataField.Genres, GenresSourceText);
        yield return (MetadataField.Plot, PlotSourceText);
    }

    private async Task<(bool Poster, bool Fanart)> LoadSelectedArtworkPreviewAsync()
    {
        var candidate = _artworkCoverReview?.SelectedCandidate;
        if (candidate is null)
        {
            ClearPosterPreview();
            ClearFanartPreview();
            return (false, false);
        }

        if (!candidate.IsSidecarPair)
        {
            return (
                await LoadPosterPreviewAsync(_metadata),
                await LoadFanartPreviewAsync(_metadata));
        }

        var posterLoaded = await LoadLocalSidecarPreviewAsync(
            candidate.LocalPosterPath,
            isPoster: true);
        var fanartLoaded = await LoadLocalSidecarPreviewAsync(
            candidate.LocalFanartPath,
            isPoster: false);
        return (posterLoaded, fanartLoaded);
    }

    private async Task<bool> LoadLocalSidecarPreviewAsync(string path, bool isPoster)
    {
        if (!ArtworkLocationHelper.TryGetLocalPath(path, out var localPath))
        {
            if (isPoster)
            {
                ClearPosterPreview();
            }
            else
            {
                ClearFanartPreview();
            }
            return false;
        }

        try
        {
            var bytes = await ArtworkLocationHelper.ReadLocalImageAsync(
                localPath,
                CurrentOperationToken);
            if (isPoster)
            {
                PosterImage.Source = PosterBitmapFactory.CreateFrozen(bytes);
                DropHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                var dimensions = PosterImageProcessor.GetDimensions(bytes);
                FanartImage.Source = PosterBitmapFactory.CreateFrozen(bytes);
                FanartHintText.Text = LocalizationService.Get("Artwork.Dimensions", dimensions.Width, dimensions.Height);
            }
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
            if (isPoster)
            {
                ClearPosterPreview();
            }
            else
            {
                ClearFanartPreview();
            }
            return false;
        }
    }

    private async Task<bool> LoadPosterPreviewAsync(MovieMetadata metadata)
    {
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
                var imageBytes = await DownloadPreviewImageAsync(candidate);
                var posterBytes = PosterImageProcessor.CreatePosterJpeg(imageBytes);
                PosterImage.Source = PosterBitmapFactory.CreateFrozen(posterBytes);
                DropHint.Visibility = Visibility.Collapsed;
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

        ClearPosterPreview();
        return false;
    }

    private async Task<bool> LoadFanartPreviewAsync(MovieMetadata metadata)
    {
        var candidates = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try
            {
                var imageBytes = await DownloadPreviewImageAsync(candidate);
                var dimensions = PosterImageProcessor.GetDimensions(imageBytes);
                FanartImage.Source = PosterBitmapFactory.CreateFrozen(PosterImageProcessor.CreateFanartJpeg(imageBytes));
                FanartHintText.Text = LocalizationService.Get("Artwork.Dimensions", dimensions.Width, dimensions.Height);
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

        ClearFanartPreview();
        return false;
    }

    private async Task<byte[]> DownloadPreviewImageAsync(string url)
    {
        if (ArtworkLocationHelper.TryGetLocalPath(url, out var localPath))
        {
            return await ArtworkLocationHelper.ReadLocalImageAsync(
                localPath,
                CurrentOperationToken);
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
                    CurrentOperationToken);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(CurrentOperationToken);
                if (bytes.Length < 128)
                {
                    throw new InvalidDataException(LocalizationService.Get("Error.ImageTooSmall"));
                }

                _ = PosterImageProcessor.GetDimensions(bytes);
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

    private void ClearPosterPreview()
    {
        PosterImage.Source = null;
        DropHint.Visibility = Visibility.Visible;
    }

    private void ClearFanartPreview()
    {
        FanartImage.Source = null;
        FanartHintText.Text = string.Empty;
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
        if (_busy)
        {
            return;
        }

        _busy = true;
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _activeOperationCancellation = operationCancellation;
        SearchButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        ArtworkSourceButton.IsEnabled = false;
        CancelOperationButton.IsEnabled = true;
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
            AppLog.Error(message, exception);
            ShowError(GetLocalizedExceptionMessage(exception));
        }
        finally
        {
            Mouse.OverrideCursor = null;
            SearchButton.IsEnabled = true;
            CancelOperationButton.Visibility = Visibility.Collapsed;
            _activeOperationCancellation = null;
            _busy = false;
            RefreshSaveAvailability();
            RefreshArtworkSourceBadge();
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_activeOperationCancellation is null || _activeOperationCancellation.IsCancellationRequested)
        {
            return;
        }

        CancelOperationButton.IsEnabled = false;
        SetStatus(LocalizationService.Get("Status.Canceling"), null);
        _activeOperationCancellation.Cancel();
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

    private static bool TryGetSingleDroppedPath(IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            return false;
        }

        path = files[0];
        return true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        AppLog.Info("JavMetaLite 关闭");
        PersistPreferencesOnClose();
        if (_metadataReview is not null)
        {
            _metadataReview.SelectionChanged -= MetadataReview_SelectionChanged;
            _metadataReview.Dispose();
        }
        _metadata.PropertyChanged -= Metadata_PropertyChanged;
        _activeOperationCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _javLibraryClient.Dispose();
        _libreDmmClient.Dispose();
        _r18DevClient.Dispose();
        _outputService.Dispose();
        _previewHttpClient.Dispose();
    }
}
