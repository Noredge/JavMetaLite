using System.IO;
using System.Globalization;
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
        var languageComboBox = window.FindName("LanguageComboBox") as ComboBox
            ?? throw new InvalidOperationException("没有找到语言选择框。 ");
        var searchButton = window.FindName("SearchButton") as Button
            ?? throw new InvalidOperationException("没有找到搜索按钮。 ");
        var languageExpectations = new[]
        {
            (Code: UiLanguageCodes.SimplifiedChinese, Search: "搜索资料", Save: "保存", AutoSource: "多来源搜索（推荐）"),
            (Code: UiLanguageCodes.TraditionalChinese, Search: "搜尋資料", Save: "儲存", AutoSource: "多來源搜尋（建議）"),
            (Code: UiLanguageCodes.English, Search: "Search", Save: "Save", AutoSource: "Multi-source search"),
            (Code: UiLanguageCodes.Japanese, Search: "検索", Save: "保存", AutoSource: "複数ソース検索（推奨）")
        };
        if (languageComboBox.Items.Count != languageExpectations.Length)
        {
            throw new InvalidOperationException("v0.9 语言选择框没有提供四种语言。 ");
        }
        HashSet<object>? baselineLocalizationKeys = null;
        foreach (var expectation in languageExpectations)
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri(
                    $"/JavMetaLite;component/Resources/Strings.{expectation.Code}.xaml",
                    UriKind.Relative)
            };
            var keys = dictionary.Keys.Cast<object>().ToHashSet();
            baselineLocalizationKeys ??= keys;
            if (!baselineLocalizationKeys.SetEquals(keys))
            {
                throw new InvalidOperationException($"v0.9 {expectation.Code} 语言资源键不完整。 ");
            }

            languageComboBox.SelectedItem = languageComboBox.Items
                .OfType<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == expectation.Code);
            window.UpdateLayout();
            if (searchButton.Content?.ToString() != expectation.Search ||
                window.FindName("SaveButton") is not Button languageSaveButton ||
                languageSaveButton.Content?.ToString() != expectation.Save ||
                sourceComboBox.Items[0] is not ComboBoxItem languageAutoSource ||
                languageAutoSource.Content?.ToString() != expectation.AutoSource ||
                string.IsNullOrWhiteSpace(languageAutoSource.ToolTip?.ToString()))
            {
                throw new InvalidOperationException($"v0.9 {expectation.Code} 没有即时更新主要界面文字。 ");
            }

            if (expectation.Code == UiLanguageCodes.English)
            {
                var sourceText = new FormattedText(
                    expectation.AutoSource,
                    CultureInfo.GetCultureInfo("en-US"),
                    FlowDirection.LeftToRight,
                    new Typeface(
                        sourceComboBox.FontFamily,
                        sourceComboBox.FontStyle,
                        sourceComboBox.FontWeight,
                        sourceComboBox.FontStretch),
                    sourceComboBox.FontSize,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(sourceComboBox).PixelsPerDip);
                var availableTextWidth = sourceComboBox.ActualWidth - 42;
                if (sourceText.WidthIncludingTrailingWhitespace > availableTextWidth)
                {
                    throw new InvalidOperationException("v0.9 英文来源名称会超出选择框的可见宽度。 ");
                }
            }
        }
        languageComboBox.SelectedItem = languageComboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == UiLanguageCodes.SimplifiedChinese);
        window.UpdateLayout();
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
        if (window.FindName("TargetModeComboBox") is not ComboBox targetModeComboBox ||
            targetModeComboBox.SelectedItem is not ComboBoxItem { Tag: "VideoDirectory" } ||
            window.FindName("CustomTargetPanel") is not Grid { Visibility: Visibility.Collapsed } ||
            window.FindName("CustomRootTextBox") is not TextBox ||
            window.FindName("ChooseTargetFolderButton") is not Button ||
            window.FindName("RecentRootsButton") is not Button { IsEnabled: false, Content: "最近目录" } ||
            window.FindName("TargetPathHintText") is not TextBlock { Text: "选择影片后显示最终路径" } ||
            window.FindName("OrganizeFolderCheckBox") is not null ||
            window.FindName("RenameVideoCheckBox") is not CheckBox renameCheckBox || renameCheckBox.IsChecked == true)
        {
            throw new InvalidOperationException("dev2 目标位置控件未创建或没有保持安全默认值。 ");
        }
        if (window.FindName("DirectSaveOverwriteCheckBox") is not CheckBox directSaveCheckBox ||
            directSaveCheckBox.IsChecked == true ||
            directSaveCheckBox.Content?.ToString() != "直接保存并覆盖（跳过预览）")
        {
            throw new InvalidOperationException("v0.4 直接保存选项未创建或没有保持安全的默认关闭状态。 ");
        }
        if (window.FindName("RememberPreferencesCheckBox") is not CheckBox rememberPreferencesCheckBox ||
            rememberPreferencesCheckBox.IsChecked == true ||
            rememberPreferencesCheckBox.Content?.ToString() != "记住保存偏好")
        {
            throw new InvalidOperationException("v0.8 安全偏好开关未创建或没有保持默认关闭。 ");
        }

        var applyPreferences = typeof(MainWindow).GetMethod(
            "ApplyPreferences",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.8 偏好应用入口未找到。 ");
        var capturePreferences = typeof(MainWindow).GetMethod(
            "CapturePreferences",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.8 偏好采集入口未找到。 ");
        var preferencesRoot = Path.Combine(Path.GetTempPath(), "JavMetaLite remembered library");
        var secondPreferencesRoot = Path.Combine(Path.GetTempPath(), "JavMetaLite remembered library 2");
        directSaveCheckBox.IsChecked = true;
        applyPreferences.Invoke(window, [new AppPreferences
        {
            UiLanguage = UiLanguageCodes.SimplifiedChinese,
            RememberSavePreferences = true,
            DirectSaveOverwrite = true,
            TargetMode = OrganizationTargetMode.CustomRootNumberFolder,
            CustomRootDirectory = preferencesRoot,
            RecentCustomRootDirectories = [secondPreferencesRoot, preferencesRoot],
            RenameVideo = true,
            WriteNfo = false,
            DownloadPoster = false,
            DownloadFanart = true,
            DownloadExtrafanart = true
        }]);
        window.UpdateLayout();
        var remembered = capturePreferences.Invoke(window, null) as AppPreferences
            ?? throw new InvalidOperationException("v0.8.1 无法采集保存偏好。 ");
        var recentRootsButton = (Button)window.FindName("RecentRootsButton");
        if (directSaveCheckBox.IsChecked != true ||
            rememberPreferencesCheckBox.IsChecked != true ||
            !remembered.DirectSaveOverwrite ||
            remembered.TargetMode != OrganizationTargetMode.CustomRootNumberFolder ||
            remembered.CustomRootDirectory != preferencesRoot ||
            !remembered.RenameVideo || remembered.WriteNfo || remembered.DownloadPoster ||
            !remembered.DownloadFanart || !remembered.DownloadExtrafanart ||
            remembered.RecentCustomRootDirectories.Length != 2 ||
            remembered.UiLanguage != UiLanguageCodes.SimplifiedChinese ||
            recentRootsButton.Content?.ToString() != "最近目录 (2) ▾" ||
            !recentRootsButton.IsEnabled ||
            window.FindName("CustomTargetPanel") is not Grid { Visibility: Visibility.Visible } ||
            typeof(AppPreferences).GetProperty("DirectSaveOverwrite") is null)
        {
            throw new InvalidOperationException("v0.8.1 没有恢复用户明确记住的保存偏好。 ");
        }

        recentRootsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var recentRootsMenu = recentRootsButton.ContextMenu
            ?? throw new InvalidOperationException("v0.8 最近目录菜单未创建。 ");
        recentRootsMenu.ApplyTemplate();
        window.UpdateLayout();
        if (recentRootsMenu.Items.Count != 4 ||
            recentRootsMenu.Items.OfType<MenuItem>().Any(item =>
                item.Style != window.FindResource("CandidateMenuItem")))
        {
            throw new InvalidOperationException("v0.8 最近目录菜单没有使用紧凑深色结构。 ");
        }
        var secondRootItem = recentRootsMenu.Items.OfType<MenuItem>().First(item =>
            string.Equals(item.Tag?.ToString(), secondPreferencesRoot, StringComparison.OrdinalIgnoreCase));
        secondRootItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var preferenceRootTextBox = (TextBox)window.FindName("CustomRootTextBox");
        if (!string.Equals(preferenceRootTextBox.Text, secondPreferencesRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("v0.8 无法选择最近目标根目录。 ");
        }

        recentRootsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var removeCurrentItem = recentRootsButton.ContextMenu?.Items.OfType<MenuItem>().First(item =>
            item.Tag?.ToString() == "remove-current")
            ?? throw new InvalidOperationException("v0.8 最近目录菜单缺少单条移除。 ");
        removeCurrentItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (recentRootsButton.Content?.ToString() != "最近目录 (1) ▾" ||
            preferenceRootTextBox.Text != secondPreferencesRoot)
        {
            throw new InvalidOperationException("v0.8 移除单条记录时不应清空当前路径。 ");
        }

        recentRootsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var clearRootsItem = recentRootsButton.ContextMenu?.Items.OfType<MenuItem>().First(item =>
            item.Tag?.ToString() == "clear-all")
            ?? throw new InvalidOperationException("v0.8 最近目录菜单缺少清空入口。 ");
        clearRootsItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (recentRootsButton.IsEnabled || recentRootsButton.Content?.ToString() != "最近目录")
        {
            throw new InvalidOperationException("v0.8 清空最近目录后按钮状态不正确。 ");
        }
        applyPreferences.Invoke(window, [AppPreferences.CreateSafeDefaults() with
        {
            UiLanguage = UiLanguageCodes.SimplifiedChinese
        }]);
        window.UpdateLayout();
        if (directSaveCheckBox.IsChecked == true)
        {
            throw new InvalidOperationException("v0.8.1 安全默认值没有关闭直接保存并覆盖。 ");
        }
        if (window.FindName("SaveButton") is not Button saveButton || saveButton.Content?.ToString() != "保存")
        {
            throw new InvalidOperationException("v0.4 保存入口未创建。 ");
        }
        if (window.FindName("CancelOperationButton") is not Button { Visibility: Visibility.Collapsed })
        {
            throw new InvalidOperationException("dev3 取消操作按钮没有保持默认隐藏。 ");
        }
        window.UpdateLayout();
        var directSaveY = directSaveCheckBox.TranslatePoint(new Point(0, 0), window).Y;
        var renameVideoY = renameCheckBox.TranslatePoint(new Point(0, 0), window).Y;
        var targetModeY = targetModeComboBox.TranslatePoint(new Point(0, 0), window).Y;
        if (Math.Abs(directSaveY - renameVideoY) > 3 || targetModeY <= renameVideoY)
        {
            throw new InvalidOperationException("dev3 影片重命名选项没有移动到保存方式一行。 ");
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
        var handleStartupVideoRequestAsync = typeof(MainWindow).GetMethod(
            "HandleStartupVideoRequestAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.8 启动影片载入入口未找到。 ");
        var applyOnlineSources = typeof(MainWindow).GetMethod("ApplyOnlineSources", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.6 在线候选组合入口未找到。 ");

        var localVideoPath = Path.Combine(localTestRoot, "IPX-123.mp4");
        var localNfoPath = Path.Combine(localTestRoot, "IPX-123.nfo");
        var localPosterPath = Path.Combine(localTestRoot, "IPX-123-poster.png");
        var localFanartPath = Path.Combine(localTestRoot, "IPX-123-fanart.png");
        File.WriteAllBytes(localVideoPath, [0x01, 0x02, 0x03]);
        File.WriteAllBytes(localPosterPath, onePixelPng);
        File.WriteAllBytes(localFanartPath, onePixelPng);
        File.WriteAllText(localNfoPath, """
            <movie custom="keep">
              <id>IPX-123</id>
              <title>本地 NFO 标题</title>
              <plot>本地简介</plot>
              <actor><name>本地演员</name><thumb>https://local.example/actor.jpg</thumb></actor>
              <unknown>keep me</unknown>
            </movie>
            """);
        WaitForTask((Task)(handleStartupVideoRequestAsync.Invoke(
            window,
            [StartupVideoRequest.OpenVideo(localVideoPath)])
            ?? throw new InvalidOperationException("启动影片载入没有返回任务。 ")));
        window.UpdateLayout();
        var localMetadata = window.DataContext as MovieMetadata
            ?? throw new InvalidOperationException("本地 NFO 没有进入编辑模型。 ");
        var statusText = window.FindName("StatusText") as TextBlock
            ?? throw new InvalidOperationException("状态栏未创建。 ");
        var posterImage = window.FindName("PosterImage") as System.Windows.Controls.Image
            ?? throw new InvalidOperationException("poster 预览控件未创建。 ");
        var fanartImage = window.FindName("FanartImage") as System.Windows.Controls.Image
            ?? throw new InvalidOperationException("fanart 预览控件未创建。 ");
        if (localMetadata.Title != "本地 NFO 标题" ||
            titleSourceText.Content?.ToString() != "本地 NFO" ||
            titleSourceText.Visibility != Visibility.Visible ||
            !statusText.Text.Contains("本地 NFO", StringComparison.Ordinal) ||
            !statusText.Text.Contains("poster + fanart", StringComparison.Ordinal) ||
            !statusText.Text.Contains(localNfoPath, StringComparison.OrdinalIgnoreCase) ||
            artworkSourceButton.Content?.ToString() != "本地图片 ▾" ||
            posterImage.Source is null || fanartImage.Source is null ||
            fanartHintText.Text != "横板封套：1×1" ||
            !saveButton.IsEnabled ||
            saveButton.ToolTip?.ToString()?.Contains("保留检测到的未知 XML", StringComparison.Ordinal) != true ||
            !statusText.Text.Contains("可安全更新", StringComparison.Ordinal) ||
            window.FindName("FilePathText") is not TextBlock { Text: var startupVideoText } ||
            !string.Equals(startupVideoText, localVideoPath, StringComparison.OrdinalIgnoreCase) ||
            !File.ReadAllText(AppLog.CurrentLogPath).Contains("从启动参数载入影片", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("本地 NFO 与现有图片没有以明确来源载入界面。 ");
        }

        var localLibre = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "LibreDMM 在线标题",
            Director = "在线导演",
            CoverUrl = "https://images.example.test/local-libre-cover.jpg",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var localR18 = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "R18 English title",
            Director = "Online director",
            CoverUrl = "https://images.example.test/local-r18-cover.jpg",
            SourceName = "r18dev",
            SourceDisplayName = "R18.dev"
        };
        var localOnlinePreferred = MetadataMerger.Merge(localLibre, localR18);
        localMetadata.Title = "搜索前手动标题";
        var reviewedLocalMetadata = applyOnlineSources.Invoke(
            window,
            [localOnlinePreferred, new MovieMetadata[] { localLibre, localR18 }]) as MovieMetadata
            ?? throw new InvalidOperationException("在线候选没有加入本地编辑会话。 ");
        window.UpdateLayout();
        if (reviewedLocalMetadata.Title != "LibreDMM 在线标题" ||
            reviewedLocalMetadata.Director != "在线导演" ||
            titleSourceText.Content?.ToString() != "LibreDMM ▾" ||
            directorSourceText.Content?.ToString() != "LibreDMM ▾" ||
            artworkSourceButton.Content?.ToString() != "本地图片 ▾" ||
            posterImage.Source is null || fanartImage.Source is null)
        {
            throw new InvalidOperationException("在线搜索后没有默认选择新文字资料，或意外改变了本地图片。 ");
        }

        artworkSourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var localArtworkItems = artworkSourceButton.ContextMenu?.Items.OfType<MenuItem>().ToArray()
            ?? throw new InvalidOperationException("本地封套候选菜单未创建。 ");
        if (localArtworkItems.Length != 4 ||
            localArtworkItems.All(item => item.Tag is not ArtworkCoverCandidate { Source.Name: "local-images" }) ||
            localArtworkItems.All(item => item.Tag is not ArtworkCoverCandidate { Source.Name: "libredmm" }) ||
            localArtworkItems.All(item => item.Tag is not ArtworkCoverCandidate { Source.Name: "r18dev" }) ||
            localArtworkItems.All(item => item.Tag?.ToString() != "choose-local-cover"))
        {
            throw new InvalidOperationException("本地、在线与手动选择入口没有进入同一封套来源菜单。 ");
        }
        artworkSourceButton.ContextMenu!.IsOpen = false;

        titleSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var localTitleCandidates = titleSourceText.ContextMenu?.Items.OfType<MenuItem>().ToArray()
            ?? throw new InvalidOperationException("本地标题候选菜单未创建。 ");
        if (localTitleCandidates.Length != 4 ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.Name: "local-nfo" }) ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.Name: "libredmm" }) ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.Name: "r18dev" }) ||
            localTitleCandidates.All(item => item.Tag is not MetadataFieldCandidate { Source.IsManual: true }))
        {
            throw new InvalidOperationException("本地、在线与搜索前手动值没有一起保留在字段候选菜单。 ");
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

        var manualCoverPath = Path.Combine(localTestRoot, "manual-cover.png");
        File.WriteAllBytes(manualCoverPath, onePixelPng);
        var applyManualCoverAsync = typeof(MainWindow).GetMethod(
            "ApplyManualCoverAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("手动完整封套入口未找到。 ");
        WaitForTask((Task)(applyManualCoverAsync.Invoke(window, [manualCoverPath])
            ?? throw new InvalidOperationException("手动完整封套载入没有返回任务。 ")));
        window.UpdateLayout();
        if (artworkSourceButton.Content?.ToString() != "手动封套 ▾" ||
            reviewedLocalMetadata.CoverUrl != Path.GetFullPath(manualCoverPath) ||
            posterImage.Source is null || fanartImage.Source is null ||
            fanartHintText.Text != "横板封套：1×1" ||
            !statusText.Text.Contains("同一来源生成", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("手动完整封套没有锁定并同时预览 poster/fanart。 ");
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

        var customTargetPanel = (Grid)window.FindName("CustomTargetPanel");
        var customRootTextBox = (TextBox)window.FindName("CustomRootTextBox");
        var targetPathHintText = (TextBlock)window.FindName("TargetPathHintText");
        var rememberCustomRoot = typeof(MainWindow).GetMethod(
            "RememberCustomRoot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("v0.8 最近目录记录入口未找到。 ");
        var refreshTargetLocationPreview = typeof(MainWindow).GetMethod(
            "RefreshTargetLocationPreview",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("目标路径预览入口未找到。 ");
        var unavailableRoot = Path.Combine(localTestRoot, "offline-library");
        targetModeComboBox.SelectedIndex = 2;
        customRootTextBox.Text = unavailableRoot;
        rememberCustomRoot.Invoke(window, [unavailableRoot]);
        refreshTargetLocationPreview.Invoke(window, null);
        window.UpdateLayout();
        if (!targetPathHintText.Text.Contains("当前不可用", StringComparison.Ordinal) ||
            !targetPathHintText.Text.Contains("不会自动创建", StringComparison.Ordinal) ||
            saveButton.IsEnabled || Directory.Exists(unavailableRoot))
        {
            throw new InvalidOperationException("v0.8 离线最近目录没有保持只提示且零创建。 ");
        }

        var customRoot = Path.Combine(localTestRoot, "library");
        Directory.CreateDirectory(customRoot);
        renameCheckBox.IsChecked = true;
        customRootTextBox.Text = customRoot;
        window.UpdateLayout();
        var expectedCustomVideo = Path.Combine(customRoot, "IPX-124", "IPX-124.mp4");
        if (customTargetPanel.Visibility != Visibility.Visible ||
            !targetPathHintText.Text.Contains(expectedCustomVideo, StringComparison.OrdinalIgnoreCase) ||
            !saveButton.IsEnabled)
        {
            throw new InvalidOperationException("dev2 同卷自定义根目录没有生成可保存的实时目标路径。 ");
        }
        var chooseTargetFolderButton = (Button)window.FindName("ChooseTargetFolderButton");
        var folderButtonRight = chooseTargetFolderButton
            .TranslatePoint(new Point(chooseTargetFolderButton.ActualWidth, 0), window).X;
        var saveButtonLeft = saveButton.TranslatePoint(new Point(0, 0), window).X;
        if (saveButtonLeft - folderButtonRight < 20)
        {
            throw new InvalidOperationException("dev3 选择文件夹与保存按钮之间的留白不足。 ");
        }

        customRootTextBox.Text = Path.Combine(customRoot, "ipx-124") + Path.DirectorySeparatorChar;
        window.UpdateLayout();
        if (targetPathHintText.Text.Contains(
                Path.Combine("ipx-124", "IPX-124", "IPX-124.mp4"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("dev2 已选择番号目录时仍重复生成了番号子目录。 ");
        }

        if (OperatingSystem.IsWindows())
        {
            var sourceRoot = Path.GetPathRoot(cleanVideoPath) ?? "C:\\";
            var otherDrive = sourceRoot.StartsWith("Z:", StringComparison.OrdinalIgnoreCase) ? "C:" : "Z:";
            customRootTextBox.Text = $@"{otherDrive}\JavMetaLite-dev2-test";
            window.UpdateLayout();
            if (!saveButton.IsEnabled ||
                !targetPathHintText.Text.Contains("安全复制 + SHA-256", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("dev3 跨盘符目标没有启用安全复制提示。 ");
            }
        }

        targetModeComboBox.SelectedIndex = 1;
        renameCheckBox.IsChecked = false;
        window.UpdateLayout();
        var expectedSourceNumberVideo = Path.Combine(localTestRoot, "IPX-124", "IPX-124.mp4");
        if (customTargetPanel.Visibility != Visibility.Collapsed ||
            !targetPathHintText.Text.Contains(expectedSourceNumberVideo, StringComparison.OrdinalIgnoreCase) ||
            !saveButton.IsEnabled)
        {
            throw new InvalidOperationException("dev2 来源位置番号文件夹模式没有正确恢复。 ");
        }
        targetModeComboBox.SelectedIndex = 0;
        window.UpdateLayout();

        var invalidVideoPath = Path.Combine(localTestRoot, "IPX-125.mp4");
        var invalidNfoPath = Path.Combine(localTestRoot, "IPX-125.nfo");
        var invalidPosterPath = Path.Combine(localTestRoot, "IPX-125-poster.jpg");
        File.WriteAllBytes(invalidVideoPath, [0x05]);
        File.WriteAllText(invalidNfoPath, "<tvshow><title>错误根元素</title></tvshow>");
        File.WriteAllBytes(invalidPosterPath, [0x00, 0x01, 0x02]);
        WaitForTask((Task)(selectVideoAsync.Invoke(window, [invalidVideoPath])
            ?? throw new InvalidOperationException("无效 NFO 载入没有返回任务。 ")));
        window.UpdateLayout();
        if (!statusText.Text.Contains("无法安全读取", StringComparison.Ordinal) ||
            !statusText.Text.Contains("原文件未修改", StringComparison.Ordinal) ||
            !statusText.Text.Contains("无效本地图片已忽略", StringComparison.Ordinal) ||
            saveButton.IsEnabled || titleSourceText.Visibility != Visibility.Collapsed ||
            !File.ReadAllText(AppLog.CurrentLogPath).Contains("本地 NFO 读取失败", StringComparison.Ordinal) ||
            !File.ReadAllText(AppLog.CurrentLogPath).Contains("本地 poster 无效", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("无效 NFO/图片没有被安全隔离并记录日志。 ");
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
            [
                new PlannedFileChange(PlannedChangeKind.CreateFile, "生成 metadata", "C:\\Media\\IPX-123\\IPX-123.nfo"),
                new PlannedFileChange(PlannedChangeKind.UpdateFile, "更新 NFO", "C:\\Media\\IPX-123\\IPX-123.nfo"),
                new PlannedFileChange(PlannedChangeKind.KeepFile, "poster 内容保持不变", "C:\\Media\\IPX-123\\IPX-123-poster.jpg"),
                new PlannedFileChange(PlannedChangeKind.ReplaceImage, "替换 fanart", "C:\\Media\\IPX-123\\IPX-123-fanart.jpg"),
                new PlannedFileChange(PlannedChangeKind.CopyAndVerifyVideo, "安全复制影片", "D:\\Media\\IPX-123\\IPX-123.mp4", "C:\\Media\\source.mp4")
            ],
            [],
            []);
        var previewWindow = new SavePreviewWindow(previewPlan) { Owner = window };
        previewWindow.Show();
        previewWindow.UpdateLayout();
        if (!previewWindow.IsVisible || previewWindow.FindName("ConfirmButton") is not Button { IsEnabled: true })
        {
            throw new InvalidOperationException("v0.4 保存预览窗口未成功创建。 ");
        }
        if (previewWindow.FindName("TargetPathTextBox") is not TextBox targetPathTextBox ||
            targetPathTextBox.Text != previewPlan.TargetVideoPath)
        {
            throw new InvalidOperationException("dev2 保存预览没有显示最终影片绝对路径。 ");
        }
        var previewChanges = previewWindow.FindName("ChangesList") as ListView
            ?? throw new InvalidOperationException("保存预览变更列表未创建。 ");
        var previewActions = previewChanges.Items.Cast<object>()
            .Select(item => item.GetType().GetProperty("Action")?.GetValue(item)?.ToString())
            .ToArray();
        if (!new[] { "生成", "更新", "保持不变", "替换图片", "复制并校验" }.All(previewActions.Contains))
        {
            throw new InvalidOperationException("dev4 保存预览没有区分创建、更新、保持不变和替换图片。 ");
        }
        previewWindow.Close();

        Console.WriteLine($"UI PASS  handle={handle} visible={window.IsVisible} title={window.Title} posterFrozen={poster.IsFrozen} comboDark=True multiSourceLabel=True libreDmm=True fanart=True previewWindow=True previewChangeKinds=True sourceBadges=True candidateMenus=True fullDarkMenuTemplate=True fieldSwitch=True manualReturn=True unifiedArtworkSource=True artworkMenu=True localNfoLoad=True localNfoSaveEnabled=True localArtworkPreview=True localArtworkDefault=True localOnlineCandidates=True manualCoverPreview=True localManualReturn=True localFailureSafe=True staleCandidatesCleared=True directSaveDefaultsOff=True directSaveRemembered=True targetModes=True customTargetPreview=True verifiedCopyHint=True cancelOperation=True improvedSpacing=True startupVideo=True recentRoots=True unavailableRootSafe=True");
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
