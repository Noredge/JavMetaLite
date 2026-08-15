using System.IO;
using System.Windows;

namespace JavMetaLite.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JavMetaLite");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "startup-error.log");
            File.WriteAllText(logPath, exception.ToString());

            MessageBox.Show(
                $"JAV Metadata Lite 启动失败。\n\n错误记录：{logPath}\n\n{exception.Message}",
                "JAV Metadata Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
