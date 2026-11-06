using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using Microsoft.Web.WebView2.Core;

namespace JavMetaLite.App;

public partial class BrowserWindow : Window
{
    private readonly BrowserImportTarget _target;
    private bool _returningToR18SearchHome;

    public BrowserWindow(string initialUrl)
        : this(new BrowserImportTarget(
            BrowserImportRouting.JavLibrary,
            BrowserImportRouting.GetDisplayName(BrowserImportRouting.JavLibrary),
            initialUrl,
            false))
    {
    }

    public BrowserWindow(BrowserImportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        InitializeComponent();
        WindowVisualTheme.ApplyDarkTitleBar(this);
        _target = target;
        Title = LocalizationService.Get("Browser.SourceTitle", target.DisplayName);
        BrowserInstruction.Text = LocalizationService.Get("Browser.SourceInstruction", target.DisplayName);
        if (target.SourceName == MetadataSourcePreferenceProfile.R18Dev)
        {
            ImportButton.IsEnabled = false;
        }
        Loaded += BrowserWindow_Loaded;
    }

    public string? PageHtml { get; private set; }
    public string? PageText { get; private set; }
    public string? PageUrl { get; private set; }
    public BrowserImportPageState CurrentPageState { get; private set; } =
        BrowserImportPageState.ManualSearchRequired;

    private async void BrowserWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
            Browser.Source = new Uri(_target.InitialUrl);
            UrlText.Text = _target.InitialUrl;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                LocalizationService.Get("Browser.StartFailed", exception.Message).Replace("\\n", Environment.NewLine),
                "JAV Metadata Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Browser_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_target.SourceName == MetadataSourcePreferenceProfile.R18Dev)
        {
            ImportButton.IsEnabled = false;
        }
    }

    private async void Browser_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var pageUrl = Browser.Source?.ToString() ?? string.Empty;
        UrlText.Text = pageUrl;
        if (_target.SourceName != MetadataSourcePreferenceProfile.R18Dev)
        {
            return;
        }

        try
        {
            if (_returningToR18SearchHome && IsR18HomePage(pageUrl))
            {
                _returningToR18SearchHome = false;
                if (!e.IsSuccess || e.HttpStatusCode is >= 400 and <= 599)
                {
                    ApplyR18PageState(BrowserImportPageState.Unavailable);
                    return;
                }

                if (!await PrefillR18SearchAsync())
                {
                    ApplyR18PageState(BrowserImportPageState.Unavailable);
                }
                return;
            }

            var hasVideoInfo = false;
            var hasSearchInput = false;
            var hasNotFoundMessage = false;
            if (e.IsSuccess)
            {
                var hasSearchInputJson = await Browser.ExecuteScriptAsync(
                    "Boolean(document.querySelector('#lookup'))");
                hasSearchInput = bool.TryParse(hasSearchInputJson, out var parsed) && parsed;

                var pageTextJson = await Browser.ExecuteScriptAsync("document.body?.innerText ?? ''");
                hasNotFoundMessage = BrowserImportRouting.ContainsR18NotFoundMessage(
                    JsonSerializer.Deserialize<string>(pageTextJson));
            }
            if (e.IsSuccess &&
                BrowserImportRouting.IsExpectedDetailPage(_target.SourceName, pageUrl))
            {
                var hasVideoInfoJson = await Browser.ExecuteScriptAsync(
                    "Boolean(document.querySelector('#video-info'))");
                hasVideoInfo = bool.TryParse(hasVideoInfoJson, out var parsed) && parsed;
            }

            var pageState = BrowserImportRouting.ClassifyR18Page(
                pageUrl,
                e.IsSuccess,
                e.HttpStatusCode,
                hasVideoInfo,
                hasSearchInput,
                hasNotFoundMessage);
            ApplyR18PageState(pageState);
            if (pageState == BrowserImportPageState.NotFound)
            {
                _returningToR18SearchHome = true;
                Browser.Source = new Uri(R18DevClient.HomePageUrl);
            }
            else if (pageState == BrowserImportPageState.ManualSearchRequired)
            {
                if (!await PrefillR18SearchAsync())
                {
                    ApplyR18PageState(BrowserImportPageState.Unavailable);
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning($"R18.dev 浏览器页面状态检测失败 url={pageUrl}", exception);
            ApplyR18PageState(BrowserImportPageState.Unavailable);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var pageUrl = Browser.Source?.ToString();
        var isExpectedPage = BrowserImportRouting.IsExpectedDetailPage(_target.SourceName, pageUrl);
        if (isExpectedPage && _target.SourceName == BrowserImportRouting.JavLibrary)
        {
            var hasVideoInfoJson = await Browser.ExecuteScriptAsync("Boolean(document.querySelector('#video_info'))");
            isExpectedPage = bool.TryParse(hasVideoInfoJson, out var hasVideoInfo) && hasVideoInfo;
        }
        else if (isExpectedPage && _target.SourceName == MetadataSourcePreferenceProfile.R18Dev)
        {
            var hasVideoInfoJson = await Browser.ExecuteScriptAsync("Boolean(document.querySelector('#video-info'))");
            isExpectedPage = bool.TryParse(hasVideoInfoJson, out var hasVideoInfo) && hasVideoInfo;
        }

        if (!isExpectedPage)
        {
            if (_target.SourceName == MetadataSourcePreferenceProfile.R18Dev)
            {
                ApplyR18PageState(
                    BrowserImportRouting.IsExpectedDetailPage(_target.SourceName, pageUrl)
                        ? BrowserImportPageState.Unavailable
                        : BrowserImportPageState.ManualSearchRequired);
                return;
            }

            MessageBox.Show(this,
                LocalizationService.Get("Browser.SourceNotDetailPage", _target.DisplayName),
                "JAV Metadata Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var htmlJson = await Browser.ExecuteScriptAsync("document.documentElement.outerHTML");
        var textJson = await Browser.ExecuteScriptAsync("document.body?.innerText ?? ''");
        PageHtml = JsonSerializer.Deserialize<string>(htmlJson);
        PageText = JsonSerializer.Deserialize<string>(textJson);
        PageUrl = pageUrl;
        DialogResult = true;
    }

    private async Task<bool> PrefillR18SearchAsync()
    {
        var id = MovieIdParser.Normalize(_target.RequestedMovieId);
        if (string.IsNullOrWhiteSpace(id) || Browser.CoreWebView2 is null)
        {
            return false;
        }

        var idJson = JsonSerializer.Serialize(id);
        var prefilledJson = await Browser.ExecuteScriptAsync(
            $"(() => {{ const input = document.querySelector('#lookup'); " +
            $"if (!input) return false; input.value = {idJson}; " +
            "input.dispatchEvent(new Event('input', { bubbles: true })); input.focus(); return true; })()");
        return bool.TryParse(prefilledJson, out var prefilled) && prefilled;
    }

    private void ApplyR18PageState(BrowserImportPageState pageState)
    {
        CurrentPageState = pageState;
        ImportButton.IsEnabled = pageState == BrowserImportPageState.Ready;
        if (pageState == BrowserImportPageState.Ready)
        {
            BrowserStatusPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var id = MovieIdParser.Normalize(_target.RequestedMovieId);
        var resourceKey = pageState switch
        {
            BrowserImportPageState.NotFound => "Browser.SourceMovieNotFound",
            BrowserImportPageState.Unavailable => "Browser.SourceUnavailable",
            _ => "Browser.SourceManualSearch"
        };
        BrowserStatusText.Text = LocalizationService.Get(
            resourceKey,
            _target.DisplayName,
            id);
        var isUnavailable = pageState == BrowserImportPageState.Unavailable;
        BrowserStatusPanel.Background = new SolidColorBrush(isUnavailable
            ? Color.FromRgb(51, 33, 38)
            : Color.FromRgb(45, 39, 29));
        BrowserStatusPanel.BorderBrush = new SolidColorBrush(isUnavailable
            ? Color.FromRgb(169, 75, 91)
            : Color.FromRgb(156, 107, 42));
        BrowserStatusText.Foreground = new SolidColorBrush(isUnavailable
            ? Color.FromRgb(255, 175, 184)
            : Color.FromRgb(255, 208, 138));
        BrowserStatusPanel.Visibility = Visibility.Visible;
    }

    private static bool IsR18HomePage(string? pageUrl) =>
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) &&
        (string.Equals(uri.Host, "r18.dev", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".r18.dev", StringComparison.OrdinalIgnoreCase)) &&
        string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/'));
}
