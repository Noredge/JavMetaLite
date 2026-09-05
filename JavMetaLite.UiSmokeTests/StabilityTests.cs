using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static int RunStability(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/stability");
        Directory.CreateDirectory(root);
        AppLog.ConfigureDirectory(Path.Combine(root, "logs"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        RunStabilityTask(TestCanceledPreviewRevisitAsync(root));
        RunStabilityTask(TestDelayedViewerAsync());
        if (args.Length > 2)
        {
            var count = int.Parse(args[2]);
            var rounds = int.Parse(args.ElementAtOrDefault(3) ?? "5");
            if (count is < 10 or > 2000 || rounds is < 1 or > 10)
                throw new ArgumentException("Stability count must be 10..2000; rounds 1..10.");
            RunStabilityTask(RunContinuousStabilityAsync(root, count, rounds));
        }
        Console.WriteLine("STABILITY targeted preview checks PASS");
        return 0;
    }

    private static void RunStabilityTask(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static async Task UntilStableAsync(Func<bool> condition, string message, int timeoutSeconds = 20)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed.TotalSeconds > timeoutSeconds) throw new TimeoutException(message);
            await Task.Delay(5);
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static bool IsWindowBusy(MainWindow window) =>
        (bool)typeof(MainWindow).GetField("_busy", PrivateInstance)!.GetValue(window)!;

    private static void ReplaceStabilityClient(MainWindow window, string fieldName, IDisposable replacement)
    {
        var field = typeof(MainWindow).GetField(fieldName, PrivateInstance)!;
        ((IDisposable)field.GetValue(window)!).Dispose();
        field.SetValue(window, replacement);
    }

    private static byte[] StabilityImage(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)(width % 255), width * height * 3).ToArray();
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    private static async Task TestCanceledPreviewRevisitAsync(string root)
    {
        using var handler = new DelayedPreviewHandler();
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "preview-settings")))
        { ShowActivated = false, ShowInTaskbar = false };
        ReplaceStabilityClient(window, "_previewHttpClient", new HttpClient(handler));
        window.Show();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            MovieJob Add(string id, string url)
            {
                var job = new MovieJob(); job.ResetForVideo($@"C:\Synthetic\{id}.mp4", id);
                var metadata = new MovieMetadata { Id = id, Title = id, SourceName = "libredmm", CoverUrl = url };
                var alternate = new MovieMetadata { Id = id, Title = id, SourceName = "r18dev",
                    CoverUrl = "https://stability.invalid/switch.png" };
                job.ApplyOnlineSources(metadata, id == "SLOW-001" ? [metadata, alternate] : [metadata]);
                typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!.Invoke(window, [job]);
                queue.Add(job); return job;
            }
            var slow = Add("SLOW-001", "https://stability.invalid/slow.png");
            var fast = Add("FAST-002", "https://stability.invalid/fast.png");
            var list = (ListBox)window.FindName("MovieQueueList");
            var image = (Image)window.FindName("FanartImage");
            list.SelectedItem = slow;
            await UntilStableAsync(() => handler.Pending == 1 && IsWindowBusy(window), "Slow preview did not start.");
            ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilStableAsync(() => handler.Pending == 0 && !IsWindowBusy(window), "Canceled preview did not settle.");
            if (handler.Canceled != 1) throw new InvalidOperationException("Preview cancellation was not observed.");
            list.SelectedItem = fast;
            await UntilStableAsync(() => !IsWindowBusy(window), "Fast preview did not finish.");
            if (image.Source is null) throw new InvalidOperationException("Fast movie lost its preview.");
            list.SelectedItem = slow;
            await UntilStableAsync(() => !IsWindowBusy(window), "Canceled movie revisit did not finish.");
            if (handler.SlowCalls != 2 || image.Source is null ||
                ((TextBlock)window.FindName("FanartHintText")).Text != LocalizationService.Get("Artwork.Dimensions", 1200, 600))
                throw new InvalidOperationException($"Canceled preview was cached as complete; revisit must reload. slowCalls={handler.SlowCalls}, image={image.Source is not null}.");
            var restoredImage = image.Source;
            list.SelectedItem = fast;
            await UntilStableAsync(() => !IsWindowBusy(window), "Second fast preview did not finish.");
            list.SelectedItem = slow;
            await UntilStableAsync(() => !IsWindowBusy(window), "Completed preview revisit did not finish.");
            if (!ReferenceEquals(restoredImage, image.Source) || handler.SlowCalls != 2)
                throw new InvalidOperationException("Completed preview no longer uses the cache.");

            // Cancel a source change after a completed preview was already cached.
            var alternateSource = slow.ArtworkReview.Candidates.Single(c => c.Source.Name == "r18dev");
            var changeSource = (Task)typeof(MainWindow).GetMethod("SelectArtworkSourceCandidateAsync", PrivateInstance)!
                .Invoke(window, [alternateSource])!;
            await UntilStableAsync(() => handler.Pending == 1 && IsWindowBusy(window), "Source preview did not start.");
            ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await changeSource;
            await UntilStableAsync(() => handler.Pending == 0 && !IsWindowBusy(window), "Source preview did not cancel.");
            list.SelectedItem = fast;
            await UntilStableAsync(() => !IsWindowBusy(window), "Source-change switch did not finish.");
            list.SelectedItem = slow;
            await UntilStableAsync(() => !IsWindowBusy(window), "Canceled source revisit did not finish.");
            if (handler.SwitchCalls != 2 ||
                ((TextBlock)window.FindName("FanartHintText")).Text != LocalizationService.Get("Artwork.Dimensions", 1000, 500))
                throw new InvalidOperationException("Canceled source change restored stale/blank cached artwork.");

            var empty = Add("EMPTY-003", "");
            list.SelectedItem = empty;
            await UntilStableAsync(() => !IsWindowBusy(window), "Missing artwork did not settle.");
            list.SelectedItem = fast;
            await UntilStableAsync(() => !IsWindowBusy(window), "Switch after missing artwork did not settle.");
            var cache = (MoviePreviewCache)typeof(MainWindow).GetField("_jobPreviews", PrivateInstance)!.GetValue(window)!;
            if (!cache.Contains(empty)) throw new InvalidOperationException("Completed missing-artwork preview must remain cacheable.");
        }
        finally
        {
            if (IsWindowBusy(window))
            {
                ((Button)window.FindName("CancelOperationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UntilStableAsync(() => !IsWindowBusy(window), "Preview test could not cancel cleanup.");
            }
            window.Close();
        }
    }

    private static async Task TestDelayedViewerAsync()
    {
        var oldItem = new ArtworkViewerItem("Old", null, "https://stability.invalid/old.png");
        var newItem = new ArtworkViewerItem("New", null, "https://stability.invalid/new.png");
        var pendingItem = new ArtworkViewerItem("Pending", null, "https://stability.invalid/pending.png");
        var oldResult = new TaskCompletionSource<ArtworkViewerLoadedImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = 0;
        var canceled = 0;
        var newImage = PosterBitmapFactory.CreateFrozen(StabilityImage(900, 600));
        var oldImage = PosterBitmapFactory.CreateFrozen(StabilityImage(1200, 600));
        async Task<ArtworkViewerLoadedImage> Load(ArtworkViewerItem item, CancellationToken token)
        {
            if (ReferenceEquals(item, newItem)) return new(newImage, 900, 600);
            if (ReferenceEquals(item, oldItem)) return await oldResult.Task.WaitAsync(token);
            pending++;
            try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Unreachable."); }
            catch (OperationCanceledException) { canceled++; throw; }
            finally { pending--; }
        }
        var viewer = new ArtworkViewerWindow([oldItem, newItem, pendingItem], 0, Load)
        { ShowActivated = false, ShowInTaskbar = false };
        viewer.Show();
        try
        {
            await UntilStableAsync(() => pending == 1, "Viewer preloading did not start.");
            ((Button)viewer.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var displayed = (Image)viewer.FindName("ArtworkImage");
            await UntilStableAsync(() => ReferenceEquals(displayed.Source, newImage), "Viewer did not switch to newer image.");
            oldResult.SetResult(new(oldImage, 1200, 600));
            await UntilStableAsync(() => oldItem.LoadTask?.IsCompleted == true, "Late image did not finish.");
            if (!ReferenceEquals(displayed.Source, newImage))
                throw new InvalidOperationException("Late image replaced the currently selected viewer image.");
        }
        finally { viewer.Close(); }
        await UntilStableAsync(() => pending == 0 && canceled == 1 && pendingItem.LoadTask is null,
            "Viewer close did not settle its image requests.");
    }

    private sealed class DelayedPreviewHandler : HttpMessageHandler
    {
        private readonly byte[] _slow = StabilityImage(1200, 600);
        private readonly byte[] _fast = StabilityImage(900, 600);
        private readonly byte[] _switched = StabilityImage(1000, 500);
        public int Pending { get; private set; }
        public int Canceled { get; private set; }
        public int SlowCalls { get; private set; }
        public int SwitchCalls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "stability.invalid") throw new InvalidOperationException("Unexpected preview network request.");
            var slow = request.RequestUri.AbsolutePath == "/slow.png";
            var switched = request.RequestUri.AbsolutePath == "/switch.png";
            if ((slow && ++SlowCalls == 1) || (switched && ++SwitchCalls == 1))
            {
                Pending++;
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Canceled++; throw; }
                finally { Pending--; }
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(slow ? _slow : switched ? _switched : _fast) };
        }
    }
}
