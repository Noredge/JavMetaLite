using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestManualBrowserMenu(string profile)
    {
        var owner = new MainWindow(TestPreferences());
        owner.Show();
        try
        {
            ((TextBox)owner.FindName("IdTextBox")).Text = "TEST-123";
            var job = (MovieJob)typeof(MainWindow).GetField("_activeJob", PrivateInstance)!.GetValue(owner)!;
            job.ApplyManualWebSource(new MovieMetadata { Id = "TEST-123", SourceName = "r18dev", ContentId = "test00123" });
            // Intercept site documents before navigation, keeping this real WebView2 test offline.
            EventManager.RegisterClassHandler(typeof(BrowserWindow), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is not BrowserWindow dialog || dialog.Owner != owner) return;
                    var view = (WebView2)dialog.FindName("Browser");
                    view.CreationProperties.UserDataFolder = profile;
                    view.CoreWebView2InitializationCompleted += (_, init) =>
                    {
                        if (!init.IsSuccess) return;
                        var core = view.CoreWebView2;
                        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                        core.WebResourceRequested += (_, request) => request.Response =
                            core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(
                                "<title>Manual lookup fixture</title><div id='video-info'>TEST-123</div>")),
                                200, "OK", "Content-Type: text/html");
                    };
                }));
            foreach (var source in new[] { "libredmm", "r18dev", "javlibrary" })
            {
                var button = OpenManualWebsites(owner).Single(b => Equals(b.Tag, source));
                var target = BrowserImportRouting.BuildTarget(source, job.Metadata.Id, job.SourceResults);
                Process? browserProcess = null;
                InteractWithModal<BrowserWindow>(owner, () =>
                {
                    var peer = new ButtonAutomationPeer(button);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                }, dialog =>
                {
                    var view = (WebView2)dialog.FindName("Browser");
                    if (view.CoreWebView2?.DocumentTitle != "Manual lookup fixture")
                    {
                        var done = new TaskCompletionSource<bool>();
                        view.NavigationCompleted += (_, e) => done.TrySetResult(e.IsSuccess);
                        WaitForTask(done.Task.WaitAsync(TimeSpan.FromSeconds(40)));
                    }
                    if (view.CoreWebView2 is not { } core || view.Source != new Uri(target.InitialUrl) ||
                        core.DocumentTitle != "Manual lookup fixture")
                        throw new InvalidOperationException("Manual menu did not render the chosen site: " + source);
                    browserProcess = Process.GetProcessById((int)core.BrowserProcessId);
                });
                using (browserProcess) WaitForTask(browserProcess!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            }
            Console.WriteLine("PASS All three manual menu buttons opened an owned browser and rendered their routed URL offline.");
        }
        finally { owner.Close(); }
    }

    private static int RunBrowserProfileTest()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative)
        });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var root = Directory.CreateTempSubdirectory("JavMetaLite-profile-test-");
        var page = Path.Combine(root.FullName, "test.html");
        File.WriteAllText(page, "<title>Profile test</title>");
        var target = new BrowserImportTarget("libredmm", "LibreDMM", new Uri(page).AbsoluteUri, false);
        var expected = Path.Combine(root.FullName, "JavMetaLite", "WebView2");
        var adjacent = Environment.ProcessPath + ".WebView2";
        if (Directory.Exists(adjacent)) throw new InvalidOperationException("Test host already has an adjacent profile.");
        try
        {
            for (var launch = 0; launch < 2; launch++)
            {
                var window = new BrowserWindow(target, root.FullName);
                var browser = (WebView2)window.FindName("Browser");
                if (browser.CreationProperties.UserDataFolder != expected)
                    throw new InvalidOperationException("WebView2 profile was not configured before initialization.");
                var navigated = new TaskCompletionSource<bool>();
                browser.NavigationCompleted += (_, e) => navigated.TrySetResult(e.IsSuccess);
                window.Show();
                Process? host = null;
                try
                {
                    WaitForTask(navigated.Task.WaitAsync(TimeSpan.FromSeconds(40)));
                    var core = browser.CoreWebView2;
                    if (!navigated.Task.Result || !string.Equals(core.Environment.UserDataFolder, expected,
                        StringComparison.OrdinalIgnoreCase) || !Directory.Exists(expected) || Directory.Exists(adjacent))
                        throw new InvalidOperationException("WebView2 used an unexpected profile.");
                    host = Process.GetProcessById((int)core.BrowserProcessId);
                    var cookies = core.CookieManager;
                    if (launch == 0)
                    {
                        var cookie = cookies.CreateCookie("profile-check", "retained", "profile.example", "/");
                        cookie.Expires = DateTime.UtcNow.AddDays(1);
                        cookies.AddOrUpdateCookie(cookie);
                    }
                    var query = cookies.GetCookiesAsync("https://profile.example/");
                    WaitForTask(query);
                    if (!query.Result.Any(c => c.Name == "profile-check" && c.Value == "retained"))
                        throw new InvalidOperationException("WebView2 did not retain the test cookie.");
                }
                finally
                {
                    window.Close();
                    if (host != null)
                    {
                        using (host) WaitForTask(host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)));
                    }
                }
            }
            TestManualBrowserMenu(expected);
            Console.WriteLine("PASS WebView2 launched twice, retained its cookie in the same local profile and wrote no adjacent profile.");
            return 0;
        }
        finally
        {
            app.Shutdown();
            // Crashpad may release its last handle just after the browser process exits.
            for (var attempt = 0; root.Exists; attempt++)
            {
                try { root.Delete(true); break; }
                catch (IOException) when (attempt < 30) { Thread.Sleep(250); }
            }
        }
    }
}
