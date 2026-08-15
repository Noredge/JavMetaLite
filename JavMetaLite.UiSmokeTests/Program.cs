using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using JavMetaLite.App;
using JavMetaLite.Core.Models;

namespace JavMetaLite.UiSmokeTests;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application();
        var window = new MainWindow();
        window.Show();

        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (handle == IntPtr.Zero || !window.IsVisible || window.Title != "JAV Metadata Lite")
        {
            throw new InvalidOperationException("主窗口未成功创建。 ");
        }

        var onePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nQAAAABJRU5ErkJggg==");
        var poster = PosterBitmapFactory.CreateFrozen(onePixelPng);
        if (!poster.IsFrozen || poster.PixelWidth != 1 || poster.PixelHeight != 1)
        {
            throw new InvalidOperationException("封面 Bitmap 未能安全冻结。 ");
        }

        var sourceComboBox = window.FindName("SourceComboBox") as ComboBox
            ?? throw new InvalidOperationException("没有找到来源选择框。 ");
        sourceComboBox.IsDropDownOpen = true;
        window.UpdateLayout();
        var firstItem = sourceComboBox.Items[0] as ComboBoxItem
            ?? throw new InvalidOperationException("来源选择项未创建。 ");
        if (firstItem.Foreground is not SolidColorBrush foreground || foreground.Color.R < 200 ||
            firstItem.Background is not SolidColorBrush background || background.Color.R > 40)
        {
            throw new InvalidOperationException("来源选择框没有应用深色下拉样式。 ");
        }
        sourceComboBox.IsDropDownOpen = false;

        if (sourceComboBox.Items.OfType<ComboBoxItem>().All(item => item.Tag?.ToString() != "libredmm"))
        {
            throw new InvalidOperationException("来源选择框没有 LibreDMM。 ");
        }
        if (window.FindName("FanartImage") is not System.Windows.Controls.Image ||
            window.FindName("DownloadExtrafanartCheckBox") is not CheckBox)
        {
            throw new InvalidOperationException("v0.3 图片预览或 extrafanart 选项未创建。 ");
        }
        if (window.FindName("OrganizeFolderCheckBox") is not CheckBox organizeCheckBox || organizeCheckBox.IsChecked == true ||
            window.FindName("RenameVideoCheckBox") is not CheckBox renameCheckBox || renameCheckBox.IsChecked == true)
        {
            throw new InvalidOperationException("v0.4 整理选项未创建或没有保持安全的默认关闭状态。 ");
        }
        if (window.FindName("SaveButton") is not Button saveButton || saveButton.Content?.ToString() != "预览并保存")
        {
            throw new InvalidOperationException("v0.4 保存前预览入口未创建。 ");
        }

        var previewPlan = new SavePlan(
            "C:\\Media\\source.mp4",
            "C:\\Media\\IPX-123\\IPX-123.mp4",
            "C:\\Media\\IPX-123",
            "IPX-123",
            new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, false),
            new OrganizationOptions(true, true),
            [new PlannedFileChange(PlannedChangeKind.CreateFile, "生成 metadata", "C:\\Media\\IPX-123\\IPX-123.nfo")],
            [],
            []);
        var previewWindow = new SavePreviewWindow(previewPlan) { Owner = window };
        previewWindow.Show();
        previewWindow.UpdateLayout();
        if (!previewWindow.IsVisible || previewWindow.FindName("ConfirmButton") is not Button { IsEnabled: true })
        {
            throw new InvalidOperationException("v0.4 保存预览窗口未成功创建。 ");
        }
        previewWindow.Close();

        Console.WriteLine($"UI PASS  handle={handle} visible={window.IsVisible} title={window.Title} posterFrozen={poster.IsFrozen} comboDark=True libreDmm=True fanart=True preview=True previewWindow=True organizeDefaultsOff=True");
        window.Close();
        application.Shutdown();
    }
}
