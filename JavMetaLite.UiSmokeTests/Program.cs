using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Reflection;
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
        if (window.FindName("DirectSaveOverwriteCheckBox") is not CheckBox directSaveCheckBox ||
            directSaveCheckBox.IsChecked == true ||
            directSaveCheckBox.Content?.ToString() != "直接保存并覆盖（跳过预览）")
        {
            throw new InvalidOperationException("v0.4 直接保存选项未创建或没有保持安全的默认关闭状态。 ");
        }
        if (window.FindName("SaveButton") is not Button saveButton || saveButton.Content?.ToString() != "保存")
        {
            throw new InvalidOperationException("v0.4 保存入口未创建。 ");
        }

        var titleSourceText = window.FindName("TitleSourceText") as Button
            ?? throw new InvalidOperationException("v0.5 标题来源标记未创建。 ");
        var directorSourceText = window.FindName("DirectorSourceText") as Button
            ?? throw new InvalidOperationException("v0.5 导演来源标记未创建。 ");
        if (titleSourceText.Visibility != Visibility.Collapsed || directorSourceText.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("没有资料时来源标记应保持隐藏。 ");
        }

        var primaryMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "日文标题",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var fallbackMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "English title",
            Director = "Director A",
            SourceName = "r18dev",
            SourceDisplayName = "R18.dev"
        };
        var mergedMetadata = JavMetaLite.Core.Services.MetadataMerger.Merge(primaryMetadata, fallbackMetadata);
        var applyMetadata = typeof(MainWindow).GetMethod("ApplyMetadata", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.5 metadata 应用入口未找到。 ");
        applyMetadata.Invoke(window, [mergedMetadata, new MovieMetadata[] { primaryMetadata, fallbackMetadata }]);
        window.UpdateLayout();
        if (titleSourceText.Content?.ToString() != "LibreDMM ▾" ||
            directorSourceText.Content?.ToString() != "R18.dev" ||
            titleSourceText.Visibility != Visibility.Visible || directorSourceText.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("自动补全没有显示正确的字段来源。 ");
        }
        if (!titleSourceText.IsEnabled || directorSourceText.IsEnabled)
        {
            throw new InvalidOperationException("多来源字段应可选，单一来源字段应保持只读。 ");
        }

        titleSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var titleMenu = titleSourceText.ContextMenu
            ?? throw new InvalidOperationException("标题候选菜单未创建。 ");
        titleMenu.ApplyTemplate();
        window.UpdateLayout();
        var candidateMenuRoot = titleMenu.Template.FindName("CandidateMenuRoot", titleMenu) as Border;
        if (!titleMenu.OverridesDefaultStyle || titleMenu.HasDropShadow ||
            candidateMenuRoot?.Background is not SolidColorBrush menuBackground ||
            menuBackground.Color != Color.FromRgb(16, 22, 30))
        {
            throw new InvalidOperationException("候选菜单没有完全替换系统白色菜单模板。 ");
        }
        var titleCandidates = titleMenu.Items.OfType<MenuItem>().ToArray();
        if (titleCandidates.Length != 2 ||
            titleCandidates.Any(item => item.Header is not StackPanel panel || panel.Children.Count != 2))
        {
            throw new InvalidOperationException("标题候选菜单没有同时显示来源与值预览。 ");
        }

        var r18Title = titleCandidates.FirstOrDefault(item =>
            item.Tag is MetadataFieldCandidate candidate && candidate.Source.Name == "r18dev")
            ?? throw new InvalidOperationException("标题候选菜单缺少 R18.dev。 ");
        r18Title.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (mergedMetadata.Title != "English title" || titleSourceText.Content?.ToString() != "R18.dev ▾")
        {
            throw new InvalidOperationException("没有只切换标题字段的 R18.dev 候选。 ");
        }

        mergedMetadata.Director = "手动修正";
        if (directorSourceText.Content?.ToString() != "手动编辑 ▾" || !directorSourceText.IsEnabled)
        {
            throw new InvalidOperationException("手动修改字段后来源标记没有更新。 ");
        }

        directorSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var directorMenu = directorSourceText.ContextMenu
            ?? throw new InvalidOperationException("导演候选菜单未创建。 ");
        var r18Director = directorMenu.Items.OfType<MenuItem>().FirstOrDefault(item =>
            item.Tag is MetadataFieldCandidate candidate && candidate.Source.Name == "r18dev")
            ?? throw new InvalidOperationException("手动修改后无法返回 R18.dev 候选。 ");
        r18Director.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (mergedMetadata.Director != "Director A" || directorSourceText.Content?.ToString() != "R18.dev ▾")
        {
            throw new InvalidOperationException("没有恢复导演字段的 R18.dev 候选。 ");
        }

        directorSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var manualDirector = directorSourceText.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item =>
            item.Tag is MetadataFieldCandidate candidate && candidate.Source.IsManual)
            ?? throw new InvalidOperationException("切回来源后没有保留最近一次手动候选。 ");
        manualDirector.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (mergedMetadata.Director != "手动修正" || directorSourceText.Content?.ToString() != "手动编辑 ▾")
        {
            throw new InvalidOperationException("无法恢复最近一次手动编辑值。 ");
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

        Console.WriteLine($"UI PASS  handle={handle} visible={window.IsVisible} title={window.Title} posterFrozen={poster.IsFrozen} comboDark=True libreDmm=True fanart=True previewWindow=True sourceBadges=True candidateMenus=True fullDarkMenuTemplate=True fieldSwitch=True manualReturn=True directSaveDefaultsOff=True organizeDefaultsOff=True");
        window.Close();
        application.Shutdown();
    }
}
