using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static int RunQueueKeyboardTests(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/queue-keyboard");
        Directory.CreateDirectory(root);
        AppLog.ConfigureDirectory(Path.Combine(root, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        RunStabilityTask(TestQueueKeyboardNavigationAsync(root));
        return 0;
    }

    // Route each synthetic input through the CURRENT focused control, including the normal
    // bubbling KeyDown/class handlers. Sending only PreviewKeyDown to the list misses this bug.
    private static KeyEventArgs QueueTestKey(MainWindow window, Key key, bool release = false)
    {
        var target = Keyboard.FocusedElement as UIElement
            ?? throw new InvalidOperationException("Keyboard test has no focused UI element.");
        if (Window.GetWindow(target) != window &&
            !((System.Windows.Controls.Primitives.Popup)window.FindName("AppearancePopup")).IsKeyboardFocusWithin &&
            !((ComboBox)window.FindName("LanguageComboBox")).IsKeyboardFocusWithin)
            throw new InvalidOperationException("Keyboard test focus escaped its synthetic window.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!,
            Environment.TickCount, key)
        {
            RoutedEvent = release ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent,
            Source = target
        };
        target.RaiseEvent(args);
        if (!args.Handled)
        {
            args.RoutedEvent = release ? Keyboard.KeyUpEvent : Keyboard.KeyDownEvent;
            target.RaiseEvent(args);
        }
        return args;
    }

    private static void QueueTestClick(UIElement target)
    {
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = target });
        Keyboard.Focus(target);
    }

    private static bool NavigationSettled(MainWindow window) =>
        typeof(MainWindow).GetField("_navigationPreviewCancellation", PrivateInstance)!.GetValue(window) is null;

    private static async Task TestQueueKeyboardNavigationAsync(string root)
    {
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        try
        {
            foreach (var scenario in new[] { "down", "up", "release", "restart", "mouse", "tab", "deactivate", "supersede" })
                await TestQueueKeyboardScenarioAsync(root, scenario);
        }
        finally { LocalizationService.ApplyLanguage(originalLanguage); }
        Console.WriteLine("UI PASS queueHeldArrows=True nonblockingPreview=True cachedNavigation=True queueBounds=True keyRelease=True explicitFocusRespected=True supersededPreviewCanceled=True queueViewStable=True simultaneousUncroppedArtwork=True intentionalComboArrows=True");
    }

    private static async Task TestQueueKeyboardScenarioAsync(string root, string scenario)
    {
        LocalizationService.ApplyLanguage("zh-Hans");
        using var handler = new GatedQueuePreviewHandler();
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, scenario, "settings")))
        { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(handler));
        window.Show();
        try
        {
            SetQueueOpen(window, true);

            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            for (var i = 0; i < 3; i++)
            {
                var job = new MovieJob();
                job.ResetForVideo($@"C:\Synthetic\KEY-{i + 1:000}.mp4", $"KEY-{i + 1:000}");
                var metadata = new MovieMetadata { Id = job.Metadata.Id, Title = "Synthetic keyboard test",
                    SourceName = "libredmm", CoverUrl = $"https://keyboard.invalid/{i}.png" };
                job.ApplyOnlineSources(metadata, [metadata]);
                typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!.Invoke(window, [job]);
                queue.Add(job);
            }
            var list = (ListBox)window.FindName("MovieQueueList");
            var language = (ComboBox)window.FindName("LanguageComboBox");
            var source = (ComboBox)window.FindName("SourceComboBox");
            var sourceIndex = source.SelectedIndex;
            var direction = scenario == "up" ? Key.Up : Key.Down;
            var initial = direction == Key.Up ? 2 : 0;
            list.SelectedIndex = initial;
            await UntilStableAsync(() => NavigationSettled(window), "Initial keyboard preview did not settle.");
            Keyboard.Focus(list);
            if (!list.IsKeyboardFocusWithin) throw new InvalidOperationException("Queue did not obtain keyboard focus.");
            var resets = 0;
            ((System.Collections.Specialized.INotifyCollectionChanged)list.Items).CollectionChanged += (_, change) =>
            {
                if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
            };
            handler.HoldNext = true;
            QueueTestKey(window, direction);
            await UntilStableAsync(() => handler.Pending == 1, "Slow keyboard preview did not start.");
            if (IsWindowBusy(window) || !list.IsEnabled || list.SelectedIndex != 1 ||
                window.DataContext != ((MovieJob)queue[1]!).Metadata ||
                !((FrameworkElement)window.FindName("MetadataPanel")).IsEnabled ||
                ((Button)window.FindName("CancelOperationButton")).IsVisible)
                throw new InvalidOperationException("Selecting a movie blocked the workspace or deferred its metadata.");

            var explicitFocus = scenario is "mouse" or "tab" or "deactivate";
            var fallback = (Button)window.FindName("PreferencesButton");
            if (scenario is "release" or "restart") QueueTestKey(window, direction, release: true);
            if (scenario == "mouse") QueueTestClick(fallback);
            if (scenario == "tab") { QueueTestKey(window, Key.Tab); Keyboard.Focus(fallback); }
            if (scenario == "deactivate")
            {
                Keyboard.Focus(fallback);
                typeof(Window).GetMethod("OnDeactivated", PrivateInstance)!.Invoke(window, [EventArgs.Empty]);
            }
            if (scenario is "supersede" or "restart")
            {
                QueueTestKey(window, direction);
                if (list.SelectedIndex != 2) throw new InvalidOperationException("A pending preview swallowed the next arrow.");
                list.SelectedIndex = 1;
            }
            handler.Release();
            await UntilStableAsync(() => NavigationSettled(window) && handler.Pending == 0, "Keyboard preview did not settle.");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (explicitFocus && !fallback.IsKeyboardFocusWithin)
                throw new InvalidOperationException("Preview completion stole deliberately changed focus.");

            Keyboard.Focus(list);
            var calls = handler.Calls;
            for (var i = 0; i < 12; i++)
            {
                var back = initial == 0 ? Key.Up : Key.Down;
                QueueTestKey(window, back);
                QueueTestKey(window, back);
                if (list.SelectedIndex != initial) throw new InvalidOperationException("Queue boundary navigation escaped.");
                QueueTestKey(window, direction);
                if (list.SelectedIndex != 1) throw new InvalidOperationException("Cached navigation lost the selection.");
            }
            QueueTestKey(window, direction, release: true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (resets != 0 || handler.Calls != calls || language.SelectedIndex != 0 || source.SelectedIndex != sourceIndex)
                throw new InvalidOperationException($"Selection refreshed the queue or reloaded cached images: resets={resets}, calls={handler.Calls}/{calls}.");

            if (scenario == "down")
            {
                var cover = (Border)window.FindName("PosterPreviewBorder");
                var sleeve = (Border)window.FindName("FanartPreviewBorder");
                foreach (var width in new[] { 930, 1120 })
                {
                    window.Width = width;
                    window.Height = 650;
                    window.UpdateLayout();
                    var viewport = (Grid)window.FindName("ArtViewport");
                    if (!FitsInside(cover, viewport) || !FitsInside(sleeve, viewport) ||
                        !cover.IsVisible || !sleeve.IsVisible ||
                        cover.TranslatePoint(new Point(0, cover.ActualHeight), viewport).Y >= sleeve.TranslatePoint(new Point(), viewport).Y)
                        throw new InvalidOperationException("Both images did not fit together in the artwork column.");
                    foreach (var (border, name) in new[] { (cover, "PosterImage"), (sleeve, "FanartImage") })
                    {
                        var artwork = ((Image)window.FindName(name)).Source;
                        if (artwork is null || border.ActualWidth < 1 || border.ActualHeight < 1 ||
                            Math.Abs(border.ActualHeight - border.ActualWidth * artwork.Height / artwork.Width) > 1 ||
                            border.Background is not null)
                            throw new InvalidOperationException("Loaded artwork exposed its background or changed aspect ratio.");
                    }
                }
            }

            ClickWorkspaceButton(window, "PreferencesButton");
            QueueTestClick(language);
            language.SelectedIndex = 0;
            for (var i = 1; i < 4; i++)
            {
                QueueTestKey(window, Key.Down);
                if (language.SelectedIndex != i) throw new InvalidOperationException("Deliberate language Down stopped working.");
            }
            QueueTestKey(window, Key.Down, release: true);
            QueueTestKey(window, Key.Up);
            if (language.SelectedIndex != 2) throw new InvalidOperationException("Deliberate language Up stopped working.");
            QueueTestKey(window, Key.Up, release: true);
            ((System.Windows.Controls.Primitives.Popup)window.FindName("AppearancePopup")).IsOpen = false;
            QueueTestClick(source);
            source.SelectedIndex = 0;
            QueueTestKey(window, Key.Down);
            if (source.SelectedIndex != 1) throw new InvalidOperationException("Deliberate source navigation stopped working.");
            QueueTestKey(window, Key.Down, release: true);
            var currentMovie = list.SelectedItem;
            var text = (TextBox)window.FindName("IdTextBox");
            QueueTestClick(text);
            QueueTestKey(window, Key.Up);
            QueueTestKey(window, Key.Up, release: true);
            QueueTestKey(window, Key.Down);
            QueueTestKey(window, Key.Down, release: true);
            if (!ReferenceEquals(currentMovie, list.SelectedItem) || !text.IsKeyboardFocusWithin)
                throw new InvalidOperationException("Text-box arrows were redirected to the queue.");
        }
        finally
        {
            handler.Release();
            await UntilStableAsync(() => !IsWindowBusy(window), "Keyboard test cleanup remained busy.");
            window.Close();
        }
        Console.WriteLine($"UI PASS queueKeyboardScenario={scenario}");
    }

    private sealed class GatedQueuePreviewHandler : HttpMessageHandler
    {
        private readonly byte[] _image = StabilityImage(900, 600);
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldNext { get; set; }
        public int Calls { get; private set; }
        public int Pending { get; private set; }
        public void Release() => _gate.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "keyboard.invalid") throw new InvalidOperationException("Unexpected keyboard test request.");
            Calls++;
            if (HoldNext)
            {
                HoldNext = false;
                Pending++;
                try { await _gate.Task.WaitAsync(cancellationToken); }
                finally { Pending--; }
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_image) };
        }
    }
}
