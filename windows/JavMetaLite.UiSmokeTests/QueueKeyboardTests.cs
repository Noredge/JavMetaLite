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
        if (Window.GetWindow(target) != window)
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

    private static async Task TestQueueKeyboardNavigationAsync(string root)
    {
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        try
        {
            foreach (var scenario in new[] { "down", "up", "release", "restart", "mouse", "tab", "deactivate", "cancel" })
                await TestQueueKeyboardScenarioAsync(root, scenario);
        }
        finally { LocalizationService.ApplyLanguage(originalLanguage); }
        Console.WriteLine("UI PASS queueHeldArrows=True busyFocusLeakBlocked=True cachedNavigation=True queueBounds=True keyRelease=True explicitFocusRespected=True previewCancelKeyboard=True intentionalComboArrows=True");
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
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            await UntilStableAsync(() => !IsWindowBusy(window), "Initial keyboard preview did not settle.");
            Keyboard.Focus(list);
            if (!list.IsKeyboardFocusWithin) throw new InvalidOperationException("Queue did not obtain keyboard focus.");
            handler.HoldNext = true;
            QueueTestKey(window, direction);
            await UntilStableAsync(() => handler.Pending == 1 && IsWindowBusy(window), "Slow keyboard preview did not start.");

            // Reproduce the focus fallback observed in the recording deterministically. This is
            // NOT a mouse/Tab gesture; the held queue arrow must survive framework focus movement.
            Keyboard.Focus(language);
            if (!language.IsKeyboardFocusWithin) throw new InvalidOperationException("Cannot reproduce language focus fallback.");
            for (var i = 0; i < 20; i++)
            {
                var input = QueueTestKey(window, direction);
                if (!input.Handled || language.SelectedIndex != 0 || source.SelectedIndex != sourceIndex)
                    throw new InvalidOperationException($"Held {direction} leaked during preview: language={language.SelectedIndex}, source={source.SelectedIndex}, scenario={scenario}.");
                await Dispatcher.Yield(DispatcherPriority.Input);
            }
            if (handler.Calls != 2 || list.SelectedIndex != 1)
                throw new InvalidOperationException("Held arrows queued extra preview work while busy.");

            var explicitFocus = scenario is "mouse" or "tab" or "deactivate";
            if (scenario is "release" or "restart") QueueTestKey(window, direction, release: true);
            if (scenario == "restart")
            {
                // The key was released, but framework focus is still temporarily on the combo.
                if (!QueueTestKey(window, direction).Handled || language.SelectedIndex != 0)
                    throw new InvalidOperationException("Fresh arrow leaked before pending queue focus returned.");
            }
            if (scenario == "mouse") QueueTestClick(language);
            if (scenario == "tab")
            {
                QueueTestKey(window, Key.Tab);
                Keyboard.Focus(language);
            }
            if (scenario == "deactivate")
                typeof(Window).GetMethod("OnDeactivated", PrivateInstance)!.Invoke(window, [EventArgs.Empty]);
            if (explicitFocus)
            {
                QueueTestKey(window, Key.Down);
                if (language.SelectedIndex != 1)
                    throw new InvalidOperationException("Intentional language navigation was swallowed.");
                QueueTestKey(window, Key.Down, release: true);
            }
            if (scenario == "cancel")
                ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else handler.Release();
            await UntilStableAsync(() => !IsWindowBusy(window) && handler.Pending == 0, "Held-key preview did not finish.");
            if (explicitFocus)
            {
                if (!language.IsKeyboardFocusWithin || language.SelectedIndex != 1)
                    throw new InvalidOperationException("Preview completion stole deliberately changed focus.");
            }
            else
            {
                if (!list.IsKeyboardFocusWithin || list.SelectedIndex != 1 || language.SelectedIndex != 0)
                    throw new InvalidOperationException("Queue focus/selection was not restored after held-key preview.");
                if (scenario != "release") QueueTestKey(window, direction, release: true);

                // Cached round trips and end stops, without a fresh mouse click between arrows.
                for (var i = 0; i < 6; i++)
                {
                    var back = initial == 0 ? Key.Up : Key.Down;
                    QueueTestKey(window, back);
                    await UntilStableAsync(() => !IsWindowBusy(window) && list.IsKeyboardFocusWithin, "Cached back navigation lost focus.");
                    QueueTestKey(window, back); // already at first/last item
                    if (list.SelectedIndex != initial) throw new InvalidOperationException("Queue boundary navigation escaped.");
                    QueueTestKey(window, back, release: true);
                    QueueTestKey(window, direction);
                    await UntilStableAsync(() => !IsWindowBusy(window) && list.IsKeyboardFocusWithin, "Cached forward navigation lost focus.");
                    QueueTestKey(window, direction, release: true);
                }
                if (language.SelectedIndex != 0 || source.SelectedIndex != sourceIndex ||
                    handler.Calls != (scenario == "cancel" ? 3 : 2))
                    throw new InvalidOperationException("Cached arrows changed a combo or reloaded completed images.");

                // No key release or Dispatcher idle between cached round trips: deferred
                // focus callbacks from the previous selection must not redirect later input.
                for (var i = 0; i < 20; i++)
                {
                    QueueTestKey(window, initial == 0 ? Key.Up : Key.Down);
                    QueueTestKey(window, direction);
                }
                QueueTestKey(window, direction, release: true);
                await UntilStableAsync(() => !IsWindowBusy(window) && list.IsKeyboardFocusWithin,
                    "Rapid cached round trips did not restore queue focus.");
                if (list.SelectedIndex != 1 || language.SelectedIndex != 0)
                    throw new InvalidOperationException("Rapid cached arrows escaped the queue.");
            }

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
