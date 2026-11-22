using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JavMetaLite.App;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static int RunTransferCloseChecks(string outputPath)
    {
        var output = Directory.CreateDirectory(outputPath);
        var input = Directory.CreateTempSubdirectory("JavMetaLite-transfer-close-");
        var target = output.CreateSubdirectory("transfer-" + Guid.NewGuid().ToString("N"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative)
        });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            if (Path.GetPathRoot(input.FullName) == Path.GetPathRoot(target.FullName))
                throw new InvalidOperationException("Specify a screenshot directory on a different drive from TEMP.");
            foreach (var theme in new[] { "light", "dark" })
                CheckBatchTransferProgress(input.CreateSubdirectory(theme), target.CreateSubdirectory(theme), output, theme);
            foreach (var choice in new[] { "continue", "stop", "finish" })
                TryCloseWhileCopying(input.CreateSubdirectory(choice), target.CreateSubdirectory(choice), output, choice);
        }
        finally
        {
            app.Shutdown();
            input.Delete(true);
            target.Delete(true);
        }
        return 0;
    }

    private static void CheckBatchTransferProgress(DirectoryInfo input, DirectoryInfo target, DirectoryInfo screenshots, string theme)
    {
        var ids = new[] { "AAA-101", "BBB-102-CD1", "BBB-102-CD2" };
        var paths = ids.Select(id => Path.Combine(input.FullName, id + ".mp4")).ToArray();
        for (var index = 0; index < paths.Length; index++)
        {
            using var file = File.Create(paths[index]);
            file.SetLength((index == 1 ? 32L : 16L) * 1024 * 1024);
        }
        var window = new MainWindow(TestPreferences());
        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        var bar = Find<ProgressBar>("OperationProgressBar");
        var status = Find<TextBlock>("StatusText");
        var percent = Find<TextBlock>("ProgressPercent");
        var panel = Find<StackPanel>("OperationProgressPanel");
        var values = new List<double>();
        var stages = new List<(string Label, double Value)>();
        var full = theme == "light";
        window.Show();
        try
        {
            WindowVisualTheme.SetAppearance(theme);
            var language = Find<ComboBox>("LanguageComboBox");
            language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "zh-Hans"));
            WaitForTask((Task)typeof(MainWindow).GetMethod("AddMovieFilesAsync", PrivateInstance)!.Invoke(window, [paths])!);
            Find<ComboBox>("TargetModeComboBox").SelectedIndex = 2;
            Find<TextBox>("CustomRootTextBox").Text = target.FullName;
            foreach (var name in new[] { "DownloadPosterCheckBox", "DownloadFanartCheckBox", "DownloadExtrafanartCheckBox" })
                Find<CheckBox>(name).IsChecked = false;
            Find<CheckBox>("SkipSavePreviewCheckBox").IsChecked = true;
            Find<CheckBox>("SkipCrossVolumeVerificationCheckBox").IsChecked = !full;
            ClickWorkspaceButton(window, "ApplySelectionSettingsButton");
            if (panel.IsVisible) throw new InvalidOperationException("Idle footer shows progress.");
            var done = new TaskCompletionSource();
            panel.IsVisibleChanged += (_, e) => { if (Equals(e.NewValue, false)) done.TrySetResult(); };
            var label = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            void Observe(object? sender, EventArgs args) => stages.Add((status.Text, bar.Value));
            label.AddValueChanged(status, Observe);
            bool snapshot = false;
            bar.ValueChanged += (_, e) =>
            {
                values.Add(e.NewValue);
                if (!snapshot && e.NewValue is >= 40 and <= 70)
                {
                    snapshot = true;
                    window.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () =>
                    {
                        if (!panel.IsVisible || bar.IsIndeterminate || percent.Text != $"{bar.Value:0}%")
                            throw new InvalidOperationException("The footer percentage is missing or disagrees with the bar.");
                        window.UpdateLayout();
                        var footer = Find<Border>("StatusFooter");
                        var tile = new System.Windows.Shapes.Rectangle
                        {
                            Width = footer.ActualWidth, Height = footer.ActualHeight,
                            Fill = new System.Windows.Media.VisualBrush(footer)
                        };
                        tile.Measure(footer.RenderSize);
                        tile.Arrange(new Rect(footer.RenderSize));
                        SaveReadabilitySnapshot(tile, Path.Combine(screenshots.FullName, $"progress-{theme}.png"));
                    });
                }
            };
            try
            {
                ClickWorkspaceButton(window, "SaveSelectedButton");
                WaitForTask(done.Task.WaitAsync(TimeSpan.FromSeconds(75)));
            }
            finally { label.RemoveValueChanged(status, Observe); }
            var next = stages.FirstOrDefault(s => s.Label.Contains("BBB-102") && s.Label.Contains("复制影片"));
            if (!snapshot || values.Count < 4 || !values.SequenceEqual(values.Order()) || values[^1] != 100 ||
                next.Value is < 25 or > 27 || panel.IsVisible || paths.Any(File.Exists) ||
                Directory.GetFiles(target.FullName, "*.mp4", SearchOption.AllDirectories).Length != 3 ||
                stages.Any(s => s.Label.Contains("复核来源")) != full)
                throw new InvalidOperationException($"Incorrect aggregate transfer progress: {theme}, movie two starts at {next.Value}%, max={values.LastOrDefault()}.");
            Console.WriteLine($"PASS {theme}: two movies, three parts, unequal sizes, second movie at {next.Value}%, monotonic through 100 and hidden at rest.");
        }
        finally { window.Close(); }
    }

    private static void TryCloseWhileCopying(DirectoryInfo input, DirectoryInfo target, DirectoryInfo screenshots, string choice)
    {
        bool stop = choice == "stop";
        string Movie(string id) => Path.Combine(input.FullName, id + ".mp4");
        string Destination(string id) => Path.Combine(target.FullName, id, id + ".mp4");
        static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        var movie = Movie("DEMO-859");
        var block = new byte[1024 * 1024];
        new Random(71).NextBytes(block);
        using (var stream = File.Create(movie))
            for (int index = 0; index < 128; index++) stream.Write(block);
        var originalHash = Hash(movie);
        string[] paths = stop ? [Movie("ABC-101"), movie, Movie("ZZZ-103")] : [movie];
        if (stop)
        {
            File.WriteAllBytes(paths[0], [1, 2, 3]);
            File.WriteAllBytes(paths[2], [4, 5, 6]);
        }
        var window = new MainWindow(TestPreferences());
        window.Show();
        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        var status = Find<TextBlock>("StatusText");
        var text = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        bool requested = false, sawPrompt = false, closed = false;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(5) };
        window.Closed += (_, _) => closed = true;
        void OnProgress(object? sender, EventArgs args)
        {
            if (requested || !status.Text.Contains("正在复制影片", StringComparison.Ordinal) ||
                stop && !status.Text.Contains("DEMO-859", StringComparison.Ordinal)) return;
            requested = true;
            window.Close();
            window.Close(); // Repeated WM_CLOSE must share the pending decision.
        }
        Exception? dialogError = null;
        timer.Tick += (_, _) =>
        {
            var dialog = window.OwnedWindows.OfType<AppDialogWindow>().SingleOrDefault(d => d.IsVisible);
            if (dialog == null) return;
            timer.Stop();
            try
            {
                sawPrompt = true;
                if (!dialog.CancelButton.IsDefault || dialog.CancelButton.Content?.ToString() != "继续复制" ||
                    dialog.ConfirmButton.Content?.ToString() != "停止并关闭")
                    throw new InvalidOperationException("Transfer prompt actions or default focus incorrect.");
                if (choice == "continue") SaveReadabilitySnapshot(dialog, Path.Combine(screenshots.FullName, "close-transfer.png"));
                if (choice != "finish")
                    (stop ? dialog.ConfirmButton : dialog.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception e) { dialogError = e; if (dialog.IsVisible) dialog.Close(); }
        };
        try
        {
            var language = Find<ComboBox>("LanguageComboBox");
            language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "zh-Hans"));
            WindowVisualTheme.SetAppearance(stop ? "dark" : "light");
            WaitForTask((Task)typeof(MainWindow).GetMethod("AddMovieFilesAsync", PrivateInstance)!.Invoke(window, [paths])!);
            Find<ComboBox>("TargetModeComboBox").SelectedIndex = 2;
            Find<TextBox>("CustomRootTextBox").Text = target.FullName;
            Find<CheckBox>("WriteNfoCheckBox").IsChecked = true;
            foreach (var name in new[] { "DownloadPosterCheckBox", "DownloadFanartCheckBox", "DownloadExtrafanartCheckBox" })
                Find<CheckBox>(name).IsChecked = false;
            Find<CheckBox>("SkipSavePreviewCheckBox").IsChecked = true;
            Find<CheckBox>("SkipCrossVolumeVerificationCheckBox").IsChecked = stop;
            if (stop)
            {
                ClickWorkspaceButton(window, "ApplySelectionSettingsButton");
                ((ListBox)window.FindName("MovieQueueList")).SelectedIndex = 1;
                WaitForCondition(() => ((TextBox)window.FindName("IdTextBox")).Text == "DEMO-859", "ID did not switch.");
            }
            var id = Find<TextBox>("IdTextBox");
            window.UpdateLayout();
            if (id.ExtentWidth > id.ViewportWidth + 1)
                throw new InvalidOperationException("The ID field clips DEMO-859 at the default width.");
            if (Find<TextBlock>("TargetPathHintText").Text.Contains("风险自负"))
                throw new InvalidOperationException("Fast copy hint still has the removed suffix.");
            text.AddValueChanged(status, OnProgress);
            timer.Start();
            ClickWorkspaceButton(window, stop ? "SaveSelectedButton" : "SaveButton");
            WaitForCondition(() => !IsWindowBusy(window) && (!stop || closed), "Copy or requested close did not finish.");
            var progressBar = Find<ProgressBar>("OperationProgressBar");
            if (Find<StackPanel>("OperationProgressPanel").Visibility != Visibility.Collapsed ||
                (stop ? progressBar.Value >= 100 : progressBar.Value != 100))
                throw new InvalidOperationException("Transfer reported the wrong final percentage or kept a stale progress bar.");
            if (dialogError != null) throw dialogError;
            if (!sawPrompt || closed != stop || window.OwnedWindows.OfType<AppDialogWindow>().Any(d => d.IsVisible))
                throw new InvalidOperationException($"Transfer prompt did not respect {choice}.");
            if (stop)
            {
                if (Hash(movie) != originalHash || File.Exists(Destination("DEMO-859")) ||
                    !File.ReadAllBytes(Destination("ABC-101")).SequenceEqual(new byte[] { 1, 2, 3 }) ||
                    !File.ReadAllBytes(paths[2]).SequenceEqual(new byte[] { 4, 5, 6 }) ||
                    !Directory.EnumerateFiles(target.FullName, "*", SearchOption.AllDirectories)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        .SetEquals([Destination("ABC-101"), Path.ChangeExtension(Destination("ABC-101"), ".nfo")]))
                    throw new InvalidOperationException("Cancel did not preserve committed and remaining files or clean up staging.");
            }
            else if (File.Exists(movie) || Hash(Destination("DEMO-859")) != originalHash)
                throw new InvalidOperationException("Completed transfer did not preserve file contents.");
            Console.WriteLine($"PASS real cross-drive copy: {choice}, prompt={sawPrompt}, closed={closed}, verified file contents");
        }
        finally
        {
            text.RemoveValueChanged(status, OnProgress);
            timer.Stop();
            if (window.IsVisible) window.Close();
        }
    }
}
