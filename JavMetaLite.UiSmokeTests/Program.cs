using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application();
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
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
        if (firstItem.Tag?.ToString() != "auto" || firstItem.Content?.ToString() != "多来源搜索（推荐）")
        {
            throw new InvalidOperationException("v0.5 默认来源没有更新为多来源搜索。 ");
        }
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
        if (window.FindName("FanartHintText") is not TextBlock fanartHintText ||
            fanartHintText.Visibility != Visibility.Visible ||
            fanartHintText.Text != string.Empty || fanartHintText.MinHeight < 14 ||
            window.FindName("FanartDropHint") is not null)
        {
            throw new InvalidOperationException("完整封套尚未加载时应保留无文字的固定间距。 ");
        }
        var initialPosterPreviewBorder = window.FindName("PosterPreviewBorder") as Border
            ?? throw new InvalidOperationException("封套预览区域未创建。 ");
        var fanartPreviewBorder = window.FindName("FanartPreviewBorder") as Border
            ?? throw new InvalidOperationException("Fanart 预览区域未创建。 ");
        window.UpdateLayout();
        var posterBottom = initialPosterPreviewBorder.TranslatePoint(new Point(0, initialPosterPreviewBorder.ActualHeight), window).Y;
        var gapBeforeDimensions = fanartPreviewBorder.TranslatePoint(new Point(0, 0), window).Y - posterBottom;
        fanartHintText.Text = "横板封套：2184×1468";
        window.UpdateLayout();
        posterBottom = initialPosterPreviewBorder.TranslatePoint(new Point(0, initialPosterPreviewBorder.ActualHeight), window).Y;
        var gapAfterDimensions = fanartPreviewBorder.TranslatePoint(new Point(0, 0), window).Y - posterBottom;
        fanartHintText.Text = string.Empty;
        if (Math.Abs(gapBeforeDimensions - gapAfterDimensions) > 0.5)
        {
            throw new InvalidOperationException("搜索前后两个封套预览框的间距不一致。 ");
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
            CoverUrl = "https://images.example.test/libre-cover.jpg",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var fallbackMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "English title",
            Director = "Director A",
            CoverUrl = "https://images.example.test/r18-cover.jpg",
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

        var artworkSourceButton = window.FindName("ArtworkSourceButton") as Button
            ?? throw new InvalidOperationException("统一封套来源标记未创建。 ");
        if (artworkSourceButton.Visibility != Visibility.Visible ||
            !artworkSourceButton.IsEnabled ||
            artworkSourceButton.Content?.ToString() != "LibreDMM ▾")
        {
            throw new InvalidOperationException("多来源结果没有显示统一封套来源候选。 ");
        }
        var posterPreviewBorder = window.FindName("PosterPreviewBorder") as Border
            ?? throw new InvalidOperationException("封套预览区域未创建。 ");
        var artworkSourceHeader = window.FindName("ArtworkSourceHeader") as Grid
            ?? throw new InvalidOperationException("统一封套来源标题栏未创建。 ");
        if (!ReferenceEquals(artworkSourceButton.Parent, artworkSourceHeader) ||
            Grid.GetRow(artworkSourceHeader) >= Grid.GetRow(posterPreviewBorder) ||
            artworkSourceButton.HorizontalAlignment != HorizontalAlignment.Right)
        {
            throw new InvalidOperationException("统一封套来源没有固定在封套预览上方右侧。 ");
        }
        artworkSourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var artworkMenu = artworkSourceButton.ContextMenu
            ?? throw new InvalidOperationException("统一封套候选菜单未创建。 ");
        artworkMenu.ApplyTemplate();
        window.UpdateLayout();
        var artworkCandidates = artworkMenu.Items.OfType<MenuItem>().ToArray();
        if (artworkCandidates.Length != 2 ||
            artworkCandidates.Any(item => item.Tag is not ArtworkCoverCandidate || item.Header is not StackPanel panel || panel.Children.Count != 2))
        {
            throw new InvalidOperationException("封套候选没有复用字段候选的双行下拉结构。 ");
        }
        artworkMenu.IsOpen = false;

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

        var localTestRoot = Path.Combine(Path.GetTempPath(), $"JavMetaLite.UiLocalNfo.{Guid.NewGuid():N}");
        Directory.CreateDirectory(localTestRoot);
        AppLog.ConfigureDirectory(Path.Combine(localTestRoot, "logs"));
        var selectVideoAsync = typeof(MainWindow).GetMethod("SelectVideoAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.6 本地影片载入入口未找到。 ");
        var applyOnlineSources = typeof(MainWindow).GetMethod("ApplyOnlineSources", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.6 在线候选组合入口未找到。 ");

        var localVideoPath = Path.Combine(localTestRoot, "IPX-123.mp4");
        var localNfoPath = Path.Combine(localTestRoot, "IPX-123.nfo");
        File.WriteAllBytes(localVideoPath, [0x01, 0x02, 0x03]);
        File.WriteAllText(localNfoPath, """
            <movie custom="keep">
              <id>IPX-123</id>
              <title>本地 NFO 标题</title>
              <plot>本地简介</plot>
              <actor><name>本地演员</name><thumb>https://local.example/actor.jpg</thumb></actor>
              <unknown>keep me</unknown>
            </movie>
            """);
        WaitForTask((Task)(selectVideoAsync.Invoke(window, [localVideoPath])
            ?? throw new InvalidOperationException("本地影片载入没有返回任务。 ")));
        window.UpdateLayout();
        var localMetadata = window.DataContext as MovieMetadata
            ?? throw new InvalidOperationException("本地 NFO 没有进入编辑模型。 ");
        var statusText = window.FindName("StatusText") as TextBlock
            ?? throw new InvalidOperationException("状态栏未创建。 ");
        if (localMetadata.Title != "本地 NFO 标题" ||
            titleSourceText.Content?.ToString() != "本地 NFO" ||
            titleSourceText.Visibility != Visibility.Visible ||
            !statusText.Text.Contains("本地 NFO", StringComparison.Ordinal) ||
            !statusText.Text.Contains(localNfoPath, StringComparison.OrdinalIgnoreCase) ||
            saveButton.IsEnabled ||
            saveButton.ToolTip?.ToString()?.Contains("只读检查", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("本地 NFO 没有以明确的只读来源载入界面。 ");
        }

        var localLibre = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "LibreDMM 在线标题",
            Director = "在线导演",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var localR18 = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "R18 English title",
            Director = "Online director",
            SourceName = "r18dev",
            SourceDisplayName = "R18.dev"
        };
        var localOnlinePreferred = MetadataMerger.Merge(localLibre, localR18);
        var reviewedLocalMetadata = applyOnlineSources.Invoke(
            window,
            [localOnlinePreferred, new MovieMetadata[] { localLibre, localR18 }]) as MovieMetadata
            ?? throw new InvalidOperationException("在线候选没有加入本地编辑会话。 ");
        window.UpdateLayout();
        if (reviewedLocalMetadata.Title != "本地 NFO 标题" ||
            reviewedLocalMetadata.Director != "在线导演" ||
            titleSourceText.Content?.ToString() != "本地 NFO ▾" ||
            directorSourceText.Content?.ToString() != "LibreDMM ▾")
        {
            throw new InvalidOperationException("在线搜索改变了本地默认字段，或没有补齐本地空白字段。 ");
        }

        titleSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var localTitleCandidates = titleSourceText.ContextMenu?.Items.OfType<MenuItem>().ToArray()
            ?? throw new InvalidOperationException("本地标题候选菜单未创建。 ");
        if (localTitleCandidates.Length != 3 ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.Name: "local-nfo" }) ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.Name: "libredmm" }) ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.Name: "r18dev" }))
        {
            throw new InvalidOperationException("本地、LibreDMM 与 R18.dev 没有出现在同一字段候选菜单。 ");
        }
        titleSourceText.ContextMenu!.IsOpen = false;

        reviewedLocalMetadata.Title = "本地会话手动修正";
        if (titleSourceText.Content?.ToString() != "手动编辑 ▾")
        {
            throw new InvalidOperationException("本地会话中的手动修改没有成为候选。 ");
        }
        titleSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var localTitle = titleSourceText.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item =>
            item.Tag is MetadataFieldCandidate { Source.Name: "local-nfo" })
            ?? throw new InvalidOperationException("手动修改后无法返回本地 NFO 候选。 ");
        localTitle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        titleSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var localManualTitle = titleSourceText.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item =>
            item.Tag is MetadataFieldCandidate { Source.IsManual: true })
            ?? throw new InvalidOperationException("切回本地 NFO 后没有保留手动候选。 ");
        localManualTitle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (reviewedLocalMetadata.Title != "本地会话手动修正")
        {
            throw new InvalidOperationException("本地会话无法恢复最近一次手动值。 ");
        }

        var cleanVideoPath = Path.Combine(localTestRoot, "IPX-124.mp4");
        File.WriteAllBytes(cleanVideoPath, [0x04]);
        WaitForTask((Task)(selectVideoAsync.Invoke(window, [cleanVideoPath])
            ?? throw new InvalidOperationException("第二个影片载入没有返回任务。 ")));
        window.UpdateLayout();
        var cleanMetadata = window.DataContext as MovieMetadata
            ?? throw new InvalidOperationException("第二个影片没有编辑模型。 ");
        if (cleanMetadata.Id != "IPX-124" || cleanMetadata.Title.Length != 0 ||
            titleSourceText.Visibility != Visibility.Collapsed || !saveButton.IsEnabled || saveButton.ToolTip is not null)
        {
            throw new InvalidOperationException("选择新影片后残留了上一个影片的本地或在线候选。 ");
        }

        var invalidVideoPath = Path.Combine(localTestRoot, "IPX-125.mp4");
        var invalidNfoPath = Path.Combine(localTestRoot, "IPX-125.nfo");
        File.WriteAllBytes(invalidVideoPath, [0x05]);
        File.WriteAllText(invalidNfoPath, "<tvshow><title>错误根元素</title></tvshow>");
        WaitForTask((Task)(selectVideoAsync.Invoke(window, [invalidVideoPath])
            ?? throw new InvalidOperationException("无效 NFO 载入没有返回任务。 ")));
        window.UpdateLayout();
        if (!statusText.Text.Contains("无法安全读取", StringComparison.Ordinal) ||
            !statusText.Text.Contains("原文件未修改", StringComparison.Ordinal) ||
            saveButton.IsEnabled || titleSourceText.Visibility != Visibility.Collapsed ||
            !File.ReadAllText(AppLog.CurrentLogPath).Contains("本地 NFO 读取失败", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("无效 NFO 没有被安全阻止并记录日志。 ");
        }

        AppLog.ConfigureDirectory(null);
        Directory.Delete(localTestRoot, true);

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

        Console.WriteLine($"UI PASS  handle={handle} visible={window.IsVisible} title={window.Title} posterFrozen={poster.IsFrozen} comboDark=True multiSourceLabel=True libreDmm=True fanart=True previewWindow=True sourceBadges=True candidateMenus=True fullDarkMenuTemplate=True fieldSwitch=True manualReturn=True unifiedArtworkSource=True artworkMenu=True localNfoLoad=True localDefault=True localOnlineCandidates=True localManualReturn=True localFailureSafe=True staleCandidatesCleared=True directSaveDefaultsOff=True organizeDefaultsOff=True");
        window.Close();
        application.Shutdown();
    }

    private static void WaitForTask(Task task)
    {
        if (task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(
            _ => dispatcher.BeginInvoke(() => frame.Continue = false),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
