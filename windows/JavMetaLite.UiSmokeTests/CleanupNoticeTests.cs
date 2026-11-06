using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestCleanupNotices()
    {
        foreach (var recoveryFailed in new[] { false, true })
        {
            var window = new MainWindow();
            window.Show();
            var path = @"C:\Synthetic\.JavMetaLite-test.tmp";
            Exception error = recoveryFailed
                ? new FileRecoveryException("Synthetic incomplete recovery", [path], new IOException("Device unavailable"))
                : new TemporaryCleanupException([new TemporaryCleanupIssue(path, "Cleanup not confirmed")],
                    new IOException("Device unavailable"));
            var sawNotice = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<DiscoveryDetailsWindow>().FirstOrDefault(w => w.Owner == window);
                if (dialog is null) return;
                var text = ((TextBox)dialog.FindName("DetailsText")).Text;
                sawNotice = text.Contains(path) && text.Contains("Device unavailable") &&
                    text.Contains(LocalizationService.Get(recoveryFailed ? "Cleanup.RecoveryFailed" : "Cleanup.Incomplete"));
                dialog.Close();
            };
            timer.Start();
            try
            {
                Func<Task> operation = () =>
                {
                    ((CancellationTokenSource)typeof(MainWindow).GetField("_activeOperationCancellation", PrivateInstance)!
                        .GetValue(window)!).Cancel();
                    typeof(MainWindow).GetField("_closeRequested", PrivateInstance)!.SetValue(window, true);
                    return Task.FromException(error);
                };
                var task = (Task)typeof(MainWindow).GetMethod("RunBusyAsync", PrivateInstance)!
                    .Invoke(window, ["Synthetic save interruption", operation])!;
                WaitForTask(task);
                if (!sawNotice || !window.IsVisible ||
                    (bool)typeof(MainWindow).GetField("_closeRequested", PrivateInstance)!.GetValue(window)!)
                    throw new InvalidOperationException("Cancellation must not hide cleanup/recovery failure or close the window.");
            }
            finally { timer.Stop(); window.Close(); }
        }
        Console.WriteLine("UI PASS cleanupPathsVisible=True deviceErrorPreserved=True recoveryDistinct=True canceledFailureKeepsWindowOpen=True");
    }
}
