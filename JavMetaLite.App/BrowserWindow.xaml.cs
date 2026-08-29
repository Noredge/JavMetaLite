using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace JavMetaLite.App;

public partial class BrowserWindow : Window
{
    private readonly string _initialUrl;

    public BrowserWindow(string initialUrl)
    {
        InitializeComponent();
        _initialUrl = initialUrl;
        Loaded += BrowserWindow_Loaded;
    }

    public string? PageHtml { get; private set; }
    public string? PageUrl { get; private set; }

    private async void BrowserWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
            Browser.Source = new Uri(_initialUrl);
            UrlText.Text = _initialUrl;
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

    private void Browser_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        UrlText.Text = Browser.Source?.ToString() ?? string.Empty;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var hasVideoInfoJson = await Browser.ExecuteScriptAsync("Boolean(document.querySelector('#video_info'))");
        if (!bool.TryParse(hasVideoInfoJson, out var hasVideoInfo) || !hasVideoInfo)
        {
            MessageBox.Show(this,
                LocalizationService.Get("Browser.NotDetailPage"),
                "JAV Metadata Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var htmlJson = await Browser.ExecuteScriptAsync("document.documentElement.outerHTML");
        PageHtml = JsonSerializer.Deserialize<string>(htmlJson);
        PageUrl = Browser.Source?.ToString();
        DialogResult = true;
    }
}
