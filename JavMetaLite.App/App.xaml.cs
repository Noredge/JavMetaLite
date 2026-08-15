using System.IO;
using System.Windows;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Info("应用启动流程开始");

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            AppLog.Error("应用启动失败", exception);
            var logPath = AppLog.CurrentLogPath;

            MessageBox.Show(
                $"JAV Metadata Lite 启动失败。\n\n错误记录：{logPath}\n\n{exception.Message}",
                "JAV Metadata Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
