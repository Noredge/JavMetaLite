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
        IReadOnlyList<MovieMetadata> Sources)
    {
        public static MetadataSearchOutcome FromSingleSource(MovieMetadata metadata) =>
            new(metadata, [metadata]);
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
    private string? _videoPath;
    private bool _busy;

    public MainWindow()
    {
        _outputService = new OutputService();
        _fileOrganizationService = new FileOrganizationService(_outputService);
        InitializeComponent();
        ApplyMetadata(_metadata, []);
        AppLog.Info("JavMetaLite v0.5.0-dev2 启动");
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
        await RunBusyAsync("正在获取影片资料…", async () =>
        {
            try
            {
                var outcome = await SearchFromSelectedSourceAsync(source, _metadata.Id);
                var result = outcome.Metadata;
                ApplyMetadata(result, outcome.Sources);
                AppLog.Info(
                    $"metadata 搜索成功 source={GetSourceDisplayName(result)} id={result.Id} " +
                    $"contentId={result.ContentId} screenshots={result.ScreenshotUrls.Count} " +
                    $"reviewSources={outcome.Sources.Count}");
                var posterLoaded = await LoadPosterPreviewAsync(result);
                var fanartLoaded = await LoadFanartPreviewAsync(result);
                var sourceName = GetSourceDisplayName(result);
                var imageSummary = result.ScreenshotUrls.Count > 0
                    ? $"，找到 {result.ScreenshotUrls.Count} 张样张"
                    : "，没有独立样张";
                SetStatus(
                    posterLoaded
                        ? $"已从 {sourceName} 读取 {result.Id}{imageSummary}{(fanartLoaded ? string.Empty : "；完整封套 fanart 预览未加载")}"
                        : $"已从 {sourceName} 读取 {result.Id}；封面预览未加载，不影响资料编辑",
                    true);
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
                return MetadataSearchOutcome.FromSingleSource(
                    await _libreDmmClient.SearchAsync(id, _lifetimeCancellation.Token));
            }

            if (source == "r18dev")
            {
                return MetadataSearchOutcome.FromSingleSource(
                    await _r18DevClient.SearchAsync(id, _lifetimeCancellation.Token));
            }

            if (source == "javlibrary")
            {
                return MetadataSearchOutcome.FromSingleSource(
                    await _javLibraryClient.SearchAsync(id, _lifetimeCancellation.Token));
            }

            MovieMetadata? primary = null;
            try
            {
                primary = await _libreDmmClient.SearchAsync(id, _lifetimeCancellation.Token);
            }
            catch (Exception exception) when (IsRecoverableMetadataError(exception))
            {
                AppLog.Warning($"自动搜索的 LibreDMM 阶段失败 id={id}", exception);
            }

            if (primary is null)
            {
                AppLog.Info($"LibreDMM 不可用，改用 R18.dev id={id}");
                return MetadataSearchOutcome.FromSingleSource(
                    await _r18DevClient.SearchAsync(id, _lifetimeCancellation.Token));
            }

            if (!MetadataMerger.NeedsFallback(primary))
            {
                return MetadataSearchOutcome.FromSingleSource(primary);
            }

            try
            {
                var fallback = await _r18DevClient.SearchAsync(id, _lifetimeCancellation.Token);
                return new MetadataSearchOutcome(
                    MetadataMerger.Merge(primary, fallback),
                    [primary, fallback]);
            }
            catch (Exception exception) when (IsRecoverableMetadataError(exception))
            {
                AppLog.Warning($"R18.dev 补全阶段失败，保留 LibreDMM 结果 id={id}", exception);
                return MetadataSearchOutcome.FromSingleSource(primary);
            }
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

        SavePlan plan;
        try
        {
            plan = FileOrganizationService.BuildPlan(_videoPath, _metadata, options, organizationOptions);
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
                : result.Outputs.FanartUsedFullCover ? "；fanart 来自完整封套" : string.Empty;
            var moveNote = result.VideoMoved ? $"；影片已整理为 {Path.GetFileName(result.VideoPath)}" : string.Empty;
            SetStatus($"保存完成：{string.Join("、", outputs)}{fanartNote}{moveNote}", true);
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
                var posterLoaded = await LoadPosterPreviewAsync(result);
                await LoadFanartPreviewAsync(result);
                SetStatus(
                    posterLoaded
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
        RefreshSourceBadges();
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
        badge.Text = candidate?.Source.DisplayName ?? string.Empty;
        badge.Visibility = candidate is null ? Visibility.Collapsed : Visibility.Visible;
        badge.ToolTip = candidate is null
            ? null
            : string.IsNullOrWhiteSpace(candidate.Source.Url)
                ? candidate.Source.DisplayName
                : $"来源：{candidate.Source.DisplayName}\n{candidate.Source.Url}";
    }

    private IEnumerable<(MetadataField Field, TextBlock Badge)> GetSourceBadgeControls()
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

    private async Task<bool> LoadPosterPreviewAsync(MovieMetadata metadata)
    {
        var candidates = new[] { metadata.CoverUrl, metadata.FallbackCoverUrl, metadata.PosterUrl }
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _))
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
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try
            {
                var imageBytes = await DownloadPreviewImageAsync(candidate);
                var dimensions = PosterImageProcessor.GetDimensions(imageBytes);
                FanartImage.Source = PosterBitmapFactory.CreateFrozen(PosterImageProcessor.CreateFanartJpeg(imageBytes));
                FanartDropHint.Visibility = Visibility.Collapsed;
                FanartHintText.Text = $"完整横版封套 · {dimensions.Width}×{dimensions.Height}";
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

        ClearFanartPreview("完整封套预览加载失败");
        return false;
    }

    private async Task<byte[]> DownloadPreviewImageAsync(string url)
    {
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

    private void ClearFanartPreview(string hint)
    {
        FanartImage.Source = null;
        FanartDropHint.Visibility = Visibility.Visible;
        FanartHintText.Text = hint;
    }

    private static bool IsRecoverableMetadataError(Exception exception) =>
        exception is MetadataNotFoundException or HttpRequestException or InvalidDataException or TaskCanceledException;

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
