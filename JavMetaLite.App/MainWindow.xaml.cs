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

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".wmv", ".mov", ".webm", ".ts", ".m2ts"
    };

    private readonly JavLibraryClient _javLibraryClient = new();
    private readonly LibreDmmClient _libreDmmClient = new();
    private readonly R18DevClient _r18DevClient = new();
    private readonly OutputService _outputService;
    private readonly FileOrganizationService _fileOrganizationService;
    private readonly HttpClient _previewHttpClient = CreatePreviewClient();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
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
    private bool _localNfoSaveBlocked;
    private bool _busy;

    public MainWindow()
    {
        _outputService = new OutputService();
        _fileOrganizationService = new FileOrganizationService(_outputService);
        InitializeComponent();
        ApplyMetadata(_metadata, []);
        AppLog.Info("JavMetaLite v0.6.0-dev4 启动");
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择一个影片",
            Filter = "影片文件|*.mp4;*.m4v;*.mkv;*.avi;*.wmv;*.mov;*.webm;*.ts;*.m2ts|所有文件|*.*",
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
        e.Effects = TryGetDroppedVideo(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedVideo(e.Data, out var path))
        {
            await SelectVideoAsync(path!);
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_metadata.Id))
        {
            ShowError("请先输入影片番号。 ");
            return;
        }

        var source = (SourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        if (source == "manual")
        {
            SetStatus("当前为手动模式，可以直接填写资料并保存", true);
            return;
        }

        string? browserFallbackUrl = null;
        var busyMessage = source == "auto"
            ? "正在同时获取 LibreDMM 与 R18.dev…"
            : "正在获取影片资料…";
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
                    $"reviewSources={outcome.Sources.Count} localDefault={_localSourceMetadata is not null}");
                var artworkLoaded = await LoadSelectedArtworkPreviewAsync();
                var sourceName = string.Join(
                    " + ",
                    outcome.Sources.Select(GetSourceDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                var degradedNote = string.IsNullOrWhiteSpace(failedSources)
                    ? string.Empty
                    : $"；{failedSources} 未返回结果";
                var imageSummary = result.ScreenshotUrls.Count > 0
                    ? $"，找到 {result.ScreenshotUrls.Count} 张样张"
                    : "，没有独立样张";
                var localPrefix = _localSourceMetadata is null
                    ? $"已从 {sourceName} 读取 {result.Id}"
                    : $"已加入 {sourceName} 在线候选，当前保留本地 NFO";
                SetStatus(
                    artworkLoaded.Poster
                        ? $"{localPrefix}{imageSummary}{(artworkLoaded.Fanart ? string.Empty : "；fanart 预览未加载")}{degradedNote}"
                        : $"{localPrefix}；封面预览未加载，不影响资料编辑{degradedNote}",
                    string.IsNullOrWhiteSpace(failedSources));
            }
            catch (JavLibraryChallengeException exception)
            {
                browserFallbackUrl = exception.Url;
                SetStatus("网站要求浏览器验证，正在打开内置浏览器…", false);
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
                        _lifetimeCancellation.Token));
            }

            if (source == "r18dev")
            {
                return MetadataSearchOutcome.FromSingleAttempt(
                    await MetadataSearchCoordinator.SearchSingleAsync(
                        id,
                        _r18DevClient,
                        _lifetimeCancellation.Token));
            }

            if (source == "javlibrary")
            {
                return MetadataSearchOutcome.FromSingleAttempt(
                    await MetadataSearchCoordinator.SearchSingleAsync(
                        id,
                        _javLibraryClient,
                        _lifetimeCancellation.Token));
            }

            var multiSourceResult = await MetadataSearchCoordinator.SearchAllAsync(
                id,
                _libreDmmClient,
                _r18DevClient,
                _lifetimeCancellation.Token);
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
            ShowError("请先选择一个影片文件。 ");
            return;
        }

        if (_localNfoSaveBlocked)
        {
            ShowError("检测到无法安全读取的本地 NFO。为保护原文件，必须修复或移走该 NFO 后重新选择影片，当前不能保存。");
            return;
        }

        var options = new SaveOptions(
            WriteNfoCheckBox.IsChecked == true,
            DownloadPosterCheckBox.IsChecked == true,
            DownloadFanartCheckBox.IsChecked == true,
            DownloadExtrafanartCheckBox.IsChecked == true,
            DirectSaveOverwriteCheckBox.IsChecked == true);

        var organizationOptions = new OrganizationOptions(
            OrganizeFolderCheckBox.IsChecked == true,
            RenameVideoCheckBox.IsChecked == true);

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
                SetStatus("已取消，影片和 metadata 均未修改", false);
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

        await RunBusyAsync("正在安全生成并提交文件…", async () =>
        {
            var result = await _fileOrganizationService.ExecuteAsync(
                plan,
                _metadata,
                allowOverwrite,
                _lifetimeCancellation.Token);
            _videoPath = result.VideoPath;
            var outputs = new[] { result.Outputs.NfoPath, result.Outputs.PosterPath, result.Outputs.FanartPath }
                .Where(path => path is not null)
                .Select(Path.GetFileName)
                .ToList();
            if (result.Outputs.ExtrafanartPaths.Count > 0)
            {
                outputs.Add($"extrafanart（{result.Outputs.ExtrafanartPaths.Count} 张）");
            }
            var fanartNote = result.Outputs.FanartPath is null
                ? string.Empty
                : result.Outputs.FanartUsedFullCover ? "；fanart 来自完整封套" : string.Empty;
            var moveNote = result.VideoMoved ? $"；影片已整理为 {Path.GetFileName(result.VideoPath)}" : string.Empty;
            await SelectVideoCoreAsync(result.VideoPath);
            var outputSummary = outputs.Count == 0
                ? plan.HasActualChanges ? "sidecar 已安全迁移" : "没有需要写入的变更"
                : string.Join("、", outputs);
            SetStatus($"保存完成：{outputSummary}{fanartNote}{moveNote}", true);
        });
    }

    private Task SelectVideoAsync(string path) =>
        RunBusyAsync("正在检查影片旁的本地 metadata…", () => SelectVideoCoreAsync(path));

    private async Task SelectVideoCoreAsync(string path)
    {
        if (!File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            ShowError("请选择受支持的影片文件。 ");
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
            SetStatus($"无法检查影片旁的本地文件：{exception.Message}", false);
            return;
        }

        string metadataStatus;
        var statusSuccess = !string.IsNullOrWhiteSpace(id);
        if (!sidecars.HasNfo)
        {
            metadataStatus = !string.IsNullOrWhiteSpace(id)
                ? $"已识别番号 {id}，未找到同名本地 NFO，可以搜索资料"
                : "没有找到同名本地 NFO，也未从文件名识别到番号，请手动输入";
        }
        else
        {
            _localNfoSaveBlocked = true;
            try
            {
                var bundle = await NfoReader.ReadAsync(sidecars, _lifetimeCancellation.Token);
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
                metadataStatus = $"已从本地 NFO 载入（可安全更新）：{bundle.Sidecars.NfoPath}{diagnosticNote}";
                statusSuccess = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AppLog.Warning($"本地 NFO 读取失败 path={sidecars.NfoPath}", exception);
                metadataStatus =
                    $"检测到本地 NFO，但无法安全读取，原文件未修改：{Path.GetFileName(sidecars.NfoPath)}；{exception.Message}";
                statusSuccess = false;
            }
        }

        var artworkDiscovery = await DiscoverLocalArtworkAsync(sidecars);
        if (!string.IsNullOrWhiteSpace(artworkDiscovery.Summary))
        {
            metadataStatus += $"；{artworkDiscovery.Summary}";
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
            _lifetimeCancellation.Token);
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
                : $"{discovery.Diagnostics.Count} 个无效本地图片已忽略（见日志）";
            return (invalidSummary, discovery.Diagnostics.Count > 0);
        }

        _preferredArtworkSourceName = _localArtworkCandidate.Source.Name;
        RebuildArtworkReview();
        var loaded = await LoadSelectedArtworkPreviewAsync();
        var availability = (_localArtworkCandidate.HasPoster, _localArtworkCandidate.HasFanart) switch
        {
            (true, true) => "poster + fanart",
            (true, false) => "仅 poster，fanart 缺失",
            (false, true) => "仅 fanart，poster 缺失",
            _ => "无可用图片"
        };
        var diagnosticNote = discovery.Diagnostics.Count == 0
            ? string.Empty
            : $"；{discovery.Diagnostics.Count} 个无效图片已忽略";
        var previewNote = loaded.Poster == _localArtworkCandidate.HasPoster &&
                          loaded.Fanart == _localArtworkCandidate.HasFanart
            ? string.Empty
            : "；预览加载不完整";
        return ($"本地图片已载入（{availability}）{diagnosticNote}{previewNote}",
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
            await RunBusyAsync("正在读取浏览器中的资料…", async () =>
            {
                var result = await _javLibraryClient.ParseDetailPageAsync(
                    browser.PageHtml,
                    browser.PageUrl ?? url,
                    _metadata.Id,
                    _lifetimeCancellation.Token);
                ApplyOnlineSources(result, [result]);
                var artworkLoaded = await LoadSelectedArtworkPreviewAsync();
                var localNote = _localSourceMetadata is null ? string.Empty : "，当前保留本地 NFO";
                SetStatus(
                    artworkLoaded.Poster
                        ? $"已加入浏览器资料候选 {result.Id}{localNote}"
                        : $"已加入浏览器资料候选 {result.Id}{localNote}；封面预览未加载，不影响资料编辑",
                    true);
            });
        }
    }

    private MovieMetadata ApplyOnlineSources(
        MovieMetadata preferredOnlineMetadata,
        IReadOnlyList<MovieMetadata> onlineSources)
    {
        if (_localSourceMetadata is null)
        {
            ApplyMetadata(preferredOnlineMetadata, onlineSources);
            return _metadata;
        }

        var localForMerge = LocalMetadataReviewComposer.CreateLocal(_localSourceMetadata).Metadata;
        localForMerge.Id = _metadata.Id;
        var composition = LocalMetadataReviewComposer.ComposeWithOnline(
            localForMerge,
            preferredOnlineMetadata,
            onlineSources);
        ApplyMetadata(composition.Metadata, composition.Sources);
        return _metadata;
    }

    private void ApplyMetadata(MovieMetadata result, IReadOnlyList<MovieMetadata> sourceResults)
    {
        if (_metadataReview is not null)
        {
            _metadataReview.SelectionChanged -= MetadataReview_SelectionChanged;
            _metadataReview.Dispose();
        }

        _metadata = result;
        DataContext = _metadata;
        _currentSourceResults = sourceResults.ToArray();
        _metadataReview = MetadataReviewSession.Create(result, _currentSourceResults.ToArray());
        _metadataReview.SelectionChanged += MetadataReview_SelectionChanged;
        RebuildArtworkReview();
        RefreshSourceBadges();
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
        badge.Content = candidate is null
            ? string.Empty
            : candidates.Count > 1
                ? $"{candidate.Source.DisplayName} ▾"
                : candidate.Source.DisplayName;
        badge.Visibility = candidate is null ? Visibility.Collapsed : Visibility.Visible;
        badge.IsEnabled = candidates.Count > 1;
        badge.ToolTip = candidate is null
            ? null
            : candidates.Count > 1
                ? $"点击选择“{GetFieldDisplayName(field)}”的资料来源（{candidates.Count} 个候选）"
                : string.IsNullOrWhiteSpace(candidate.Source.Url)
                    ? $"来源：{candidate.Source.DisplayName}"
                    : $"来源：{candidate.Source.DisplayName}\n{candidate.Source.Url}";
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
            var sourceText = new TextBlock
            {
                Text = $"{(candidate == selected ? "✓ " : string.Empty)}{candidate.Source.DisplayName}",
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
                    ? candidate.Source.DisplayName
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
        AppLog.Info($"字段来源切换 field={candidate.Field} source={candidate.Source.Name}");
        SetStatus($"已将“{fieldName}”切换为 {candidate.Source.DisplayName}", true);
    }

    private void RefreshArtworkSourceBadge()
    {
        var candidate = _artworkCoverReview?.SelectedCandidate;
        var candidates = _artworkCoverReview?.Candidates ?? [];
        ArtworkSourceButton.Content = candidate is null
            ? "选择封套…"
            : $"{candidate.Source.DisplayName} ▾";
        ArtworkSourceButton.Visibility = candidate is null && _videoPath is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArtworkSourceButton.IsEnabled = !_busy && (_videoPath is not null || candidate is not null);
        ArtworkSourceButton.ToolTip = candidate is null
            ? "选择一张本地完整封套，由同一张图生成 poster 与 fanart"
            : $"点击选择封套来源（{candidates.Count} 个现有候选），或选择本地完整封套";
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
            var sourceText = new TextBlock
            {
                Text = $"{(candidate == selected ? "✓ " : string.Empty)}{candidate.Source.DisplayName}",
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
                Text = "选择本地完整封套…",
                Foreground = new SolidColorBrush(Color.FromRgb(111, 168, 255)),
                FontWeight = FontWeights.SemiBold
            };
            var chooseValueText = new TextBlock
            {
                Text = "选择一张图片，统一生成 poster 与 fanart",
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
                ToolTip = "支持 JPG、JPEG、PNG 与 WEBP"
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
        await RunBusyAsync($"正在加载 {candidate.Source.DisplayName} 封套…", async () =>
        {
            var preview = await LoadSelectedArtworkPreviewAsync();
            var loaded = preview.Poster && preview.Fanart;
            SetStatus(
                loaded
                    ? $"封套与 fanart 已同时切换为 {candidate.Source.DisplayName}"
                    : $"{candidate.Source.DisplayName} 封套预览加载不完整，可改选其他来源",
                loaded);
        });
    }

    private static string GetArtworkCandidateDescription(ArtworkCoverCandidate candidate)
    {
        if (candidate.IsSidecarPair)
        {
            return (candidate.HasPoster, candidate.HasFanart) switch
            {
                (true, true) => "现有 poster + fanart（成对来源）",
                (true, false) => "现有 poster；fanart 缺失",
                (false, true) => "现有 fanart；poster 缺失",
                _ => "没有可用的本地图片"
            };
        }

        return candidate.Source.Name == "manual-cover"
            ? "本地完整封套 · 统一生成 poster / fanart"
            : candidate.Urls.Count > 1
                ? $"poster / fanart 共用 · {candidate.Urls.Count} 个封套地址"
                : "poster / fanart 共用此完整封套";
    }

    private async Task ChooseManualCoverAsync()
    {
        if (_videoPath is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择一张完整封套",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.webp|所有文件|*.*",
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
        RunBusyAsync("正在读取本地完整封套…", async () =>
        {
            var bytes = await ArtworkLocationHelper.ReadLocalImageAsync(
                path,
                _lifetimeCancellation.Token);
            var dimensions = PosterImageProcessor.GetDimensions(bytes);
            _manualArtworkCandidate = ArtworkCoverCandidate.CreateCompleteCover(
                new MetadataCandidateSource("manual-cover", "手动封套", Path.GetFullPath(path)),
                path);
            _preferredArtworkSourceName = _manualArtworkCandidate.Source.Name;
            RebuildArtworkReview();
            var preview = await LoadSelectedArtworkPreviewAsync();
            if (!preview.Poster || !preview.Fanart)
            {
                throw new InvalidDataException("本地完整封套无法同时生成 poster 与 fanart 预览。");
            }

            AppLog.Info($"手动完整封套载入成功 path={path} size={dimensions.Width}x{dimensions.Height}");
            SetStatus(
                $"已选择本地完整封套：{Path.GetFileName(path)}；poster 与 fanart 将由同一来源生成",
                true);
        });

    private static string BuildCandidatePreview(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return "（空白）";
        }

        const int maximumLength = 100;
        return normalized.Length <= maximumLength
            ? normalized
            : $"{normalized[..maximumLength]}…";
    }

    private static string GetFieldDisplayName(MetadataField field) => field switch
    {
        MetadataField.Title => "标题",
        MetadataField.OriginalTitle => "原始标题",
        MetadataField.ReleaseDate => "发行日期",
        MetadataField.RuntimeMinutes => "时长",
        MetadataField.Maker => "片商",
        MetadataField.Director => "导演",
        MetadataField.Label => "标签 / 厂牌",
        MetadataField.Series => "系列",
        MetadataField.Actors => "演员",
        MetadataField.Genres => "类型",
        MetadataField.Plot => "简介",
        MetadataField.Rating => "评分",
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
                _lifetimeCancellation.Token);
            if (isPoster)
            {
                PosterImage.Source = PosterBitmapFactory.CreateFrozen(bytes);
                DropHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                var dimensions = PosterImageProcessor.GetDimensions(bytes);
                FanartImage.Source = PosterBitmapFactory.CreateFrozen(bytes);
                FanartHintText.Text = $"横板封套：{dimensions.Width}×{dimensions.Height}";
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
                FanartHintText.Text = $"横板封套：{dimensions.Width}×{dimensions.Height}";
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
                _lifetimeCancellation.Token);
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
                    _lifetimeCancellation.Token);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(_lifetimeCancellation.Token);
                if (bytes.Length < 128)
                {
                    throw new InvalidDataException("图片内容太小。 ");
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

        throw new InvalidDataException("图片预览下载失败。", lastError);
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
        string.IsNullOrWhiteSpace(metadata.SourceDisplayName) ? metadata.SourceName : metadata.SourceDisplayName;

    private static HttpClient CreatePreviewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,ja;q=0.7,en;q=0.5");
        return client;
    }

    private async Task RunBusyAsync(string message, Func<Task> operation)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SearchButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        ArtworkSourceButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        SetStatus(message, null);

        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error(message, exception);
            ShowError(exception.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            SearchButton.IsEnabled = true;
            SaveButton.IsEnabled = !_localNfoSaveBlocked;
            SaveButton.ToolTip = _localNfoSaveBlocked
                ? "本地 NFO 无法安全读取；修复或移走后重新选择影片"
                : _localMetadataBundle is not null
                    ? "保存时只更新受管理字段，并保留未知 XML"
                    : null;
            _busy = false;
            RefreshArtworkSourceBadge();
        }
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
            SetStatus($"日志目录：{AppLog.LogDirectory}", true);
        }
        catch (Exception exception)
        {
            AppLog.Error("无法打开日志目录", exception);
            ShowError($"无法打开日志目录：{exception.Message}");
        }
    }

    private static bool TryGetDroppedVideo(IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            return false;
        }

        path = files[0];
        return File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        AppLog.Info("JavMetaLite 关闭");
        if (_metadataReview is not null)
        {
            _metadataReview.SelectionChanged -= MetadataReview_SelectionChanged;
            _metadataReview.Dispose();
        }
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _javLibraryClient.Dispose();
        _libreDmmClient.Dispose();
        _r18DevClient.Dispose();
        _outputService.Dispose();
        _previewHttpClient.Dispose();
    }
}
