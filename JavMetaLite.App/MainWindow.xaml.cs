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
    private readonly Dictionary<string, PreviewImage> _previewImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Width, int Height)> _coverDimensions = new(StringComparer.OrdinalIgnoreCase);
    private MovieMetadata _metadata = new();
    private MetadataReviewSession? _metadataReview;
    private ArtworkReviewSession? _artworkReview;
    private string? _videoPath;
    private bool _busy;
    private bool _updatingArtworkControls;

    public MainWindow()
    {
        _outputService = new OutputService();
        _fileOrganizationService = new FileOrganizationService(_outputService);
        InitializeComponent();
        ApplyMetadata(_metadata, []);
        AppLog.Info("JavMetaLite v0.5.0-dev5 启动");
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
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
            SelectVideo(dialog.FileName);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedVideo(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedVideo(e.Data, out var path))
        {
            SelectVideo(path!);
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
                var result = outcome.Metadata;
                ApplyMetadata(result, outcome.Sources);
                var successfulSources = string.Join(
                    "+",
                    outcome.Sources.Select(GetSourceDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                var failedSources = string.Join(
                    "+",
                    outcome.Attempts.Where(attempt => !attempt.Success).Select(attempt => attempt.SourceDisplayName));
                AppLog.Info(
                    $"metadata 搜索成功 sources={successfulSources} failedSources={failedSources} id={result.Id} " +
                    $"contentId={result.ContentId} screenshots={result.ScreenshotUrls.Count} " +
                    $"reviewSources={outcome.Sources.Count}");
                var coverLoaded = await LoadSelectedCoverPreviewAsync();
                var sourceName = string.Join(
                    " + ",
                    outcome.Sources.Select(GetSourceDisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
                var degradedNote = string.IsNullOrWhiteSpace(failedSources)
                    ? string.Empty
                    : $"；{failedSources} 未返回结果";
                var imageSummary = result.ScreenshotUrls.Count > 0
                    ? $"，找到 {result.ScreenshotUrls.Count} 张样张"
                    : "，没有独立样张";
                SetStatus(
                    coverLoaded
                        ? $"已从 {sourceName} 读取 {result.Id}{imageSummary}{degradedNote}"
                        : $"已从 {sourceName} 读取 {result.Id}；封面预览未加载，不影响资料编辑{degradedNote}",
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

        var options = new SaveOptions(
            WriteNfoCheckBox.IsChecked == true,
            DownloadPosterCheckBox.IsChecked == true,
            DownloadFanartCheckBox.IsChecked == true,
            DownloadExtrafanartCheckBox.IsChecked == true,
            DirectSaveOverwriteCheckBox.IsChecked == true);
        var organizationOptions = new OrganizationOptions(
            OrganizeFolderCheckBox.IsChecked == true,
            RenameVideoCheckBox.IsChecked == true);
        var artworkSelection = _artworkReview?.CreateSelection() ?? ArtworkSelection.FromMetadata(_metadata);

        SavePlan plan;
        try
        {
            plan = FileOrganizationService.BuildPlan(
                _videoPath,
                _metadata,
                options,
                organizationOptions,
                artworkSelection);
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
            FileNameText.Text = Path.GetFileName(result.VideoPath);
            FilePathText.Text = result.VideoPath;
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
                : result.Outputs.FanartUsedFullCover
                    ? $"；封套：{result.Outputs.CoverSourceDisplayName}"
                    : string.Empty;
            var screenshotNote = result.Outputs.ExtrafanartPaths.Count > 0
                ? $"；剧照：{result.Outputs.ScreenshotSourceDisplayName}"
                : string.Empty;
            var moveNote = result.VideoMoved ? $"；影片已整理为 {Path.GetFileName(result.VideoPath)}" : string.Empty;
            SetStatus($"保存完成：{string.Join("、", outputs)}{fanartNote}{screenshotNote}{moveNote}", true);
        });
    }

    private void SelectVideo(string path)
    {
        if (!File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            ShowError("请选择受支持的影片文件。 ");
            return;
        }

        _videoPath = path;
        AppLog.Info($"选择影片 path={path}");
        FileNameText.Text = Path.GetFileName(path);
        FilePathText.Text = path;
        var id = MovieIdParser.TryExtract(path);
        ApplyMetadata(new MovieMetadata { Id = id ?? string.Empty }, []);
        ClearPosterPreview();
        ClearFanartPreview("等待搜索");
        if (!string.IsNullOrWhiteSpace(id))
        {
            SetStatus($"已识别番号 {id}，可以搜索资料", true);
        }
        else
        {
            SetStatus("没有从文件名识别到番号，请手动输入", false);
        }
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
                ApplyMetadata(result, [result]);
                var coverLoaded = await LoadSelectedCoverPreviewAsync();
                SetStatus(
                    coverLoaded
                        ? $"已从浏览器读取 {result.Id}"
                        : $"已从浏览器读取 {result.Id}；封面预览未加载，不影响保存",
                    true);
            });
        }
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
        _metadataReview = MetadataReviewSession.Create(result, sourceResults.ToArray());
        _metadataReview.SelectionChanged += MetadataReview_SelectionChanged;
        _artworkReview = ArtworkReviewSession.Create(result, sourceResults.ToArray());
        _previewImageCache.Clear();
        _coverDimensions.Clear();
        RefreshSourceBadges();
        RefreshArtworkSourceControls();
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

    private void RefreshArtworkSourceControls()
    {
        _updatingArtworkControls = true;
        try
        {
            CoverSourceComboBox.Items.Clear();
            SampleSourceComboBox.Items.Clear();

            if (_artworkReview is null)
            {
                ArtworkSourcePanel.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var candidate in _artworkReview.CoverCandidates)
            {
                var dimensions = _coverDimensions.TryGetValue(candidate.Source.Name, out var size)
                    ? $"{size.Width}×{size.Height}"
                    : "待检测";
                CoverSourceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{candidate.Source.DisplayName} · {dimensions}",
                    Tag = candidate.Source.Name,
                    ToolTip = string.Join(Environment.NewLine, candidate.CoverUrls)
                });
            }

            foreach (var choice in _artworkReview.ScreenshotChoices)
            {
                SampleSourceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = choice.IsCombined
                        ? $"合并去重 · {choice.Urls.Count} 张候选"
                        : $"{choice.DisplayName} · {choice.Urls.Count} 张",
                    Tag = choice.Name,
                    ToolTip = choice.DisplayName
                });
            }

            CoverSourceComboBox.SelectedIndex = FindComboBoxItem(
                CoverSourceComboBox,
                _artworkReview.SelectedCoverCandidate?.Source.Name ?? string.Empty);
            SampleSourceComboBox.SelectedIndex = FindComboBoxItem(
                SampleSourceComboBox,
                _artworkReview.SelectedScreenshotChoice?.Name ?? string.Empty);
            CoverSourceComboBox.IsEnabled = !_busy && CoverSourceComboBox.Items.Count > 1;
            SampleSourceComboBox.IsEnabled = !_busy && SampleSourceComboBox.Items.Count > 1;
            CoverSourceLabel.Visibility = CoverSourceComboBox.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CoverSourceComboBox.Visibility = CoverSourceComboBox.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SampleSourceLabel.Visibility = SampleSourceComboBox.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SampleSourceComboBox.Visibility = SampleSourceComboBox.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ArtworkSourcePanel.Visibility = CoverSourceComboBox.Items.Count + SampleSourceComboBox.Items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _updatingArtworkControls = false;
        }
    }

    private static int FindComboBoxItem(ComboBox comboBox, string selectedName)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), selectedName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return comboBox.Items.Count == 0 ? -1 : 0;
    }

    private async void CoverSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingArtworkControls || _artworkReview is null ||
            CoverSourceComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string sourceName ||
            !_artworkReview.SelectCoverSource(sourceName))
        {
            return;
        }

        var selected = _artworkReview.CreateSelection();
        AppLog.Info($"封套来源切换 source={selected.CoverSourceName}");
        await RunBusyAsync($"正在加载 {selected.CoverSourceDisplayName} 封套…", async () =>
        {
            var loaded = await LoadSelectedCoverPreviewAsync();
            SetStatus(
                loaded
                    ? $"封套来源已切换为 {selected.CoverSourceDisplayName}"
                    : $"{selected.CoverSourceDisplayName} 封套加载失败，请选择其他来源",
                loaded);
        });
    }

    private void SampleSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingArtworkControls || _artworkReview is null ||
            SampleSourceComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string choiceName ||
            !_artworkReview.SelectScreenshotSource(choiceName))
        {
            return;
        }

        var selected = _artworkReview.CreateSelection();
        AppLog.Info(
            $"剧照来源切换 source={selected.ScreenshotSourceName} candidates={selected.ScreenshotUrls.Count} hashDedupe=true");
        SetStatus($"剧照来源已切换为 {selected.ScreenshotSourceDisplayName}；保存时按图片内容去重", true);
    }

    private async Task<bool> LoadSelectedCoverPreviewAsync()
    {
        var selection = _artworkReview?.CreateSelection() ?? ArtworkSelection.FromMetadata(_metadata);
        if (selection.CoverUrls.Count == 0)
        {
            ClearPosterPreview();
            ClearFanartPreview("当前来源没有封套");
            return false;
        }

        foreach (var candidate in selection.CoverUrls)
        {
            try
            {
                var image = await DownloadPreviewImageAsync(candidate);
                PosterImage.Source = PosterBitmapFactory.CreateFrozen(PosterImageProcessor.CreatePosterJpeg(image.Bytes));
                FanartImage.Source = PosterBitmapFactory.CreateFrozen(PosterImageProcessor.CreateFanartJpeg(image.Bytes));
                DropHint.Visibility = Visibility.Collapsed;
                FanartDropHint.Visibility = Visibility.Collapsed;
                FanartHintText.Text = $"{selection.CoverSourceDisplayName} · 完整封套 · {image.Width}×{image.Height}";
                _coverDimensions[selection.CoverSourceName] = (image.Width, image.Height);
                RefreshArtworkSourceControls();
                AppLog.Info(
                    $"封套预览加载成功 source={selection.CoverSourceName} url={image.Url} dimensions={image.Width}x{image.Height}");
                return true;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or NotSupportedException or FormatException)
            {
                AppLog.Warning($"封套预览候选失败 source={selection.CoverSourceName} url={candidate}", exception);
            }
        }

        ClearPosterPreview();
        ClearFanartPreview($"{selection.CoverSourceDisplayName} 封套预览失败");
        return false;
    }

    private async Task<PreviewImage> DownloadPreviewImageAsync(string url)
    {
        Exception? lastError = null;
        foreach (var candidate in DmmImageUrlHelper.GetDownloadCandidates(url).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_previewImageCache.TryGetValue(candidate, out var cached))
            {
                return cached;
            }

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

                var dimensions = PosterImageProcessor.GetDimensions(bytes);
                var image = new PreviewImage(candidate, bytes, dimensions.Width, dimensions.Height);
                _previewImageCache[candidate] = image;
                return image;
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

    private sealed record PreviewImage(string Url, byte[] Bytes, int Width, int Height);

    private void ClearPosterPreview()
    {
        PosterImage.Source = null;
        DropHint.Visibility = Visibility.Visible;
    }

    private void ClearFanartPreview(string hint)
    {
        FanartImage.Source = null;
        FanartDropHint.Visibility = Visibility.Visible;
        FanartHintText.Text = hint;
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
        CoverSourceComboBox.IsEnabled = false;
        SampleSourceComboBox.IsEnabled = false;
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
            SaveButton.IsEnabled = true;
            CoverSourceComboBox.IsEnabled = CoverSourceComboBox.Items.Count > 1;
            SampleSourceComboBox.IsEnabled = SampleSourceComboBox.Items.Count > 1;
            _busy = false;
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
