using System.Windows;
using System.Windows.Threading;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void SetLayoutTestWidth(Window window, double width)
    {
        // A small desktop can clamp the HWND below Window.Width. Pin only this
        // test host's minimum so assertions exercise the requested layout size.
        // WPF gives MinWidth precedence over MaxWidth and native tracking limits.
        window.MinWidth = width;
        window.Width = width;
        window.UpdateLayout();
        if (Math.Abs(window.ActualWidth - width) > 0.5)
            throw new InvalidOperationException($"Layout host width mismatch: requested={width}, actual={window.ActualWidth}.");
    }

    private static void TestRestrictedLayoutHost()
    {
        var window = new Window { Width = 1120, MinWidth = 930, MaxWidth = 1040, Height = 200 };
        window.Show();
        try
        {
            window.UpdateLayout();
            if (window.ActualWidth > 1040.5)
                throw new InvalidOperationException("Restricted layout fixture did not constrain the original request.");
            foreach (var width in new[] { 1120d, 930d, 1320d, 1000d })
                SetLayoutTestWidth(window, width);
            Console.WriteLine("UI PASS restrictedLayoutHost=True requestedWidthsActuallyMeasured=True");
        }
        finally { window.Close(); }
    }

    private static int RunLayoutTests()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative) });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        TestRestrictedLayoutHost();
        foreach (var maximumWidth in new[] { double.PositiveInfinity, 1040d })
        {
            TestSearchToolbarLayout(maximumWidth);
            TestReadabilityCloseout(maximumWidth);
        }
        application.Shutdown();
        return 0;
    }
}
