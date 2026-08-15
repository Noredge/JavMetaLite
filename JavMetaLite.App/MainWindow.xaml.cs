using System.ComponentModel;
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
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".wmv", ".mov", ".webm", ".ts", ".m2ts"
    };

    private readonly JavLibraryClient _javLibraryClient = new();
    private readonly LibreDmmClient _libreDmmClient = new();
    private readonly R18DevClient _r18DevClient = new();
    private readonly OutputService _outputService = new();
    private readonly HttpClient _previewHttpClient = CreatePreviewClient();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private MovieMetadata _metadata = new();
    private string? _videoPath;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _metadata;
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
                var result = await SearchFromSelectedSourceAsync(source, _metadata.Id);
                ApplyMetadata(result);
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

    private async Task<MovieMetadata> SearchFromSelectedSourceAsync(string source, string id)
    {
        if (source == "libredmm")
        {
            return await _libreDmmClient.SearchAsync(id, _lifetimeCancellation.Token);
        }

        if (source == "r18dev")
        {
            return await _r18DevClient.SearchAsync(id, _lifetimeCancellation.Token);
        }

        if (source == "javlibrary")
        {
            return await _javLibraryClient.SearchAsync(id, _lifetimeCancellation.Token);
        }

        MovieMetadata? primary = null;
        try
        {
            primary = await _libreDmmClient.SearchAsync(id, _lifetimeCancellation.Token);
        }
        catch (Exception exception) when (IsRecoverableMetadataError(exception))
        {
        }

        if (primary is null)
        {
            return await _r18DevClient.SearchAsync(id, _lifetimeCancellation.Token);
        }

        if (!MetadataMerger.NeedsFallback(primary))
        {
            return primary;
        }

        try
        {
            return MetadataMerger.Merge(
                primary,
                await _r18DevClient.SearchAsync(id, _lifetimeCancellation.Token));
        }
        catch (Exception exception) when (IsRecoverableMetadataError(exception))
        {
            return primary;
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
            OverwriteCheckBox.IsChecked == true);

        if (!options.OverwriteExisting)
        {
            var conflicts = OutputService.FindExistingOutputFiles(_videoPath, _metadata, options);
            if (conflicts.Count > 0)
            {
                var videoDirectory = Path.GetDirectoryName(_videoPath)!;
                var displayedFiles = conflicts
                    .Take(12)
                    .Select(path => Path.GetRelativePath(videoDirectory, path));
                var remainingText = conflicts.Count > 12 ? $"\n……另有 {conflicts.Count - 12} 个文件" : string.Empty;
                var choice = MessageBox.Show(
                    this,
                    $"检测到重复文件，是否覆盖？\n\n{string.Join(Environment.NewLine, displayedFiles)}{remainingText}",
                    "JAV Metadata Lite",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes)
                {
                    SetStatus("已取消保存，现有文件未修改", false);
                    return;
                }

                options = options with { OverwriteExisting = true };
            }
        }

        await RunBusyAsync("正在生成伴随文件…", async () =>
        {
            var result = await _outputService.SaveAsync(_videoPath, _metadata, options, _lifetimeCancellation.Token);
            var outputs = new[] { result.NfoPath, result.PosterPath, result.FanartPath }
                .Where(path => path is not null)
                .Select(Path.GetFileName)
                .ToList();
            if (result.ExtrafanartPaths.Count > 0)
            {
                outputs.Add($"extrafanart（{result.ExtrafanartPaths.Count} 张）");
            }
            var fanartNote = result.FanartPath is null
                ? string.Empty
                : result.FanartUsedFullCover ? "；fanart 来自完整封套" : string.Empty;
            SetStatus($"保存完成：{string.Join("、", outputs)}{fanartNote}", true);
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
        FileNameText.Text = Path.GetFileName(path);
        FilePathText.Text = path;
        var id = MovieIdParser.TryExtract(path);
        _metadata = new MovieMetadata { Id = id ?? string.Empty };
        DataContext = _metadata;
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
                ApplyMetadata(result);
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

    private void ApplyMetadata(MovieMetadata result)
    {
        _metadata = result;
        DataContext = _metadata;
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
            catch (Exception exception) when (exception is HttpRequestException or IOException or NotSupportedException or FormatException)
            {
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
            catch (Exception exception) when (exception is HttpRequestException or IOException or NotSupportedException or FormatException)
            {
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
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _javLibraryClient.Dispose();
        _libreDmmClient.Dispose();
        _r18DevClient.Dispose();
        _outputService.Dispose();
        _previewHttpClient.Dispose();
    }
}
