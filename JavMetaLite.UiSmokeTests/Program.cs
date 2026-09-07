using System.IO;
using System.Globalization;
using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--performance")
                return RunPerformance(args);
            if (args.FirstOrDefault() == "--search-hotspots")
                return RunSearchHotspots(args);
            if (args.FirstOrDefault() == "--stability")
                return RunStability(args);
            if (args.FirstOrDefault() == "--keyboard")
                return RunQueueKeyboardTests(args);
            if (args.FirstOrDefault() == "--cover-checks")
                return RunCoverResolutionTests(args);
            if (args.FirstOrDefault() == "--final-review-fixes")
                return RunFinalReviewFixTests(args);
            if (args.FirstOrDefault() == "--version-display")
                return RunVersionDisplayTests(args);
            if (args.FirstOrDefault() == "--layout")
                return RunLayoutTests();
            RunTests();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunTests()
    {
        TestSystemLanguageResolution();
        var application = new Application();
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative)
        });
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        window.Show();

        TestCloseWaitsForOperation(observeCancellation: true);
        TestCloseWaitsForOperation(observeCancellation: false);
        Console.WriteLine("UI PASS closeWaitsForRecovery=True repeatedCloseSafe=True uncancellableCompletionSafe=True");
        TestPartialSourceQueueUi();
        TestBatchSourceRoutingUi();
        TestPerformanceCloseout();
        RunStabilityTask(TestCoverResolutionAsync(Path.Combine(
            Path.GetTempPath(), "JavMetaLite-cover-resolution-" + Guid.NewGuid().ToString("N"))));
        RunStabilityTask(TestCanceledPreviewRevisitAsync(Path.Combine(
            Path.GetTempPath(), "JavMetaLite-preview-cancel-" + Guid.NewGuid().ToString("N"))));
        RunStabilityTask(TestDelayedViewerAsync());
        RunStabilityTask(TestQueueKeyboardNavigationAsync(Path.Combine(
            Path.GetTempPath(), "JavMetaLite-queue-keyboard-" + Guid.NewGuid().ToString("N"))));
        Console.WriteLine("UI PASS canceledPreviewRevisit=True canceledSourcePreviewRevisit=True completedPreviewCache=True missingArtworkCache=True lateViewerImageIgnored=True viewerCloseCancels=True");
        TestSearchPredicateParity();
        TestRestrictedLayoutHost();
        TestSearchToolbarLayout();
        TestReadabilityCloseout();
        TestVersionDisplay(Path.Combine(Path.GetTempPath(), "JavMetaLite-version-display-" + Guid.NewGuid().ToString("N")));
        RunStabilityTask(TestFinalReviewFixesAsync(Path.Combine(
            Path.GetTempPath(), "JavMetaLite-final-review-fixes-" + Guid.NewGuid().ToString("N"))));

        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (handle == IntPtr.Zero || !window.IsVisible || window.Title != "JAV Metadata Lite")
        {
            throw new InvalidOperationException("主窗口未成功创建。 ");
        }
        var nativeCaptionColor = TryReadDwmIntAttribute(handle, 35);
        if (nativeCaptionColor is not null && nativeCaptionColor != 0x00211811)
        {
            throw new InvalidOperationException(
                $"v0.9 dev3-r2 标题栏颜色不匹配：0x{nativeCaptionColor:X8}。 ");
        }

        var onePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nQAAAABJRU5ErkJggg==");
        var poster = PosterBitmapFactory.CreateFrozen(onePixelPng);
        if (!poster.IsFrozen || poster.PixelWidth != 1 || poster.PixelHeight != 1)
        {
            throw new InvalidOperationException("封面 Bitmap 未能安全冻结。 ");
        }
        var wideArtwork = new RenderTargetBitmap(200, 100, 96, 96, PixelFormats.Pbgra32);
        wideArtwork.Freeze();

        var sourceComboBox = window.FindName("SourceComboBox") as ComboBox
            ?? throw new InvalidOperationException("没有找到来源选择框。 ");
        var sourceSelectorHost = window.FindName("SourceSelectorHost") as Grid
            ?? throw new InvalidOperationException("Preview 38 没有找到组合来源控件。 ");
        var sourceProfileButton = window.FindName("SourceProfileButton") as Button
            ?? throw new InvalidOperationException("没有找到自定义来源配置入口。 ");
        var sourceProfileDivider = window.FindName("SourceProfileDivider") as Border
            ?? throw new InvalidOperationException("Preview 38 没有找到来源规则分隔线。 ");
        var sourceDropDownArrow = FindVisualChildren<ShapePath>(sourceComboBox)
            .FirstOrDefault(path => Math.Abs(path.ActualWidth - 8) < 0.5 &&
                                    Math.Abs(path.ActualHeight - 5) < 0.5)
            ?? throw new InvalidOperationException("Preview 39 没有找到来源下拉箭头。 ");
        var languageComboBox = window.FindName("LanguageComboBox") as ComboBox
            ?? throw new InvalidOperationException("没有找到语言选择框。 ");
        var searchButton = window.FindName("SearchButton") as Button
            ?? throw new InvalidOperationException("没有找到搜索按钮。 ");
        var browserImportButton = window.FindName("BrowserImportButton") as Button
            ?? throw new InvalidOperationException("没有找到网页导入按钮。 ");
        var idTextBox = window.FindName("IdTextBox") as TextBox
            ?? throw new InvalidOperationException("没有找到番号输入框。 ");
        var searchToolbar = window.FindName("SearchToolbar") as Border
            ?? throw new InvalidOperationException("没有找到搜索工具栏。 ");
        var chooseVideoButton = window.FindName("ChooseVideoButton") as Button
            ?? throw new InvalidOperationException("没有找到选择影片按钮。 ");
        var removeCurrentVideoButton = window.FindName("RemoveCurrentVideoButton") as Button
            ?? throw new InvalidOperationException("Preview 41 没有找到单片移除影片入口。 ");
        var automaticSourceText = window.FindName("AutomaticSourceText") as TextBlock
            ?? throw new InvalidOperationException("Preview 24 自动来源标签未创建。 ");
        var searchToolbarLayout = window.FindName("SearchToolbarLayout") as Grid
            ?? throw new InvalidOperationException("Preview 25 响应式搜索布局未创建。 ");
        var metadataScrollViewer = window.FindName("MetadataScrollViewer") as ScrollViewer
            ?? throw new InvalidOperationException("Preview 23 资料滚动视口未命名。 ");
        var saveSettingsToggleButton = window.FindName("SaveSettingsToggleButton") as Button
            ?? throw new InvalidOperationException("Preview 23 保存设置摘要入口未创建。 ");
        var saveSettingsOptionsPanel = window.FindName("SaveSettingsOptionsPanel") as StackPanel
            ?? throw new InvalidOperationException("Preview 23 可折叠保存设置未创建。 ");
        var saveSettingsSummaryText = window.FindName("SaveSettingsSummaryText") as TextBlock
            ?? throw new InvalidOperationException("Preview 23 保存设置摘要未创建。 ");
        var saveSettingsTitleText = window.FindName("SaveSettingsTitleText") as TextBlock
            ?? throw new InvalidOperationException("Preview 35 保存设置标题未创建。 ");
        var saveSettingsChevronPath = window.FindName("SaveSettingsChevronPath") as ShapePath
            ?? throw new InvalidOperationException("Preview 36 保存设置矢量折叠箭头未创建。 ");
        var posterRoleText = window.FindName("PosterRoleText") as TextBlock
            ?? throw new InvalidOperationException("Preview 23 海报裁切标签未创建。 ");
        var fanartRoleText = window.FindName("FanartRoleText") as TextBlock
            ?? throw new InvalidOperationException("Preview 23 完整封套标签未创建。 ");
        var preview26PosterBorder = window.FindName("PosterPreviewBorder") as Border
            ?? throw new InvalidOperationException("Preview 26 海报预览入口未创建。 ");
        var preview26FanartBorder = window.FindName("FanartPreviewBorder") as Border
            ?? throw new InvalidOperationException("Preview 26 fanart 预览入口未创建。 ");
        var posterPreviewStatePanel = window.FindName("PosterPreviewStatePanel") as StackPanel
            ?? throw new InvalidOperationException("Preview 26 海报状态层未创建。 ");
        var fanartPreviewStateText = window.FindName("FanartPreviewStateText") as TextBlock
            ?? throw new InvalidOperationException("Preview 26 fanart 状态层未创建。 ");
        var retryFailedSourcesButton = window.FindName("RetryFailedSourcesButton") as Button
            ?? throw new InvalidOperationException("Preview 32 底部失败来源重试入口未创建。 ");
        var brandIconImage = window.FindName("BrandIconImage") as Image
            ?? throw new InvalidOperationException("没有找到应用品牌图标。 ");
        if (window.Icon is null || brandIconImage.Source is null)
        {
            throw new InvalidOperationException("v0.9 dev3 窗口图标或页眉图标未成功载入。 ");
        }
        if (!application.Resources.MergedDictionaries.Any(dictionary =>
                dictionary.Source?.OriginalString.EndsWith("Resources/Theme.xaml", StringComparison.OrdinalIgnoreCase) == true) ||
            searchButton.MinHeight < 36 || idTextBox.MinHeight < 36 || sourceComboBox.MinHeight < 36)
        {
            throw new InvalidOperationException("v0.9 dev2 共用主题或统一控件尺寸未生效。 ");
        }
        var scrollBarStyle = application.TryFindResource(typeof(ScrollBar)) as Style
            ?? throw new InvalidOperationException("没有找到共用滚动条样式。 ");
        var themedScrollBar = new ScrollBar { Style = scrollBarStyle };
        themedScrollBar.ApplyTemplate();
        if (themedScrollBar.Width > 12 ||
            themedScrollBar.Template.FindName("PART_Track", themedScrollBar) is not Track)
        {
            throw new InvalidOperationException("v0.9 dev3-r1 深色滚动条样式未生效。 ");
        }
        sourceComboBox.ApplyTemplate();
        var dropDownPopup = sourceComboBox.Template.FindName("PART_Popup", sourceComboBox) as Popup
            ?? throw new InvalidOperationException("没有找到来源选择框的下拉菜单。 ");
        var dropDownBorder = dropDownPopup.Child as Border
            ?? throw new InvalidOperationException("没有找到下拉菜单边框。 ");
        var comboItemStyle = window.FindResource("DarkComboBoxItem") as Style
            ?? throw new InvalidOperationException("没有找到来源选项样式。 ");
        var comboItemTemplate = comboItemStyle.Setters.OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == Control.TemplateProperty)?.Value as ControlTemplate
            ?? throw new InvalidOperationException("没有找到来源选项模板。 ");
        var sourceItemBorder = comboItemTemplate.LoadContent() as Border
            ?? throw new InvalidOperationException("没有找到来源选项边框。 ");
        if (dropDownBorder.Margin.Top < 4 || dropDownBorder.Padding.Left < 3 ||
            dropDownBorder.CornerRadius.TopLeft < 7 || sourceItemBorder.CornerRadius.TopLeft < 4)
        {
            throw new InvalidOperationException("v0.9 dev2-r1 下拉菜单间距或圆角未生效。 ");
        }

        var defaultSourceItem = sourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "libredmm");
        var libreSourceItem = sourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "libredmm");
        var manualSourceItem = sourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "manual");
        idTextBox.Text = "IPZZ-850";
        sourceComboBox.SelectedItem = libreSourceItem;
        window.UpdateLayout();
        if (browserImportButton.Content?.ToString() != "Manual web lookup ▾" ||
            browserImportButton.ToolTip?.ToString()?.Contains("independently", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("Preview 20 手动网页查询仍受自动搜索来源影响。 ");
        }
        browserImportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var browserSourceMenu = browserImportButton.ContextMenu
            ?? throw new InvalidOperationException("Preview 20 单来源模式没有打开独立网页查询菜单。 ");
        browserSourceMenu.ApplyTemplate();
        window.UpdateLayout();
        var browserMenuScrollViewer = FindVisualChildren<ScrollViewer>(browserSourceMenu).FirstOrDefault()
            ?? throw new InvalidOperationException("Preview 37 网页查询菜单滚动视口未创建。 ");
        var browserTargets = browserSourceMenu.Items.OfType<MenuItem>().ToArray();
        browserSourceMenu.IsOpen = false;
        sourceComboBox.SelectedItem = manualSourceItem;
        window.UpdateLayout();
        if (browserImportButton.Content?.ToString() != "Manual web lookup ▾")
        {
            throw new InvalidOperationException("Preview 20 手动填写模式错误地改变了网页查询入口。 ");
        }
        foreach (var sourceItem in sourceComboBox.Items.OfType<ComboBoxItem>())
        {
            sourceComboBox.SelectedItem = sourceItem;
            window.UpdateLayout();
            if (browserImportButton.Content?.ToString() != "Manual web lookup ▾")
            {
                throw new InvalidOperationException(
                    $"Preview 20 来源模式 {sourceItem.Tag} 错误地改变了网页查询入口。 ");
            }
        }
        sourceComboBox.SelectedItem = defaultSourceItem;
        window.UpdateLayout();
        var r18BrowserTarget = browserTargets
            .Select(item => item.Tag)
            .OfType<BrowserImportTarget>()
            .Single(target => target.SourceName == "r18dev");
        var r18MenuHeading = (browserTargets[1].Header as StackPanel)?.Children
            .OfType<TextBlock>()
            .FirstOrDefault()?.Text;
        if (browserImportButton.Content?.ToString() != "Manual web lookup ▾" ||
            browserTargets.Length != 3 ||
            browserTargets.Any(item => item.Style != window.FindResource("CandidateMenuItem")) ||
            browserMenuScrollViewer.CanContentScroll ||
            PreciseScrollBehavior.GetAxis(browserMenuScrollViewer) != PreciseScrollAxis.Vertical ||
            browserTargets.Select(item => item.Tag).OfType<BrowserImportTarget>().Select(target => target.SourceName)
                .SequenceEqual(["libredmm", "r18dev", "javlibrary"]) == false ||
            r18MenuHeading != "Search with R18.dev" ||
            r18BrowserTarget.InitialUrl != R18DevClient.HomePageUrl ||
            r18BrowserTarget.InitialUrl.EndsWith("/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Preview 20 独立网页查询菜单、R18.dev 安全首页路由或深色样式不正确。 ");
        }
        idTextBox.Text = string.Empty;
        var normalWidth = window.Width;
        var normalHeight = window.Height;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        window.UpdateLayout();
        var languageExpectations = new[]
        {
            (Code: UiLanguageCodes.SimplifiedChinese, Search: "搜索资料", Save: "保存", AutomaticLabel: "来源", AutoSource: "LibreDMM（推荐）", CustomSource: "自定义多来源", Configure: "编辑规则", Font: "Segoe UI, Microsoft YaHei UI"),
            (Code: UiLanguageCodes.TraditionalChinese, Search: "搜尋資料", Save: "儲存", AutomaticLabel: "來源", AutoSource: "LibreDMM（建議）", CustomSource: "自訂多來源", Configure: "編輯規則", Font: "Segoe UI, Microsoft JhengHei UI"),
            (Code: UiLanguageCodes.English, Search: "Search", Save: "Save", AutomaticLabel: "Source", AutoSource: "LibreDMM (recommended)", CustomSource: "Custom multi-source", Configure: "Edit rules", Font: "Segoe UI"),
            (Code: UiLanguageCodes.Japanese, Search: "検索", Save: "保存", AutomaticLabel: "取得元", AutoSource: "LibreDMM（推奨）", CustomSource: "カスタム複数ソース", Configure: "ルールを編集", Font: "Segoe UI, Yu Gothic UI")
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
            if (window.FindName("VersionText") is not TextBlock versionText ||
                versionText.Text != string.Format(CultureInfo.InvariantCulture,
                    (string)dictionary[MainWindow.ApplicationVersion.Split('+')[0].Contains('-')
                        ? "Main.PreviewVersionFormat" : "Main.CurrentVersionFormat"], MainWindow.ApplicationVersion))
            {
                throw new InvalidOperationException("Displayed version does not match the running assembly and language.");
            }
            if (searchButton.Content?.ToString() != expectation.Search ||
                window.FindName("SaveButton") is not Button languageSaveButton ||
                languageSaveButton.Content?.ToString() != expectation.Save ||
                automaticSourceText.Text != expectation.AutomaticLabel ||
                sourceComboBox.Items[0] is not ComboBoxItem languageAutoSource ||
                languageAutoSource.Content?.ToString() != expectation.AutoSource ||
                string.IsNullOrWhiteSpace(languageAutoSource.ToolTip?.ToString()) ||
                languageSaveButton.ToolTip?.ToString()?.Contains("Ctrl+S", StringComparison.Ordinal) != true ||
                searchButton.FontFamily.Source != expectation.Font)
            {
                throw new InvalidOperationException($"v0.9 {expectation.Code} 没有即时更新主要界面文字。 ");
            }
            var customSourceItem = sourceComboBox.Items.OfType<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == "custom");
            if (customSourceItem.Content?.ToString() != expectation.CustomSource)
            {
                throw new InvalidOperationException($"Preview 11 {expectation.Code} 自定义来源名称未本地化。 ");
            }
            var sourceHostPositionBeforeCustom = sourceSelectorHost.TranslatePoint(new Point(0, 0), searchToolbar);
            var sourceHostWidthBeforeCustom = sourceSelectorHost.ActualWidth;
            var sourceHostRowBeforeCustom = Grid.GetRow(sourceSelectorHost);
            var sourceHostColumnBeforeCustom = Grid.GetColumn(sourceSelectorHost);
            var sourceHostColumnSpanBeforeCustom = Grid.GetColumnSpan(sourceSelectorHost);
            sourceComboBox.SelectedItem = customSourceItem;
            window.UpdateLayout();
            var sourceProfileButtonRight = sourceProfileButton.TranslatePoint(
                new Point(sourceProfileButton.ActualWidth, 0),
                sourceSelectorHost).X;
            var sourceDropDownArrowLeft = sourceDropDownArrow.TranslatePoint(
                new Point(0, 0),
                sourceSelectorHost).X;
            var sourceActionGap = sourceDropDownArrowLeft - sourceProfileButtonRight;
            if (sourceProfileButton.Visibility != Visibility.Visible ||
                sourceProfileDivider.Visibility != Visibility.Visible ||
                sourceProfileButton.Content is not TextBlock { Text: "⚙" } ||
                sourceProfileButton.Width < 24 ||
                sourceProfileButton.Height < 26 ||
                sourceActionGap < 3 ||
                AutomationProperties.GetName(sourceProfileButton) != expectation.Configure ||
                string.IsNullOrWhiteSpace(sourceProfileButton.ToolTip?.ToString()) ||
                !FitsInside(sourceProfileButton, sourceSelectorHost) ||
                Grid.GetRow(sourceSelectorHost) != sourceHostRowBeforeCustom ||
                Grid.GetColumn(sourceSelectorHost) != sourceHostColumnBeforeCustom ||
                Grid.GetColumnSpan(sourceSelectorHost) != sourceHostColumnSpanBeforeCustom ||
                Math.Abs(sourceSelectorHost.ActualWidth - sourceHostWidthBeforeCustom) > 0.5 ||
                (sourceSelectorHost.TranslatePoint(new Point(0, 0), searchToolbar) -
                 sourceHostPositionBeforeCustom).Length > 0.5)
            {
                throw new InvalidOperationException(
                    $"Preview 39 {expectation.Code} 自定义来源入口的间距或布局不正确。 " +
                    $"gearArrowGap={sourceActionGap:0.0}");
            }
            sourceComboBox.SelectedItem = sourceComboBox.Items[0];
            window.UpdateLayout();
            if (sourceProfileButton.Visibility != Visibility.Collapsed ||
                sourceProfileDivider.Visibility != Visibility.Collapsed ||
                !FitsInside(idTextBox, searchToolbar) ||
                !FitsInside(searchButton, searchToolbar) ||
                !FitsInside(browserImportButton, searchToolbar) ||
                !FitsInside(sourceSelectorHost, searchToolbar) ||
                !FitsInside(sourceComboBox, searchToolbar) ||
                !FitsInside(chooseVideoButton, window))
            {
                throw new InvalidOperationException($"v0.9 dev2 {expectation.Code} 在最小窗口下出现控件越界。 ");
            }

            if (expectation.Code == UiLanguageCodes.English)
            {
                var sourceText = new FormattedText(
                    sourceComboBox.Resources["SearchSourceSelectionLabel"]?.ToString() ?? expectation.AutoSource,
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
                var availableTextWidth = sourceComboBox.ActualWidth -
                                         sourceComboBox.Padding.Left -
                                         sourceComboBox.Padding.Right;
                if (sourceText.WidthIncludingTrailingWhitespace > availableTextWidth)
                {
                    throw new InvalidOperationException(
                        $"v0.9 英文来源名称会超出选择框的可见宽度。 " +
                        $"actual={sourceComboBox.ActualWidth:0.0} available={availableTextWidth:0.0} text={sourceText.WidthIncludingTrailingWhitespace:0.0}");
                }
            }
        }
        window.Width = normalWidth;
        window.Height = normalHeight;
        languageComboBox.SelectedItem = languageComboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == UiLanguageCodes.SimplifiedChinese);
        window.UpdateLayout();
        sourceComboBox.IsDropDownOpen = true;
        window.UpdateLayout();
        var sourcePopup = sourceComboBox.Template.FindName("PART_Popup", sourceComboBox) as Popup
            ?? throw new InvalidOperationException("Preview 37 来源下拉弹出层未创建。 ");
        var sourcePopupScrollViewer = FindVisualChildren<ScrollViewer>(sourcePopup.Child).FirstOrDefault()
            ?? throw new InvalidOperationException("Preview 37 来源下拉滚动视口未创建。 ");
        var firstItem = sourceComboBox.Items[0] as ComboBoxItem
            ?? throw new InvalidOperationException("来源选择项未创建。 ");
        if (firstItem.Tag?.ToString() != "libredmm" || firstItem.Content?.ToString() != "LibreDMM（推荐）")
        {
            throw new InvalidOperationException("Preview 46 默认推荐来源不是 LibreDMM。 ");
        }
        if (firstItem.Foreground is not SolidColorBrush foreground || foreground.Color.R < 200 ||
            firstItem.Background is not SolidColorBrush background || background.Color.R > 40 ||
            sourcePopupScrollViewer.CanContentScroll ||
            PreciseScrollBehavior.GetAxis(sourcePopupScrollViewer) != PreciseScrollAxis.Vertical)
        {
            throw new InvalidOperationException("Preview 37 来源选择框没有应用深色下拉或像素滚动。 ");
        }
        sourceComboBox.IsDropDownOpen = false;

        if (sourceComboBox.Items.OfType<ComboBoxItem>().All(item => item.Tag?.ToString() != "libredmm"))
        {
            throw new InvalidOperationException("来源选择框没有 LibreDMM。 ");
        }
        var idCenterY = idTextBox.TranslatePoint(new Point(0, idTextBox.ActualHeight / 2), searchToolbar).Y;
        var sourceCenterY = sourceComboBox.TranslatePoint(
            new Point(0, sourceComboBox.ActualHeight / 2),
            searchToolbar).Y;
        if (automaticSourceText.Text != "来源" ||
            sourceComboBox.ToolTip?.ToString()?.Contains("LibreDMM", StringComparison.Ordinal) != true ||
            Math.Abs(idCenterY - sourceCenterY) > 1.5 ||
            searchToolbarLayout.RowDefinitions[1].Height.Value != 0 ||
            metadataScrollViewer.CanContentScroll ||
            PreciseScrollBehavior.GetAxis(metadataScrollViewer) != PreciseScrollAxis.Vertical ||
            saveSettingsOptionsPanel.Visibility != Visibility.Collapsed ||
            string.IsNullOrWhiteSpace(saveSettingsSummaryText.Text) ||
            posterRoleText.Text != "海报裁切" ||
            fanartRoleText.Text != "完整封套 / fanart" ||
            preview26PosterBorder.Cursor != Cursors.Arrow ||
            preview26FanartBorder.Cursor != Cursors.Arrow ||
            posterPreviewStatePanel.Visibility != Visibility.Collapsed ||
            fanartPreviewStateText.Visibility != Visibility.Collapsed ||
            window.FindName("ArtworkViewerButton") is not Button { IsEnabled: false } ||
            window.FindName("SearchFeedbackPanel") is not null ||
            retryFailedSourcesButton.Visibility != Visibility.Collapsed ||
            window.FindName("RatingSourceText") is not Button { FontSize: >= 11, MinHeight: >= 24 })
        {
            throw new InvalidOperationException("Preview 25 常规窗口搜索栏、图片标签或折叠摘要未正确呈现。 ");
        }
        var backgroundArtworkLoadCount = 0;
        var localDeletionConfirmationCount = 0;
        var allowLocalDeletion = false;
        var deletedLocalArtworkPaths = new List<string>();
        var localPosterItem = new ArtworkViewerItem(
            "Poster",
            poster,
            @"C:\Movies\poster.jpg",
            kind: ArtworkViewerItemKind.Poster,
            isLocal: true);
        var localFanartItem = new ArtworkViewerItem(
            "Fanart",
            wideArtwork,
            @"C:\Movies\fanart.jpg",
            kind: ArtworkViewerItemKind.Fanart,
            isLocal: true);
        var sampleItem = new ArtworkViewerItem(
            "Sample / extrafanart 1",
            null,
            @"C:\Movies\extrafanart\fanart1.jpg",
            isSelectableSample: true,
            isSelected: true,
            isLocal: true);
        var onlineSampleItem = new ArtworkViewerItem(
            "Sample / extrafanart 2",
            null,
            "https://example.test/sample2.jpg",
            isSelectableSample: true,
            isSelected: true);
        var viewer = new ArtworkViewerWindow(
            [
                localPosterItem,
                localFanartItem,
                sampleItem,
                onlineSampleItem
            ],
            2,
            (item, _) =>
            {
                backgroundArtworkLoadCount++;
                return Task.FromResult(ArtworkLocationHelper.TryGetLocalPath(item.SourceLocation, out var localImagePath)
                    ? new ArtworkViewerLoadedImage(poster, 1080, 1920)
                    : new ArtworkViewerLoadedImage(poster, 1920, 1080));
            },
            confirmLocalDeletion: items =>
            {
                localDeletionConfirmationCount++;
                return allowLocalDeletion;
            },
            deleteLocalImage: path => deletedLocalArtworkPaths.Add(path));
        viewer.Show();
        viewer.Width = viewer.MinWidth;
        viewer.Height = viewer.MinHeight;
        viewer.UpdateLayout();
        var artworkFilmstripToggleButton = viewer.FindName("ArtworkFilmstripToggleButton") as Button;
        var artworkFilmstripPanel = viewer.FindName("ArtworkFilmstripPanel") as StackPanel;
        var artworkFilmstripChevronPath = viewer.FindName("ArtworkFilmstripChevronPath") as ShapePath;
        var artworkFilmstripSummaryText = viewer.FindName("ArtworkFilmstripSummaryText") as TextBlock;
        var collapsedFilmstripChevronSize = new Size(
            artworkFilmstripChevronPath?.ActualWidth ?? 0,
            artworkFilmstripChevronPath?.ActualHeight ?? 0);
        if (artworkFilmstripToggleButton is null ||
            artworkFilmstripPanel is not { Visibility: Visibility.Collapsed } ||
            artworkFilmstripChevronPath?.RenderTransform is not RotateTransform { Angle: 0 } ||
            artworkFilmstripSummaryText is not { Text: "本地 3 · 在线候选 1" })
        {
            throw new InvalidOperationException("Preview 35 图片列表没有默认收起或显示本地与在线摘要。 ");
        }
        artworkFilmstripToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        viewer.UpdateLayout();
        if (artworkFilmstripPanel.Visibility != Visibility.Visible ||
            artworkFilmstripChevronPath.RenderTransform is not RotateTransform { Angle: 90 } ||
            Math.Abs(artworkFilmstripChevronPath.ActualWidth - collapsedFilmstripChevronSize.Width) > 0.1 ||
            Math.Abs(artworkFilmstripChevronPath.ActualHeight - collapsedFilmstripChevronSize.Height) > 0.1)
        {
            throw new InvalidOperationException("Preview 36 图片列表无法展开，或两个方向的箭头尺寸不一致。 ");
        }
        var artworkTypeComboBox = viewer.FindName("ArtworkTypeComboBox") as ComboBox;
        artworkTypeComboBox?.ApplyTemplate();
        var artworkTypePopup = artworkTypeComboBox?.Template.FindName("PART_Popup", artworkTypeComboBox) as Popup;
        var artworkTypePopupBorder = artworkTypePopup?.Child as Border;
        var artworkTypePopupScrollViewer = artworkTypePopupBorder is null
            ? null
            : FindVisualChildren<ScrollViewer>(artworkTypePopupBorder).FirstOrDefault();
        var localThumbnailPanel = viewer.FindName("LocalThumbnailPanel") as StackPanel;
        var thumbnailPanel = viewer.FindName("ThumbnailPanel") as StackPanel;
        var localThumbnailScrollViewer = viewer.FindName("LocalThumbnailScrollViewer") as ScrollViewer;
        var onlineThumbnailScrollViewer = viewer.FindName("ThumbnailScrollViewer") as ScrollViewer;
        var artworkPreviewBorder = viewer.FindName("ArtworkPreviewBorder") as Border;
        var artworkDimensionsText = viewer.FindName("ArtworkDimensionsText") as TextBlock;
        var firstLocalThumbnailSelectionBox = localThumbnailPanel is null
            ? null
            : FindVisualChildren<CheckBox>(localThumbnailPanel).FirstOrDefault();
        firstLocalThumbnailSelectionBox?.ApplyTemplate();
        var firstLocalThumbnailCheckBorder = firstLocalThumbnailSelectionBox?.Template.FindName(
            "CheckBorder",
            firstLocalThumbnailSelectionBox) as Border;
        if (viewer.FindName("ArtworkImage") is not Image { Source: not null } ||
            viewer.FindName("ArtworkPositionText") is not TextBlock { Text: "3 / 4" } ||
            artworkTypeComboBox is not { Items.Count: 4, SelectedIndex: 0 } ||
            artworkTypeComboBox.Background is not SolidColorBrush artworkTypeBackground ||
            artworkTypeBackground.Color.R > 40 ||
            artworkTypeComboBox.Foreground is not SolidColorBrush artworkTypeForeground ||
            artworkTypeForeground.Color.R < 180 ||
            artworkTypePopupBorder?.Background is not SolidColorBrush artworkPopupBackground ||
            artworkPopupBackground.Color.R > 40 ||
            artworkTypePopupScrollViewer is null ||
            PreciseScrollBehavior.GetAxis(artworkTypePopupScrollViewer) != PreciseScrollAxis.Vertical ||
            localThumbnailScrollViewer is null ||
            PreciseScrollBehavior.GetAxis(localThumbnailScrollViewer) != PreciseScrollAxis.Horizontal ||
            onlineThumbnailScrollViewer is null ||
            PreciseScrollBehavior.GetAxis(onlineThumbnailScrollViewer) != PreciseScrollAxis.Horizontal ||
            localThumbnailPanel is null ||
            localThumbnailPanel.Children.Count != 3 ||
            localThumbnailPanel.Children.OfType<Border>().Select(border => border.Width).Distinct().Count() < 2 ||
            FindVisualChildren<Image>(localThumbnailPanel).Any(image => image.Stretch != Stretch.Uniform) ||
            localThumbnailPanel.Children.OfType<Border>().Count(border => border.BorderThickness.Left >= 2) != 1 ||
            FindVisualChildren<CheckBox>(localThumbnailPanel).Count() != 3 ||
            !ReferenceEquals(
                firstLocalThumbnailSelectionBox?.Style,
                viewer.FindResource("ArtworkThumbnailCheckBox") as Style) ||
            firstLocalThumbnailCheckBorder?.Background is not SolidColorBrush thumbnailCheckBackground ||
            thumbnailCheckBackground.Color.A is >= 140 or < 80 ||
            thumbnailPanel is null ||
            thumbnailPanel.Children.Count != 1 ||
            FindVisualChildren<CheckBox>(thumbnailPanel).Count() != 1 ||
            artworkPreviewBorder is null ||
            artworkDimensionsText is not
            {
                Text: "图片尺寸：1080 × 1920",
                Visibility: Visibility.Visible,
                HorizontalAlignment: HorizontalAlignment.Right
            } ||
            viewer.FindName("DeleteSelectedLocalButton") is not Button
            {
                Visibility: Visibility.Visible,
                IsEnabled: false,
                Content: "删除所选（0）"
            } deleteSelectedLocalButton ||
            viewer.FindName("LocalImagesHeaderText") is not TextBlock { Text: "本地图片（3）" } ||
            viewer.FindName("OnlineImagesHeaderText") is not TextBlock { Text: "在线候选（1）" } ||
            viewer.FindName("PreviousButton") is not Button { IsEnabled: true } ||
            viewer.FindName("NextButton") is not Button { IsEnabled: true } ||
            viewer.FindName("CurrentSampleSelectionCheckBox") is not CheckBox
            {
                Visibility: Visibility.Collapsed
            } sampleSelectionCheckBox ||
            viewer.FindName("SampleSelectionSummaryText") is not TextBlock
            {
                Visibility: Visibility.Visible,
                Text: "已选择 1 / 1 张剧照"
            } ||
            viewer.FindName("ApplySelectionButton") is not Button { Visibility: Visibility.Visible } applySelectionButton ||
            !FitsInside(deleteSelectedLocalButton, viewer) ||
            backgroundArtworkLoadCount != 2)
        {
            throw new InvalidOperationException("Preview 34 图片查看器分行、批量操作或半透明缩略图选择框状态不正确。 ");
        }
        var artworkPreviewBottom = artworkPreviewBorder
            .TranslatePoint(new Point(0, artworkPreviewBorder.ActualHeight), viewer).Y;
        var artworkPreviewRight = artworkPreviewBorder
            .TranslatePoint(new Point(artworkPreviewBorder.ActualWidth, 0), viewer).X;
        var artworkDimensionsTop = artworkDimensionsText.TranslatePoint(new Point(0, 0), viewer).Y;
        var artworkDimensionsRight = artworkDimensionsText
            .TranslatePoint(new Point(artworkDimensionsText.ActualWidth, 0), viewer).X;
        if (artworkDimensionsTop < artworkPreviewBottom - 0.5 ||
            Math.Abs(artworkDimensionsRight - artworkPreviewRight) > 8)
        {
            throw new InvalidOperationException("Preview 31 图片尺寸仍遮挡预览，或没有在预览框外右对齐。 ");
        }
        artworkTypeComboBox.SelectedIndex = 3;
        viewer.UpdateLayout();
        if (viewer.FindName("ArtworkPositionText") is not TextBlock { Text: "1 / 2" } ||
            viewer.FindName("ArtworkTitleText") is not TextBlock { Text: "Sample / extrafanart 1" })
        {
            throw new InvalidOperationException("Preview 28 Extra Fanart 类型筛选未生效。 ");
        }
        artworkTypeComboBox.SelectedIndex = 2;
        viewer.UpdateLayout();
        if (viewer.FindName("ArtworkPositionText") is not TextBlock { Text: "1 / 1" } ||
            viewer.FindName("ArtworkTitleText") is not TextBlock { Text: "Fanart" })
        {
            throw new InvalidOperationException("Preview 28 Fanart 类型筛选未生效。 ");
        }
        artworkTypeComboBox.SelectedIndex = 3;
        viewer.UpdateLayout();
        thumbnailPanel = viewer.FindName("ThumbnailPanel") as StackPanel;
        localThumbnailPanel = viewer.FindName("LocalThumbnailPanel") as StackPanel;
        var onlineThumbnailSelectionBox = thumbnailPanel is null
            ? null
            : FindVisualChildren<CheckBox>(thumbnailPanel).FirstOrDefault();
        var localThumbnailSelectionBox = localThumbnailPanel is null
            ? null
            : FindVisualChildren<CheckBox>(localThumbnailPanel).SingleOrDefault();
        if (onlineThumbnailSelectionBox is null || localThumbnailSelectionBox is null)
        {
            throw new InvalidOperationException("Preview 33 本地删除选择与在线保存选择没有独立显示。 ");
        }
        localThumbnailSelectionBox.IsChecked = true;
        localThumbnailSelectionBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        deleteSelectedLocalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        viewer.UpdateLayout();
        if (localDeletionConfirmationCount != 1 ||
            deletedLocalArtworkPaths.Count != 0 ||
            viewer.DeletedLocalArtworkLocations.Count != 0 ||
            viewer.SelectedSampleLocations.Count != 2 ||
            !sampleItem.IsSelectedForDeletion ||
            deleteSelectedLocalButton.Content?.ToString() != "删除所选（1）" ||
            sampleSelectionCheckBox.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("Preview 33 本地图片批量删除未经过强制确认，或取消后改变了选择。 ");
        }
        allowLocalDeletion = true;
        deleteSelectedLocalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        viewer.UpdateLayout();
        if (localDeletionConfirmationCount != 2 ||
            viewer.SelectedSampleLocations.Count != 1 ||
            viewer.DeletedLocalArtworkLocations.Count != 1 ||
            deletedLocalArtworkPaths.Single() != Path.GetFullPath(sampleItem.SourceLocation!) ||
            viewer.FindName("ArtworkTitleText") is not TextBlock { Text: "Sample / extrafanart 2" } ||
            sampleSelectionCheckBox.Visibility != Visibility.Visible ||
            applySelectionButton.Visibility != Visibility.Visible ||
            viewer.FindName("LocalThumbnailSection") is not StackPanel { Visibility: Visibility.Collapsed })
        {
            throw new InvalidOperationException("Preview 33 确认后没有立即删除所选本地 Extra Fanart 或更新独立行。 ");
        }

        artworkTypeComboBox.SelectedIndex = 0;
        viewer.UpdateLayout();
        var selectAllLocalButton = (Button)viewer.FindName("SelectAllLocalButton");
        selectAllLocalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        viewer.UpdateLayout();
        if (deleteSelectedLocalButton.Content?.ToString() != "删除所选（2）")
        {
            throw new InvalidOperationException("Preview 33 本地图片全选没有更新批量删除数量。 ");
        }
        deleteSelectedLocalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        viewer.UpdateLayout();
        if (localDeletionConfirmationCount != 3 ||
            viewer.DeletedLocalArtworkLocations.Count != 3 ||
            deletedLocalArtworkPaths.Count != 3 ||
            !deletedLocalArtworkPaths.Contains(Path.GetFullPath(localPosterItem.SourceLocation!), StringComparer.OrdinalIgnoreCase) ||
            !deletedLocalArtworkPaths.Contains(Path.GetFullPath(localFanartItem.SourceLocation!), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Preview 33 本地 poster 与 fanart 无法一次勾选并批量删除。 ");
        }

        artworkTypeComboBox.SelectedIndex = 3;
        viewer.UpdateLayout();
        onlineThumbnailSelectionBox = FindVisualChildren<CheckBox>(
            (StackPanel)viewer.FindName("ThumbnailPanel")).SingleOrDefault();
        if (onlineThumbnailSelectionBox is null)
        {
            throw new InvalidOperationException("Preview 33 删除本地图片后在线选择框丢失。 ");
        }
        onlineThumbnailSelectionBox.IsChecked = false;
        onlineThumbnailSelectionBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        viewer.UpdateLayout();
        if (viewer.SelectedSampleLocations.Count != 0 ||
            onlineSampleItem.IsSelected ||
            viewer.FindName("ArtworkDimensionsText") is not TextBlock { Text: "图片尺寸：1920 × 1080" } ||
            viewer.FindName("LocalThumbnailSection") is not StackPanel { Visibility: Visibility.Collapsed } ||
            viewer.FindName("OnlineThumbnailSection") is not StackPanel { Visibility: Visibility.Visible })
        {
            throw new InvalidOperationException("Preview 33 在线图片保存选择与本地删除入口隔离不正确。 ");
        }
        viewer.Close();
        var sourceComparisonViewer = new ArtworkViewerWindow(
            [
                new ArtworkViewerItem(
                    "LibreDMM poster",
                    poster,
                    "https://example.test/libredmm-cover.jpg",
                    kind: ArtworkViewerItemKind.Poster,
                    artworkSourceName: "libredmm",
                    requiresPosterCrop: true,
                    artworkSourceDisplayName: "LibreDMM",
                    artworkSourceDescription: "poster / fanart 共用此完整封套"),
                new ArtworkViewerItem(
                    "LibreDMM fanart",
                    wideArtwork,
                    "https://example.test/libredmm-cover.jpg",
                    kind: ArtworkViewerItemKind.Fanart,
                    artworkSourceName: "libredmm",
                    artworkSourceDisplayName: "LibreDMM",
                    artworkSourceDescription: "poster / fanart 共用此完整封套"),
                new ArtworkViewerItem(
                    "LibreDMM sample 1",
                    wideArtwork,
                    "https://example.test/libredmm-sample-1.jpg",
                    isSelectableSample: true,
                    isSelected: true,
                    relatedSourceNames: ["libredmm"]),
                new ArtworkViewerItem(
                    "LibreDMM sample 2",
                    wideArtwork,
                    "https://example.test/libredmm-sample-2.jpg",
                    isSelectableSample: true,
                    isSelected: true,
                    relatedSourceNames: ["libredmm"]),
                new ArtworkViewerItem(
                    "R18.dev poster",
                    poster,
                    "https://example.test/r18-cover.jpg",
                    kind: ArtworkViewerItemKind.Poster,
                    artworkSourceName: "r18dev",
                    requiresPosterCrop: true,
                    artworkSourceDisplayName: "R18.dev",
                    artworkSourceDescription: "poster / fanart 共用此完整封套"),
                new ArtworkViewerItem(
                    "R18.dev fanart",
                    wideArtwork,
                    "https://example.test/r18-cover.jpg",
                    kind: ArtworkViewerItemKind.Fanart,
                    artworkSourceName: "r18dev",
                    artworkSourceDisplayName: "R18.dev",
                    artworkSourceDescription: "poster / fanart 共用此完整封套"),
                new ArtworkViewerItem(
                    "R18.dev sample 1",
                    wideArtwork,
                    "https://example.test/r18-sample-1.jpg",
                    isSelectableSample: true,
                    isSelected: false,
                    relatedSourceNames: ["r18dev"]),
                new ArtworkViewerItem(
                    "R18.dev sample 2",
                    wideArtwork,
                    "https://example.test/r18-sample-2.jpg",
                    isSelectableSample: true,
                    isSelected: false,
                    relatedSourceNames: ["r18dev"])
            ],
            0,
            imageLoader: null,
            initialKind: null,
            currentArtworkSourceName: "libredmm");
        var sourceComparisonVerified = false;
        var sourceSelectionVerified = false;
        var sourceScopedSamplesVerified = false;
        sourceComparisonViewer.Loaded += (_, _) => sourceComparisonViewer.Dispatcher.BeginInvoke(() =>
        {
            sourceComparisonViewer.UpdateLayout();
            if (sourceComparisonViewer.FindName("ArtworkTypeComboBox") is not ComboBox
                {
                    SelectedIndex: 0
                } ||
                sourceComparisonViewer.FindName("ArtworkPositionText") is not TextBlock { Text: "1 / 4" } ||
                sourceComparisonViewer.FindName("ArtworkTitleText") is not TextBlock { Text: "LibreDMM poster" } ||
                sourceComparisonViewer.FindName("ArtworkSourceLabelText") is not TextBlock { Text: "来源" } ||
                sourceComparisonViewer.FindName("ArtworkSourceSelectorPanel") is not StackPanel
                {
                    Visibility: Visibility.Visible
                } ||
                sourceComparisonViewer.FindName("ArtworkSourceSelectorButton") is not Button
                {
                    IsEnabled: true,
                    Content: "LibreDMM ▾"
                } artworkSourceSelectorButton ||
                sourceComparisonViewer.FindName("ThumbnailPanel") is not StackPanel
                {
                    Children.Count: 4
                } initialOnlinePanel ||
                FindVisualChildren<CheckBox>(initialOnlinePanel).Count(box => box.IsChecked == true) != 2 ||
                sourceComparisonViewer.FindName("SampleSelectionSummaryText") is not TextBlock
                {
                    Text: "已选择 2 / 2 张剧照"
                } ||
                sourceComparisonViewer.FindName("ApplySelectionButton") is not Button
                {
                    Visibility: Visibility.Visible,
                    Content: "应用图片设置"
                } applyImageSettingsButton)
            {
                sourceComparisonViewer.Close();
                return;
            }

            sourceComparisonVerified = true;
            artworkSourceSelectorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = artworkSourceSelectorButton.ContextMenu;
            var r18SourceItem = menu?.Items.OfType<MenuItem>().SingleOrDefault(item =>
                item.Tag is string sourceName && sourceName == "r18dev");
            if (menu is null ||
                menu.Style != sourceComparisonViewer.FindResource("CandidateContextMenu") ||
                menu.Items.OfType<MenuItem>().Count() != 2 ||
                r18SourceItem is null ||
                r18SourceItem.Style != sourceComparisonViewer.FindResource("CandidateMenuItem"))
            {
                sourceComparisonViewer.Close();
                return;
            }

            r18SourceItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            sourceComparisonViewer.UpdateLayout();
            var r18OnlinePanel = (StackPanel)sourceComparisonViewer.FindName("ThumbnailPanel");
            var r18SelectionBoxes = FindVisualChildren<CheckBox>(r18OnlinePanel).ToArray();
            sourceSelectionVerified =
                artworkSourceSelectorButton.Content?.ToString() == "R18.dev ▾" &&
                sourceComparisonViewer.FindName("ArtworkTitleText") is TextBlock { Text: "R18.dev poster" } &&
                sourceComparisonViewer.FindName("ArtworkPositionText") is TextBlock { Text: "1 / 4" } &&
                r18OnlinePanel.Children.Count == 4 &&
                r18OnlinePanel.Children.OfType<Border>().All(border =>
                    border.Tag is ArtworkViewerItem item && item.BelongsToSource("r18dev")) &&
                r18SelectionBoxes.Length == 2 &&
                r18SelectionBoxes.All(box => box.IsChecked == true) &&
                sourceComparisonViewer.SelectedSampleLocations.Count == 2 &&
                sourceComparisonViewer.SelectedSampleLocations.All(location =>
                    location.Contains("r18-sample", StringComparison.Ordinal)) &&
                sourceComparisonViewer.SelectedArtworkSourceName == "r18dev" &&
                sourceComparisonViewer.ArtworkSourceChanged;

            if (!sourceSelectionVerified)
            {
                sourceComparisonViewer.Close();
                return;
            }

            r18SelectionBoxes[0].IsChecked = false;
            r18SelectionBoxes[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            artworkSourceSelectorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var libreSourceItem = artworkSourceSelectorButton.ContextMenu?.Items.OfType<MenuItem>().SingleOrDefault(item =>
                item.Tag is string sourceName && sourceName == "libredmm");
            libreSourceItem?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            sourceComparisonViewer.UpdateLayout();
            var libreSelectionPreserved =
                artworkSourceSelectorButton.Content?.ToString() == "LibreDMM ▾" &&
                sourceComparisonViewer.SelectedSampleLocations.Count == 2 &&
                sourceComparisonViewer.SelectedSampleLocations.All(location =>
                    location.Contains("libredmm-sample", StringComparison.Ordinal));

            artworkSourceSelectorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            r18SourceItem = artworkSourceSelectorButton.ContextMenu?.Items.OfType<MenuItem>().SingleOrDefault(item =>
                item.Tag is string sourceName && sourceName == "r18dev");
            r18SourceItem?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            sourceComparisonViewer.UpdateLayout();
            sourceScopedSamplesVerified =
                libreSelectionPreserved &&
                sourceComparisonViewer.SelectedSampleLocations.Count == 1 &&
                sourceComparisonViewer.SelectedSampleLocations.Single().EndsWith(
                    "r18-sample-2.jpg",
                    StringComparison.Ordinal) &&
                FindVisualChildren<CheckBox>((StackPanel)sourceComparisonViewer.FindName("ThumbnailPanel"))
                    .Count(box => box.IsChecked == true) == 1;
            applyImageSettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        if (sourceComparisonViewer.ShowDialog() != true ||
            !sourceComparisonVerified ||
            !sourceSelectionVerified ||
            !sourceScopedSamplesVerified ||
            sourceComparisonViewer.SelectedArtworkSourceName != "r18dev" ||
            sourceComparisonViewer.SelectedSampleLocations.Count != 1)
        {
            throw new InvalidOperationException("Preview 42 来源切换没有过滤在线候选、联动样品图选择或统一应用图片设置。 ");
        }
        var deleteDialog = new AppDialogWindow(
            "确认删除本地图片",
            "这些文件将直接从磁盘删除，此操作无法撤销。",
            "确认删除")
        {
            Owner = window
        };
        deleteDialog.Show();
        deleteDialog.UpdateLayout();
        if (deleteDialog.Background is not SolidColorBrush dialogBackground ||
            dialogBackground.Color.R > 40 ||
            deleteDialog.FindName("DialogMessageText") is not TextBlock
            {
                Text: "这些文件将直接从磁盘删除，此操作无法撤销。"
            } ||
            deleteDialog.FindName("ConfirmButton") is not Button
            {
                Content: "确认删除"
            } dialogConfirmButton ||
            dialogConfirmButton.Background is not SolidColorBrush dialogConfirmBackground ||
            dialogConfirmBackground.Color.R < 80 ||
            dialogConfirmButton.IsDefault ||
            deleteDialog.FindName("CancelButton") is not Button
            {
                IsDefault: true,
                IsKeyboardFocused: true
            })
        {
            throw new InvalidOperationException("Preview 40 删除确认没有使用深色样式或默认聚焦取消。 ");
        }
        deleteDialog.Close();
        var applyViewer = new ArtworkViewerWindow(
            [new ArtworkViewerItem(
                "Sample / extrafanart 1",
                poster,
                "https://example.test/sample1.jpg",
                isSelectableSample: true,
                isSelected: false)],
            0);
        applyViewer.Loaded += (_, _) => applyViewer.Dispatcher.BeginInvoke(() =>
        {
            (applyViewer.FindName("SelectAllSamplesButton") as Button)?.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            (applyViewer.FindName("ApplySelectionButton") as Button)?.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
        });
        if (applyViewer.ShowDialog() != true || applyViewer.SelectedSampleLocations.Count != 1)
        {
            throw new InvalidOperationException("Preview 28 应用 extrafanart 选择没有返回确认结果。 ");
        }
        var saveChevronRight = saveSettingsChevronPath
            .TranslatePoint(new Point(saveSettingsChevronPath.ActualWidth, 0), window).X;
        var saveTitleLeft = saveSettingsTitleText.TranslatePoint(new Point(0, 0), window).X;
        var collapsedSaveChevronSize = new Size(
            saveSettingsChevronPath.ActualWidth,
            saveSettingsChevronPath.ActualHeight);
        if (saveSettingsOptionsPanel.Visibility != Visibility.Collapsed ||
            saveSettingsChevronPath.RenderTransform is not RotateTransform { Angle: 0 } ||
            saveChevronRight > saveTitleLeft + 0.5 ||
            !ReferenceEquals(
                saveSettingsChevronPath.Style,
                window.FindResource("DisclosureChevronPath") as Style))
        {
            throw new InvalidOperationException("Preview 35 保存设置箭头没有与标题组成左侧折叠入口。 ");
        }
        saveSettingsToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        if (saveSettingsOptionsPanel.Visibility != Visibility.Visible ||
            saveSettingsChevronPath.RenderTransform is not RotateTransform { Angle: 90 } ||
            Math.Abs(saveSettingsChevronPath.ActualWidth - collapsedSaveChevronSize.Width) > 0.1 ||
            Math.Abs(saveSettingsChevronPath.ActualHeight - collapsedSaveChevronSize.Height) > 0.1)
        {
            throw new InvalidOperationException("Preview 36 保存设置无法展开，或两个方向的箭头尺寸不一致。 ");
        }
        saveSettingsToggleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        if (saveSettingsOptionsPanel.Visibility != Visibility.Collapsed ||
            saveSettingsChevronPath.RenderTransform is not RotateTransform { Angle: 0 })
        {
            throw new InvalidOperationException("Preview 35 保存设置无法收起或箭头方向不正确。 ");
        }
        var sourceProfileWindow = new SourceProfileWindow(new MetadataSourcePreferenceProfile())
        {
            Owner = window
        };
        sourceProfileWindow.Show();
        sourceProfileWindow.UpdateLayout();
        if (!sourceProfileWindow.IsVisible ||
            sourceProfileWindow.Rows.Count != Enum.GetValues<MetadataField>().Length + 1 ||
            sourceProfileWindow.Rows.Single(row => row.Field == MetadataField.Title).SourceName !=
                MetadataSourcePreferenceProfile.LibreDmm ||
            sourceProfileWindow.Rows.Single(row => row.Field == MetadataField.Plot).SourceName !=
                MetadataSourcePreferenceProfile.LibreDmm ||
            sourceProfileWindow.Rows.Single(row => row.IsArtwork).SourceName !=
                MetadataSourcePreferenceProfile.LibreDmm ||
            sourceProfileWindow.FindName("SaveButton") is not Button { IsDefault: true })
        {
            throw new InvalidOperationException("Preview 12 自定义字段来源窗口没有采用预期默认规则。 ");
        }
        var profileSourceComboBox = FindVisualChildren<ComboBox>(sourceProfileWindow).FirstOrDefault()
            ?? throw new InvalidOperationException("自定义来源窗口没有生成来源下拉框。 ");
        profileSourceComboBox.ApplyTemplate();
        var profilePopup = profileSourceComboBox.Template.FindName("PART_Popup", profileSourceComboBox) as Popup
            ?? throw new InvalidOperationException("自定义来源下拉框没有深色弹出模板。 ");
        var profilePopupBorder = profilePopup.Child as Border
            ?? throw new InvalidOperationException("自定义来源下拉框没有弹出背景。 ");
        var profilePopupScrollViewer = FindVisualChildren<ScrollViewer>(profilePopupBorder).FirstOrDefault()
            ?? throw new InvalidOperationException("Preview 37 自定义来源下拉滚动视口未创建。 ");
        var sourceRowsScrollViewer = sourceProfileWindow.FindName("SourceRowsScrollViewer") as ScrollViewer
            ?? throw new InvalidOperationException("Preview 37 自定义来源内容滚动视口未创建。 ");
        if (profileSourceComboBox.Background is not SolidColorBrush profileBackground ||
            profileBackground.Color.R > 40 ||
            profilePopupBorder.Background is not SolidColorBrush profilePopupBackground ||
            profilePopupBackground.Color.R > 40 ||
            profileSourceComboBox.Foreground is not SolidColorBrush profileForeground ||
            profileForeground.Color.R < 180 ||
            PreciseScrollBehavior.GetAxis(profilePopupScrollViewer) != PreciseScrollAxis.Vertical ||
            PreciseScrollBehavior.GetAxis(sourceRowsScrollViewer) != PreciseScrollAxis.Vertical)
        {
            throw new InvalidOperationException("Preview 37 自定义来源窗口没有保持深色背景或统一像素滚动。 ");
        }
        sourceProfileWindow.Rows.Single(row => row.Field == MetadataField.Title).SourceName =
            MetadataSourcePreferenceProfile.R18Dev;
        sourceProfileWindow.Rows.Single(row => row.Field == MetadataField.Plot).SourceName =
            MetadataSourcePreferenceProfile.R18Dev;
        sourceProfileWindow.Rows.Single(row => row.IsArtwork).SourceName =
            MetadataSourcePreferenceProfile.BestArtworkResolution;
        var builtSourceProfile = sourceProfileWindow.BuildProfile();
        if (builtSourceProfile.TitleSource != MetadataSourcePreferenceProfile.R18Dev ||
            builtSourceProfile.PlotSource != MetadataSourcePreferenceProfile.R18Dev ||
            builtSourceProfile.ArtworkSource != MetadataSourcePreferenceProfile.BestArtworkResolution ||
            sourceProfileWindow.Rows.Single(row => row.Field == MetadataField.Title).Options.Count != 2 ||
            sourceProfileWindow.Rows.Single(row => row.IsArtwork).Options.Count != 3 ||
            sourceProfileWindow.Rows.Single(row => row.IsArtwork).Options.All(option =>
                option.Value != MetadataSourcePreferenceProfile.BestArtworkResolution))
        {
            throw new InvalidOperationException("Preview 41 自定义字段及高清整套图片来源规则无法保存。 ");
        }
        sourceProfileWindow.Close();
        if (window.FindName("FanartImage") is not System.Windows.Controls.Image ||
            window.FindName("DownloadExtrafanartCheckBox") is not CheckBox downloadExtrafanartCheckBox ||
            window.FindName("ReplaceLocalExtrafanartCheckBox") is not CheckBox replaceLocalExtrafanartCheckBox)
        {
            throw new InvalidOperationException("Preview 32 图片预览或 Extra Fanart 保存选项未创建。 ");
        }
        if (replaceLocalExtrafanartCheckBox.IsChecked == true ||
            replaceLocalExtrafanartCheckBox.IsEnabled ||
            replaceLocalExtrafanartCheckBox.Content?.ToString() != "替换本地剧照" ||
            string.IsNullOrWhiteSpace(replaceLocalExtrafanartCheckBox.ToolTip?.ToString()))
        {
            throw new InvalidOperationException("Preview 32 全面替换本地 Extra Fanart 没有保持安全默认值。 ");
        }
        downloadExtrafanartCheckBox.IsChecked = true;
        replaceLocalExtrafanartCheckBox.IsChecked = true;
        downloadExtrafanartCheckBox.IsChecked = false;
        if (replaceLocalExtrafanartCheckBox.IsChecked == true || replaceLocalExtrafanartCheckBox.IsEnabled)
        {
            throw new InvalidOperationException("Preview 32 关闭 Extra Fanart 输出后仍保留替换选项。 ");
        }
        if (window.FindName("FanartHintText") is not TextBlock fanartHintText ||
            fanartHintText.Visibility != Visibility.Visible ||
            fanartHintText.Text != string.Empty || fanartHintText.MinHeight < 14 ||
            window.FindName("FanartDropHint") is not null)
        {
            throw new InvalidOperationException("完整封套尚未加载时应保留无文字的固定间距。 ");
        }
        if (window.FindName("DropHint") is not StackPanel dropHint ||
            dropHint.Children.OfType<TextBlock>().All(text =>
                !text.Text.Contains("番号文件夹", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("拖放提示没有说明可直接拖入番号文件夹。 ");
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
        if (window.FindName("SkipSavePreviewCheckBox") is not CheckBox skipSavePreviewCheckBox ||
            skipSavePreviewCheckBox.IsChecked == true ||
            skipSavePreviewCheckBox.Content?.ToString() != "跳过保存预览" ||
            string.IsNullOrWhiteSpace(skipSavePreviewCheckBox.ToolTip?.ToString()))
        {
            throw new InvalidOperationException("Preview 14 统一保存预览选项未创建或没有保持安全默认值。 ");
        }
        if (window.FindName("DirectSaveOverwriteCheckBox") is not null ||
            window.FindName("SkipBatchSavePreviewCheckBox") is not null)
        {
            throw new InvalidOperationException("Preview 14 旧的单片或批量预览选项仍在界面。 ");
        }
        if (window.FindName("RememberPreferencesCheckBox") is not CheckBox rememberPreferencesCheckBox ||
            rememberPreferencesCheckBox.IsChecked == true ||
            rememberPreferencesCheckBox.Content?.ToString() != "记住当前设置" ||
            rememberPreferencesCheckBox.ToolTip?.ToString()?.Contains("搜索来源", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("Preview 22 当前设置记忆开关未创建、说明不完整或没有保持默认关闭。 ");
        }
        if (window.FindName("IncludeIdInTitleCheckBox") is not CheckBox includeIdInTitleCheckBox ||
            includeIdInTitleCheckBox.IsChecked != true ||
            includeIdInTitleCheckBox.Content?.ToString() != "标题包含番号" ||
            string.IsNullOrWhiteSpace(includeIdInTitleCheckBox.ToolTip?.ToString()))
        {
            throw new InvalidOperationException("Preview 10 标题番号全局选项未创建或没有默认开启。 ");
        }
        if (window.FindName("SkipCrossVolumeVerificationCheckBox") is not CheckBox skipVerificationCheckBox ||
            skipVerificationCheckBox.IsChecked == true ||
            skipVerificationCheckBox.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("v1.1 跨盘校验选项未创建或没有保持完整校验默认值。 ");
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
        skipSavePreviewCheckBox.IsChecked = true;
        applyPreferences.Invoke(window, [new AppPreferences
        {
            UiLanguage = UiLanguageCodes.SimplifiedChinese,
            RememberSavePreferences = true,
            SearchSourceMode = MetadataSearchSourceModes.R18Dev,
            SkipSavePreview = true,
            CrossVolumeVerification = CrossVolumeVerificationMode.FileSizeOnly,
            TargetMode = OrganizationTargetMode.CustomRootNumberFolder,
            CustomRootDirectory = preferencesRoot,
            RecentCustomRootDirectories = [secondPreferencesRoot, preferencesRoot],
            RenameVideo = true,
            WriteNfo = false,
            IncludeIdInTitle = false,
            DownloadPoster = false,
            DownloadFanart = true,
            DownloadExtrafanart = true,
            ReplaceLocalExtrafanart = true,
            CustomSourceProfile = new MetadataSourcePreferenceProfile
            {
                TitleSource = MetadataSourcePreferenceProfile.LibreDmm,
                PlotSource = MetadataSourcePreferenceProfile.R18Dev,
                ArtworkSource = MetadataSourcePreferenceProfile.R18Dev
            }
        }]);
        window.UpdateLayout();
        var remembered = capturePreferences.Invoke(window, null) as AppPreferences
            ?? throw new InvalidOperationException("v0.8.1 无法采集保存偏好。 ");
        var recentRootsButton = (Button)window.FindName("RecentRootsButton");
        if (skipSavePreviewCheckBox.IsChecked != true ||
            rememberPreferencesCheckBox.IsChecked != true ||
            (sourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() != MetadataSearchSourceModes.R18Dev ||
            remembered.SearchSourceMode != MetadataSearchSourceModes.R18Dev ||
            !remembered.SkipSavePreview ||
            remembered.CrossVolumeVerification != CrossVolumeVerificationMode.FileSizeOnly ||
            remembered.TargetMode != OrganizationTargetMode.CustomRootNumberFolder ||
            remembered.CustomRootDirectory != preferencesRoot ||
            !remembered.RenameVideo || remembered.WriteNfo || remembered.IncludeIdInTitle || remembered.DownloadPoster ||
            !remembered.DownloadFanart || !remembered.DownloadExtrafanart || !remembered.ReplaceLocalExtrafanart ||
            replaceLocalExtrafanartCheckBox.IsChecked != true || !replaceLocalExtrafanartCheckBox.IsEnabled ||
            remembered.CustomSourceProfile.TitleSource != MetadataSourcePreferenceProfile.LibreDmm ||
            remembered.CustomSourceProfile.PlotSource != MetadataSourcePreferenceProfile.R18Dev ||
            remembered.CustomSourceProfile.ArtworkSource != MetadataSourcePreferenceProfile.R18Dev ||
            remembered.RecentCustomRootDirectories.Length != 2 ||
            remembered.UiLanguage != UiLanguageCodes.SimplifiedChinese ||
            recentRootsButton.Content?.ToString() != "最近目录 (2) ▾" ||
            !recentRootsButton.IsEnabled ||
            window.FindName("CustomTargetPanel") is not Grid { Visibility: Visibility.Visible } ||
            typeof(AppPreferences).GetProperty("SkipSavePreview") is null ||
            typeof(AppPreferences).GetProperty("IncludeIdInTitle") is null ||
            typeof(AppPreferences).GetProperty("ReplaceLocalExtrafanart") is null)
        {
            throw new InvalidOperationException("Preview 22 没有恢复用户明确记住的当前设置与搜索来源。 ");
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
        if (skipSavePreviewCheckBox.IsChecked == true)
        {
            throw new InvalidOperationException("Preview 14 安全默认值没有关闭跳过保存预览。 ");
        }
        if (skipVerificationCheckBox.IsChecked == true)
        {
            throw new InvalidOperationException("v1.1 安全默认值没有恢复完整 SHA-256 校验。 ");
        }
        if (includeIdInTitleCheckBox.IsChecked != true)
        {
            throw new InvalidOperationException("Preview 10 安全默认值没有恢复标题番号。 ");
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
        var saveSettingsToggleY = saveSettingsToggleButton.TranslatePoint(new Point(0, 0), window).Y;
        var targetModeY = targetModeComboBox.TranslatePoint(new Point(0, 0), window).Y;
        if (!ReferenceEquals(skipSavePreviewCheckBox.Parent, renameCheckBox.Parent) ||
            targetModeY <= saveSettingsToggleY)
        {
            throw new InvalidOperationException("Preview 23 命名与保存行为没有归入同一折叠组，或目标位置层级不正确。 ");
        }

        var titleSourceText = window.FindName("TitleSourceText") as Button
            ?? throw new InvalidOperationException("v0.5 标题来源标记未创建。 ");
        var directorSourceText = window.FindName("DirectorSourceText") as Button
            ?? throw new InvalidOperationException("v0.5 导演来源标记未创建。 ");
        var ratingSourceText = window.FindName("RatingSourceText") as Button
            ?? throw new InvalidOperationException("Preview 8 社区评分来源标记未创建。 ");
        var ratingTextBox = window.FindName("RatingTextBox") as TextBox
            ?? throw new InvalidOperationException("Preview 8 社区评分编辑框未创建。 ");
        if (titleSourceText.Visibility != Visibility.Collapsed ||
            directorSourceText.Visibility != Visibility.Collapsed ||
            ratingSourceText.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("没有资料时来源标记应保持隐藏。 ");
        }

        var primaryMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "日文标题",
            Rating = "4.5",
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
            ratingSourceText.Content?.ToString() != "LibreDMM" ||
            ratingTextBox.Text != "4.5" ||
            titleSourceText.Visibility != Visibility.Visible ||
            directorSourceText.Visibility != Visibility.Visible ||
            ratingSourceText.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("自动补全没有显示正确的字段来源。 ");
        }
        if (!titleSourceText.IsEnabled || directorSourceText.IsEnabled || ratingSourceText.IsEnabled)
        {
            throw new InvalidOperationException("多来源字段应可选，单一来源字段应保持只读。 ");
        }

        mergedMetadata.Rating = "4.8";
        if (ratingTextBox.Text != "4.8" ||
            ratingSourceText.Content?.ToString() != "手动编辑 ▾" ||
            !ratingSourceText.IsEnabled)
        {
            throw new InvalidOperationException("手动修改社区评分后没有显示手动来源。 ");
        }
        ratingSourceText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var libreRating = ratingSourceText.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item =>
            item.Tag is MetadataFieldCandidate candidate && candidate.Source.Name == "libredmm")
            ?? throw new InvalidOperationException("社区评分候选菜单缺少 LibreDMM。 ");
        libreRating.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (mergedMetadata.Rating != "4.5" ||
            ratingTextBox.Text != "4.5" ||
            ratingSourceText.Content?.ToString() != "LibreDMM ▾")
        {
            throw new InvalidOperationException("社区评分无法恢复 LibreDMM 来源。 ");
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
        if (artworkSourceButton.Parent is not StackPanel artworkHeaderActions ||
            !ReferenceEquals(artworkHeaderActions.Parent, artworkSourceHeader) ||
            Grid.GetRow(artworkSourceHeader) >= Grid.GetRow(posterPreviewBorder) ||
            artworkHeaderActions.HorizontalAlignment != HorizontalAlignment.Right ||
            !ReferenceEquals(artworkHeaderActions.Children[^1], artworkSourceButton))
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

        var javLibraryMetadata = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "JAVLibrary 标题",
            Rating = "8.9",
            SourceUrl = "https://www.javlibrary.com/cn/?v=rating-test",
            SourceName = "javlibrary",
            SourceDisplayName = "JAVLibrary"
        };
        applyMetadata.Invoke(window, [javLibraryMetadata, new MovieMetadata[] { javLibraryMetadata }]);
        window.UpdateLayout();
        if (ratingTextBox.Text != "8.9" ||
            ratingSourceText.Content?.ToString() != "JAVLibrary" ||
            ratingSourceText.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("JAVLibrary 评分没有以正确数值和来源显示在界面。 ");
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
        var localLibreSamplePath = Path.Combine(localTestRoot, "libredmm-sample.png");
        var localR18SamplePath = Path.Combine(localTestRoot, "r18dev-sample.png");
        File.WriteAllBytes(localVideoPath, [0x01, 0x02, 0x03]);
        File.WriteAllBytes(localPosterPath, onePixelPng);
        File.WriteAllBytes(localFanartPath, onePixelPng);
        File.WriteAllBytes(localLibreSamplePath, onePixelPng);
        File.WriteAllBytes(localR18SamplePath, onePixelPng);
        File.WriteAllText(localNfoPath, """
            <movie custom="keep">
              <id>IPX-123</id>
              <title>本地 NFO 标题</title>
              <rating>3.7</rating>
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
            localMetadata.Rating != "3.7" ||
            titleSourceText.Content?.ToString() != "本地 NFO" ||
            ratingSourceText.Content?.ToString() != "本地 NFO" ||
            ratingTextBox.Text != "3.7" ||
            titleSourceText.Visibility != Visibility.Visible ||
            ratingSourceText.Visibility != Visibility.Visible ||
            !statusText.Text.Contains("本地 NFO", StringComparison.Ordinal) ||
            !statusText.Text.Contains("poster + fanart", StringComparison.Ordinal) ||
            !statusText.Text.Contains(localNfoPath, StringComparison.OrdinalIgnoreCase) ||
            artworkSourceButton.Content?.ToString() != "本地图片 ▾" ||
            posterImage.Source is null || fanartImage.Source is null ||
            fanartHintText.Text != LocalizationService.Get("CoverCheck.Low") + " · 1 × 1" ||
            removeCurrentVideoButton.Visibility != Visibility.Visible ||
            !removeCurrentVideoButton.IsEnabled ||
            removeCurrentVideoButton.Content?.ToString() != "移除影片" ||
            removeCurrentVideoButton.ToolTip?.ToString()?.Contains("不删除磁盘文件", StringComparison.Ordinal) != true ||
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
            Rating = "4.6",
            CoverUrl = "https://images.example.test/local-libre-cover.jpg",
            ScreenshotUrls = [localLibreSamplePath],
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        var localR18 = new MovieMetadata
        {
            Id = "IPX-123",
            Title = "R18 English title",
            Director = "Online director",
            CoverUrl = "https://images.example.test/local-r18-cover.jpg",
            ScreenshotUrls = [localR18SamplePath],
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
            reviewedLocalMetadata.Rating != "4.6" ||
            titleSourceText.Content?.ToString() != "LibreDMM ▾" ||
            directorSourceText.Content?.ToString() != "LibreDMM ▾" ||
            ratingSourceText.Content?.ToString() != "LibreDMM ▾" ||
            ratingTextBox.Text != "4.6" ||
            artworkSourceButton.Content?.ToString() != "LibreDMM ▾" ||
            reviewedLocalMetadata.CoverUrl != localLibre.CoverUrl ||
            posterImage.Source is null || fanartImage.Source is null)
        {
            throw new InvalidOperationException("在线搜索后没有统一默认选择新的文字与图片资料。 ");
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
            fanartHintText.Text != LocalizationService.Get("CoverCheck.Low") + " · 1 × 1" ||
            !statusText.Text.Contains("同一来源生成", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("手动完整封套没有锁定并同时预览 poster/fanart。 ");
        }

        var openArtworkViewer = typeof(MainWindow).GetMethod(
            "OpenArtworkViewer",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 42 图片查看器入口未找到。 ");
        foreach (var (entryKind, expectedPosition, expectedRole) in new[]
                 {
                     ((ArtworkViewerItemKind?)null, "1 / 4", "海报裁切"),
                     ((ArtworkViewerItemKind?)ArtworkViewerItemKind.Poster, "1 / 4", "海报裁切"),
                     ((ArtworkViewerItemKind?)ArtworkViewerItemKind.Fanart, "2 / 4", "完整封套 / fanart")
                 })
        {
            var entryVerified = false;
            var stagedSourceCancelVerified = entryKind is not null;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                var openedViewer = application.Windows
                    .OfType<ArtworkViewerWindow>()
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Owner, window));
                if (openedViewer is null)
                {
                    return;
                }

                openedViewer.UpdateLayout();
                var viewerSourceButton = openedViewer.FindName("ArtworkSourceSelectorButton") as Button;
                entryVerified = openedViewer.FindName("ArtworkTypeComboBox") is ComboBox { SelectedIndex: 0 } &&
                                openedViewer.FindName("ArtworkPositionText") is TextBlock { Text: var position } &&
                                position == expectedPosition &&
                                openedViewer.FindName("ArtworkTitleText") is TextBlock { Text: var title } &&
                                title.Contains(expectedRole, StringComparison.Ordinal) &&
                                title.Contains("手动封套", StringComparison.Ordinal) &&
                                viewerSourceButton is
                                {
                                    Visibility: Visibility.Visible,
                                    IsEnabled: true,
                                    Content: "手动封套 ▾"
                                } &&
                                openedViewer.FindName("ArtworkSourceLabelText") is TextBlock { Text: "来源" } &&
                                openedViewer.FindName("OnlineEmptySourceText") is TextBlock
                                {
                                    Visibility: Visibility.Visible,
                                    Text: "手动封套 没有当前类型的在线候选"
                                } &&
                                openedViewer.FindName("ApplySelectionButton") is Button
                                {
                                    Visibility: Visibility.Visible,
                                    Content: "应用图片设置"
                                };
                if (entryVerified && entryKind is null)
                {
                    viewerSourceButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var stagedR18Source = viewerSourceButton.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(item =>
                        item.Tag is string sourceName && sourceName == "r18dev");
                    stagedR18Source?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    openedViewer.UpdateLayout();
                    stagedSourceCancelVerified =
                        openedViewer.ArtworkSourceChanged &&
                        openedViewer.SelectedArtworkSourceName == "r18dev" &&
                        viewerSourceButton.Content?.ToString() == "R18.dev ▾" &&
                        openedViewer.FindName("ThumbnailPanel") is StackPanel r18OnlineCandidates &&
                        r18OnlineCandidates.Children.Count == 3 &&
                        r18OnlineCandidates.Children.OfType<Border>().All(border =>
                            border.Tag is ArtworkViewerItem item && item.BelongsToSource("r18dev")) &&
                        openedViewer.SelectedSampleLocations.Count == 1 &&
                        openedViewer.SelectedSampleLocations.Contains(
                            localR18SamplePath,
                            StringComparer.OrdinalIgnoreCase);
                }
                openedViewer.Close();
            });
            openArtworkViewer.Invoke(window, [entryKind]);
            if (!entryVerified ||
                !stagedSourceCancelVerified ||
                artworkSourceButton.Content?.ToString() != "手动封套 ▾" ||
                !reviewedLocalMetadata.ScreenshotUrls.SequenceEqual(
                    [localLibreSamplePath],
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Preview 42 主界面 {expectedRole} 入口定位、来源过滤或关闭时放弃暂存变更不正确。 ");
            }
        }

        var cleanVideoPath = Path.Combine(localTestRoot, "IPX-124.mp4");
        File.WriteAllBytes(cleanVideoPath, [0x04]);
        WaitForTask((Task)(selectVideoAsync.Invoke(window, [cleanVideoPath])
            ?? throw new InvalidOperationException("第二个影片载入没有返回任务。 ")));
        window.UpdateLayout();
        var cleanMetadata = window.DataContext as MovieMetadata
            ?? throw new InvalidOperationException("第二个影片没有编辑模型。 ");
        if (cleanMetadata.Id != "IPX-124" || cleanMetadata.Title.Length != 0 ||
            titleSourceText.Visibility != Visibility.Collapsed || !saveButton.IsEnabled ||
            saveButton.ToolTip?.ToString()?.Contains("Ctrl+S", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("选择新影片后残留了上一个影片的本地或在线候选。 ");
        }

        var resolveMainShortcut = typeof(MainWindow).GetMethod(
            "ResolveMainShortcut",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 40 主窗口快捷键路由未找到。 ");
        foreach (var testComboBox in FindVisualChildren<ComboBox>(window))
        {
            testComboBox.IsDropDownOpen = false;
        }
        foreach (var testButton in FindVisualChildren<Button>(window))
        {
            if (testButton.ContextMenu is { } staleTestMenu)
            {
                staleTestMenu.IsOpen = false;
            }
        }
        var singleSaveShortcut = resolveMainShortcut.Invoke(window, [Key.S, ModifierKeys.Control]);
        var altDownShortcut = resolveMainShortcut.Invoke(window, [Key.Down, ModifierKeys.Alt]);
        var escapeShortcut = resolveMainShortcut.Invoke(window, [Key.Escape, ModifierKeys.None]);
        if (!ReferenceEquals(singleSaveShortcut, saveButton) ||
            altDownShortcut is not null ||
            escapeShortcut is not null)
        {
            var hasOpenTransientSurface = typeof(MainWindow).GetMethod(
                "HasOpenTransientSurface",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(window, null);
            throw new InvalidOperationException(
                $"Preview 40 快捷键路由错误：single={singleSaveShortcut is Button} " +
                $"alt={altDownShortcut is not null} esc={escapeShortcut is not null} " +
                $"saveVisible={saveButton.Visibility} saveEnabled={saveButton.IsEnabled} " +
                $"popup={hasOpenTransientSurface}。 ");
        }
        sourceComboBox.IsDropDownOpen = true;
        if (resolveMainShortcut.Invoke(window, [Key.S, ModifierKeys.Control]) is not null)
        {
            throw new InvalidOperationException("Preview 40 下拉框展开时仍会触发 Ctrl+S。 ");
        }
        sourceComboBox.IsDropDownOpen = false;

        var idTextBoxKeyDown = typeof(MainWindow).GetMethod(
            "IdTextBox_KeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 40 番号框 Enter 搜索入口未找到。 ");
        sourceComboBox.SelectedItem = sourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "manual");
        statusText.Text = "enter-test";
        var enterArgs = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            HwndSource.FromHwnd(handle),
            Environment.TickCount,
            Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Source = idTextBox
        };
        idTextBoxKeyDown.Invoke(window, [idTextBox, enterArgs]);
        if (!enterArgs.Handled || statusText.Text != "当前为手动模式，可以直接填写资料并保存")
        {
            throw new InvalidOperationException("Preview 40 番号框内 Enter 没有触发现有搜索流程。 ");
        }
        statusText.Text = "ime-test";
        var imeEnterArgs = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            HwndSource.FromHwnd(handle),
            Environment.TickCount,
            Key.ImeProcessed)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Source = idTextBox
        };
        idTextBoxKeyDown.Invoke(window, [idTextBox, imeEnterArgs]);
        if (imeEnterArgs.Handled || statusText.Text != "ime-test")
        {
            throw new InvalidOperationException("Preview 40 输入法处理中的 Enter 被误当作搜索。 ");
        }
        sourceComboBox.SelectedItem = sourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "libredmm");

        var activeOperationCancellationField = typeof(MainWindow).GetField(
            "_activeOperationCancellation",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 40 无法检查取消任务入口。 ");
        var cancelOperationButton = (Button)window.FindName("CancelOperationButton");
        using (var directCancellation = new CancellationTokenSource())
        {
            activeOperationCancellationField.SetValue(window, directCancellation);
            cancelOperationButton.Visibility = Visibility.Visible;
            cancelOperationButton.IsEnabled = true;
            cancelOperationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!directCancellation.IsCancellationRequested ||
                cancelOperationButton.IsEnabled ||
                statusText.Text != "正在取消并恢复文件，请稍候…")
            {
                throw new InvalidOperationException(
                    $"Preview 40 取消任务按钮状态错误：canceled={directCancellation.IsCancellationRequested} " +
                    $"enabled={cancelOperationButton.IsEnabled} status={statusText.Text}。 ");
            }

            activeOperationCancellationField.SetValue(window, null);
            cancelOperationButton.Visibility = Visibility.Collapsed;
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
                !targetPathHintText.Text.Contains("安全复制 + SHA-256", StringComparison.Ordinal) ||
                skipVerificationCheckBox.Visibility != Visibility.Visible ||
                skipVerificationCheckBox.IsChecked == true)
            {
                throw new InvalidOperationException("v1.1 跨盘符目标没有保持完整 SHA-256 默认值或显示校验选项。 ");
            }
            skipVerificationCheckBox.IsChecked = true;
            window.UpdateLayout();
            if (!targetPathHintText.Text.Contains("快速跨盘复制", StringComparison.Ordinal) ||
                !targetPathHintText.Text.Contains("风险自负", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("v1.1 快速跨盘模式没有显示明确风险提示。 ");
            }
            skipVerificationCheckBox.IsChecked = false;
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

        var smartImportRoot = Path.Combine(localTestRoot, "smart-import");
        var smartImportNested = Path.Combine(smartImportRoot, "nested");
        Directory.CreateDirectory(smartImportNested);
        var smartCd1 = Path.Combine(smartImportRoot, "OFJE-126-CD1.mp4");
        var smartCd2 = Path.Combine(smartImportRoot, "OFJE-126-CD2.mp4");
        var smartNestedVideo = Path.Combine(smartImportNested, "SONE-127.mkv");
        var smartUnknownVideo = Path.Combine(smartImportNested, "vacation.webm");
        File.WriteAllBytes(smartCd1, [0x11]);
        File.WriteAllBytes(smartCd2, [0x12]);
        File.WriteAllBytes(smartNestedVideo, [0x13]);
        File.WriteAllBytes(smartUnknownVideo, [0x14]);
        var smartNotes = Path.Combine(smartImportRoot, "notes.txt");
        File.WriteAllText(smartNotes, "ignored");
        var discoveryWindow = new MovieDiscoveryWindow(
            [smartImportRoot, smartImportNested, smartCd1, smartCd2, smartNestedVideo, smartUnknownVideo, smartNotes],
            [smartNestedVideo],
            includeSubdirectories: true)
        {
            Owner = window
        };
        discoveryWindow.Show();
        WaitForCondition(
            () => discoveryWindow.Items.Count == 3,
            "Preview 13 递归导入预览没有按逻辑影片完成分组。 ");
        discoveryWindow.UpdateLayout();
        var discoveryList = discoveryWindow.FindName("MovieGroupsList") as ListBox
            ?? throw new InvalidOperationException("Preview 13 逻辑影片导入列表未创建。 ");
        var discoveryAddButton = discoveryWindow.FindName("AddButton") as Button
            ?? throw new InvalidOperationException("Preview 13 导入确认按钮未创建。 ");
        var discoverySelectAll = discoveryWindow.FindName("SelectAllButton") as Button
            ?? throw new InvalidOperationException("Preview 13 导入全选按钮未创建。 ");
        var discoverySelectNone = discoveryWindow.FindName("SelectNoneButton") as Button
            ?? throw new InvalidOperationException("Preview 13 导入取消全选按钮未创建。 ");
        var discoveryCancelButton = FindVisualChildren<Button>(discoveryWindow)
            .SingleOrDefault(button => button.Content?.ToString() == "取消");
        var inputPathsScrollViewer = discoveryWindow.FindName("InputPathsScrollViewer") as ScrollViewer
            ?? throw new InvalidOperationException("Preview 14 输入路径滚动视口未创建。 ");
        if (discoveryWindow.FindName("IncludeSubdirectoriesCheckBox") is not CheckBox { IsChecked: true } ||
            discoveryList.Items.Count != 3 ||
            ScrollViewer.GetCanContentScroll(discoveryList) ||
            ScrollViewer.GetIsDeferredScrollingEnabled(discoveryList) ||
            PreciseScrollBehavior.GetAxis(discoveryList) != PreciseScrollAxis.Vertical ||
            inputPathsScrollViewer.VerticalScrollBarVisibility != ScrollBarVisibility.Auto ||
            ScrollViewer.GetCanContentScroll(inputPathsScrollViewer) ||
            inputPathsScrollViewer.IsDeferredScrollingEnabled ||
            PreciseScrollBehavior.GetAxis(inputPathsScrollViewer) != PreciseScrollAxis.Vertical ||
            inputPathsScrollViewer.ExtentHeight <= inputPathsScrollViewer.ViewportHeight ||
            discoveryWindow.Items.Single(item => item.FileSet.MovieBaseName == "OFJE-126").FileSet.Parts.Count != 2 ||
            discoveryWindow.Items.Single(item => item.PrimaryPath == smartNestedVideo) is not
                { CanSelect: false, IsSelected: false } ||
            discoveryWindow.Items.Single(item => item.PrimaryPath == smartUnknownVideo).StatusText != "番号待确认" ||
            discoveryWindow.Items.Count(item => item.CanSelect && item.IsSelected) != 2 ||
            discoveryCancelButton is not { IsCancel: true } ||
            !discoveryAddButton.IsEnabled)
        {
            throw new InvalidOperationException("Preview 13 递归、CD 分组、重复项或默认选择状态不正确。 ");
        }
        inputPathsScrollViewer.ScrollToTop();
        inputPathsScrollViewer.UpdateLayout();
        var preciseWheelArgs = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -30)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent,
            Source = inputPathsScrollViewer
        };
        inputPathsScrollViewer.RaiseEvent(preciseWheelArgs);
        inputPathsScrollViewer.UpdateLayout();
        if (!preciseWheelArgs.Handled || Math.Abs(inputPathsScrollViewer.VerticalOffset - 6.0) > 0.5)
        {
            throw new InvalidOperationException("Preview 15 路径滚轮没有按细粒度像素距离滚动。 ");
        }
        inputPathsScrollViewer.ScrollToTop();
        discoverySelectNone.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (discoveryWindow.Items.Any(item => item.CanSelect && item.IsSelected) || discoveryAddButton.IsEnabled)
        {
            throw new InvalidOperationException("Preview 13 导入预览无法取消全选。 ");
        }
        discoverySelectAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (discoveryWindow.Items.Count(item => item.CanSelect && item.IsSelected) != 2 ||
            !discoveryAddButton.IsEnabled)
        {
            throw new InvalidOperationException("Preview 13 导入预览无法恢复全选。 ");
        }
        discoveryWindow.Close();

        var directFolderRoot = Path.Combine(localTestRoot, "single-nested-import");
        var directFolderNested = Path.Combine(directFolderRoot, "movie");
        Directory.CreateDirectory(directFolderNested);
        var directNestedCd1 = Path.Combine(directFolderNested, "IPX-125-CD1.mp4");
        var directNestedCd2 = Path.Combine(directFolderNested, "IPX-125-CD2.mp4");
        File.WriteAllBytes(directNestedCd1, [0x15]);
        File.WriteAllBytes(directNestedCd2, [0x16]);
        var directFolderWindow = new MainWindow();
        directFolderWindow.Show();
        var importDiscoveredPathsAsync = typeof(MainWindow).GetMethod(
            "ImportDiscoveredPathsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 13 智能文件夹导入入口未找到。 ");
        WaitForTask((Task)(importDiscoveredPathsAsync.Invoke(
            directFolderWindow,
            new object[] { new[] { directFolderRoot } })
            ?? throw new InvalidOperationException("Preview 13 智能文件夹导入没有返回任务。 ")));
        directFolderWindow.UpdateLayout();
        var directFolderQueue = directFolderWindow.FindName("MovieQueueList") as ListBox
            ?? throw new InvalidOperationException("Preview 13 智能导入队列未创建。 ");
        if (directFolderQueue.Items.Count != 1 ||
            directFolderQueue.Items[0] is not MovieJob { VideoPath: var importedNestedVideo } importedMovie ||
            !string.Equals(importedNestedVideo, directNestedCd1, StringComparison.OrdinalIgnoreCase) ||
            importedMovie.VideoPaths.Count != 2 ||
            !importedMovie.VideoPaths.Contains(directNestedCd2, StringComparer.OrdinalIgnoreCase) ||
            directFolderWindow.FindName("SingleModeButton") is not RadioButton { IsChecked: true })
        {
            throw new InvalidOperationException("Preview 13 单一嵌套多 CD 影片没有直接加入单片工作区。 ");
        }
        var directRemoveCurrentButton = directFolderWindow.FindName("RemoveCurrentVideoButton") as Button
            ?? throw new InvalidOperationException("Preview 41 单片移除影片入口未创建。 ");
        directRemoveCurrentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => directFolderQueue.Items.Count == 0 &&
                  directRemoveCurrentButton.Visibility == Visibility.Collapsed,
            "Preview 41 单片移除影片后没有恢复空工作区。 ");
        if (!File.Exists(directNestedCd1) || !File.Exists(directNestedCd2) ||
            directFolderWindow.FindName("StatusText") is not TextBlock { Text: var removedStatus } ||
            !removedStatus.Contains("没有删除任何文件", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Preview 41 单片移除影片影响了磁盘文件或缺少安全提示。 ");
        }
        directFolderWindow.Close();

        var queueVideoA = Path.Combine(localTestRoot, "IPX-126.mp4");
        var queueVideoB = Path.Combine(localTestRoot, "IPX-127.mp4");
        File.WriteAllBytes(queueVideoA, [0x06]);
        File.WriteAllBytes(queueVideoB, [0x07]);
        var queueWindow = new MainWindow();
        queueWindow.Show();
        queueWindow.Width = 1000;
        var singleModeButton = queueWindow.FindName("SingleModeButton") as RadioButton
            ?? throw new InvalidOperationException("Preview 2 单片模式入口未创建。 ");
        var batchModeButton = queueWindow.FindName("BatchModeButton") as RadioButton
            ?? throw new InvalidOperationException("Preview 2 批量模式入口未创建。 ");
        var singleSavePanel = queueWindow.FindName("SingleSavePanel") as StackPanel
            ?? throw new InvalidOperationException("Preview 2 单片保存入口未创建。 ");
        var scanFolderButton = queueWindow.FindName("ScanFolderButton") as Button
            ?? throw new InvalidOperationException("Preview 2 批量扫描入口未创建。 ");
        var queuePanel = queueWindow.FindName("QueuePanel") as Border
            ?? throw new InvalidOperationException("Preview 2 队列面板未创建。 ");
        if (singleModeButton.IsChecked != true ||
            batchModeButton.IsChecked == true ||
            queuePanel.Visibility != Visibility.Collapsed ||
            singleSavePanel.Visibility != Visibility.Visible ||
            scanFolderButton.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("Preview 2 默认没有进入独立单片模式。 ");
        }

        var addMovieFilesAsync = typeof(MainWindow).GetMethod(
            "AddMovieFilesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Phase 2 多影片加入入口未找到。 ");
        var unidentifiedVideo = Path.Combine(localTestRoot, "vacation-focus.mp4");
        File.WriteAllBytes(unidentifiedVideo, [0x0A]);
        var focusWindow = new MainWindow();
        focusWindow.Show();
        focusWindow.Activate();
        WaitForTask((Task)(addMovieFilesAsync.Invoke(
            focusWindow,
            new object[] { new[] { unidentifiedVideo } })
            ?? throw new InvalidOperationException("Preview 40 未识别单片载入没有返回任务。 ")));
        focusWindow.UpdateLayout();
        var focusIdTextBox = focusWindow.FindName("IdTextBox") as TextBox
            ?? throw new InvalidOperationException("Preview 40 未识别单片番号框未找到。 ");
        if (!focusIdTextBox.IsKeyboardFocused ||
            focusWindow.DataContext is not MovieMetadata { Id.Length: 0 })
        {
            throw new InvalidOperationException("Preview 40 未识别单片载入后没有聚焦番号框。 ");
        }
        focusWindow.Close();
        window.Activate();
        var multipartCd1 = Path.Combine(localTestRoot, "IPX-128-CD1.mp4");
        var multipartCd2 = Path.Combine(localTestRoot, "IPX-128-CD2.mp4");
        File.WriteAllBytes(multipartCd1, [0x08]);
        File.WriteAllBytes(multipartCd2, [0x09]);
        var multipartQueueWindow = new MainWindow();
        multipartQueueWindow.Show();
        WaitForTask((Task)(addMovieFilesAsync.Invoke(
            multipartQueueWindow,
            new object[] { new[] { multipartCd2 } })
            ?? throw new InvalidOperationException("Preview 6 CD 分段加入没有返回任务。 ")));
        multipartQueueWindow.UpdateLayout();
        var multipartQueueList = multipartQueueWindow.FindName("MovieQueueList") as ListBox
            ?? throw new InvalidOperationException("Preview 6 CD 分段队列列表未创建。 ");
        var multipartTargetHint = multipartQueueWindow.FindName("TargetPathHintText") as TextBlock
            ?? throw new InvalidOperationException("Preview 7 多分段目标路径提示未创建。 ");
        var multipartJob = multipartQueueList.Items.Cast<object>().SingleOrDefault() as MovieJob;
        if (multipartJob?.VideoPartCount != 2 ||
            !multipartJob.FileName.Contains("(+1)", StringComparison.Ordinal) ||
            !multipartTargetHint.Text.Contains(
                Path.Combine(localTestRoot, "IPX-128"),
                StringComparison.OrdinalIgnoreCase) ||
            multipartQueueWindow.FindName("SeriesSourceText") is not null)
        {
            throw new InvalidOperationException(
                "Preview 7 CD1/CD2 未合并为一个队列项目、未自动使用番号文件夹，或系列字段仍在界面。 ");
        }
        multipartQueueWindow.Close();

        WaitForTask((Task)(addMovieFilesAsync.Invoke(
            queueWindow,
            new object[] { new[] { queueVideoA, queueVideoB } })
            ?? throw new InvalidOperationException("Phase 2 多影片加入没有返回任务。 ")));
        queueWindow.UpdateLayout();
        var movieQueueList = queueWindow.FindName("MovieQueueList") as ListBox
            ?? throw new InvalidOperationException("Phase 2 队列列表未创建。 ");
        var queueCountText = queueWindow.FindName("QueueCountText") as TextBlock
            ?? throw new InvalidOperationException("Phase 2 队列计数未创建。 ");
        var removeSelectedQueueButton = queueWindow.FindName("RemoveSelectedQueueButton") as Button
            ?? throw new InvalidOperationException("Phase 2 队列删除按钮未创建。 ");
        var searchQueueButton = queueWindow.FindName("SearchQueueButton") as Button
            ?? throw new InvalidOperationException("Phase 3 批量搜索按钮未创建。 ");
        var saveSelectedButton = queueWindow.FindName("SaveSelectedButton") as Button
            ?? throw new InvalidOperationException("Preview 2 保存所选按钮未创建。 ");
        var queueSkipSavePreviewCheckBox = queueWindow.FindName("SkipSavePreviewCheckBox") as CheckBox
            ?? throw new InvalidOperationException("Preview 14 统一保存预览选项未找到。 ");
        var selectAllQueueButton = queueWindow.FindName("SelectAllQueueButton") as Button
            ?? throw new InvalidOperationException("Preview 2 队列全选按钮未创建。 ");
        var selectNoneQueueButton = queueWindow.FindName("SelectNoneQueueButton") as Button
            ?? throw new InvalidOperationException("Preview 2 队列取消全选按钮未创建。 ");
        var clearQueueButton = queueWindow.FindName("ClearQueueButton") as Button
            ?? throw new InvalidOperationException("Preview 2 清空队列按钮未创建。 ");
        var queueWriteNfoCheckBox = queueWindow.FindName("WriteNfoCheckBox") as CheckBox
            ?? throw new InvalidOperationException("Phase 4 当前影片保存设置未找到。 ");
        var queueIncludeIdInTitleCheckBox = queueWindow.FindName("IncludeIdInTitleCheckBox") as CheckBox
            ?? throw new InvalidOperationException("Preview 10 标题番号全局选项未找到。 ");
        var queueFilterComboBox = queueWindow.FindName("QueueFilterComboBox") as ComboBox
            ?? throw new InvalidOperationException("Phase 5 队列筛选器未创建。 ");
        var queueMetadataScrollViewer = queueWindow.FindName("MetadataScrollViewer") as ScrollViewer
            ?? throw new InvalidOperationException("Preview 23 批量资料滚动视口未创建。 ");
        var queueSplitter = queueWindow.FindName("QueueSplitter") as GridSplitter
            ?? throw new InvalidOperationException("Preview 23 队列宽度调节器未创建。 ");
        var previousQueueButton = queueWindow.FindName("PreviousQueueButton") as Button
            ?? throw new InvalidOperationException("Phase 5 上一个影片按钮未创建。 ");
        var nextQueueButton = queueWindow.FindName("NextQueueButton") as Button
            ?? throw new InvalidOperationException("Phase 5 下一个影片按钮未创建。 ");
        var queueSearchToolbarLayout = queueWindow.FindName("SearchToolbarLayout") as Grid
            ?? throw new InvalidOperationException("Preview 25 批量响应式搜索布局未创建。 ");
        var queueIdTextBox = queueWindow.FindName("IdTextBox") as TextBox
            ?? throw new InvalidOperationException("Preview 25 批量番号输入框未创建。 ");
        var queueSearchButton = queueWindow.FindName("SearchButton") as Button
            ?? throw new InvalidOperationException("Preview 25 批量搜索按钮未创建。 ");
        var queueBrowserImportButton = queueWindow.FindName("BrowserImportButton") as Button
            ?? throw new InvalidOperationException("Preview 25 批量网页查询按钮未创建。 ");
        var queueSourceComboBox = queueWindow.FindName("SourceComboBox") as ComboBox
            ?? throw new InvalidOperationException("Preview 25 批量自动来源选择框未创建。 ");
        var queueSourceSelectorHost = queueWindow.FindName("SourceSelectorHost") as Grid
            ?? throw new InvalidOperationException("Preview 38 批量组合来源控件未创建。 ");
        var queueSourceProfileButton = queueWindow.FindName("SourceProfileButton") as Button
            ?? throw new InvalidOperationException("Preview 25 批量来源规则按钮未创建。 ");
        var queueSourceProfileDivider = queueWindow.FindName("SourceProfileDivider") as Border
            ?? throw new InvalidOperationException("Preview 38 批量来源规则分隔线未创建。 ");
        var queueColumn = queueWindow.FindName("QueueColumn") as ColumnDefinition
            ?? throw new InvalidOperationException("Preview 25 队列列未创建。 ");
        var queueArtworkColumn = queueWindow.FindName("ArtworkColumn") as ColumnDefinition
            ?? throw new InvalidOperationException("Preview 25 图片列未创建。 ");
        movieQueueList.ApplyTemplate();
        queueWindow.UpdateLayout();
        if (!ReferenceEquals(
                resolveMainShortcut.Invoke(queueWindow, [Key.S, ModifierKeys.Control]),
                saveSelectedButton) ||
            saveSelectedButton.ToolTip?.ToString()?.Contains("Ctrl+S", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("Preview 40 批量模式 Ctrl+S 没有指向保存所选入口。 ");
        }
        var selectAllPosition = selectAllQueueButton.TranslatePoint(new Point(0, 0), queuePanel);
        var selectNonePosition = selectNoneQueueButton.TranslatePoint(new Point(0, 0), queuePanel);
        var removeSelectedPosition = removeSelectedQueueButton.TranslatePoint(new Point(0, 0), queuePanel);
        var clearQueuePosition = clearQueueButton.TranslatePoint(new Point(0, 0), queuePanel);
        if (Math.Abs(selectAllQueueButton.ActualWidth - selectNoneQueueButton.ActualWidth) > 1.5 ||
            Math.Abs(selectAllQueueButton.ActualWidth - removeSelectedQueueButton.ActualWidth) > 1.5 ||
            Math.Abs(selectAllQueueButton.ActualWidth - clearQueueButton.ActualWidth) > 1.5 ||
            Math.Abs(selectAllPosition.X - removeSelectedPosition.X) > 1.5 ||
            Math.Abs(selectNonePosition.X - clearQueuePosition.X) > 1.5 ||
            Math.Abs(selectAllPosition.Y - selectNonePosition.Y) > 1.5 ||
            Math.Abs(removeSelectedPosition.Y - clearQueuePosition.Y) > 1.5)
        {
            throw new InvalidOperationException("Preview 24 队列操作按钮没有形成等宽对齐的两列。 ");
        }
        var compactSearchCenterY = queueSearchButton.TranslatePoint(
            new Point(0, queueSearchButton.ActualHeight / 2),
            queueSearchToolbarLayout).Y;
        var compactIdCenterY = queueIdTextBox.TranslatePoint(
            new Point(0, queueIdTextBox.ActualHeight / 2),
            queueSearchToolbarLayout).Y;
        var compactBrowserCenterY = queueBrowserImportButton.TranslatePoint(
            new Point(0, queueBrowserImportButton.ActualHeight / 2),
            queueSearchToolbarLayout).Y;
        var compactSourceCenterY = queueSourceComboBox.TranslatePoint(
            new Point(0, queueSourceComboBox.ActualHeight / 2),
            queueSearchToolbarLayout).Y;
        if (Math.Abs(compactSearchCenterY - compactIdCenterY) > 1.5 ||
            compactBrowserCenterY <= compactSearchCenterY ||
            Math.Abs(compactBrowserCenterY - compactSourceCenterY) > 1.5 ||
            queueSearchToolbarLayout.RowDefinitions[1].Height.Value != 8 ||
            Math.Abs(queueColumn.Width.Value - 220) > 0.5 ||
            Math.Abs(queueArtworkColumn.Width.Value - 250) > 0.5)
        {
            throw new InvalidOperationException("Preview 25 最小批量窗口没有使用稳定的两行搜索栏或紧凑列宽。 ");
        }
        var compactSourceHostPosition = queueSourceSelectorHost.TranslatePoint(
            new Point(0, 0),
            queueSearchToolbarLayout);
        var compactSourceHostWidth = queueSourceSelectorHost.ActualWidth;
        var compactSourceHostRow = Grid.GetRow(queueSourceSelectorHost);
        var compactSourceHostColumn = Grid.GetColumn(queueSourceSelectorHost);
        var compactSourceHostColumnSpan = Grid.GetColumnSpan(queueSourceSelectorHost);
        queueSourceComboBox.SelectedItem = queueSourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "custom");
        queueWindow.UpdateLayout();
        var compactCustomSourceCenterY = queueSourceComboBox.TranslatePoint(
            new Point(0, queueSourceComboBox.ActualHeight / 2),
            queueSearchToolbarLayout).Y;
        var compactRulesCenterY = queueSourceProfileButton.TranslatePoint(
            new Point(0, queueSourceProfileButton.ActualHeight / 2),
            queueSearchToolbarLayout).Y;
        if (queueSourceProfileButton.Visibility != Visibility.Visible ||
            queueSourceProfileDivider.Visibility != Visibility.Visible ||
            Math.Abs(compactBrowserCenterY - compactCustomSourceCenterY) > 1.5 ||
            Math.Abs(compactBrowserCenterY - compactRulesCenterY) > 1.5 ||
            Grid.GetRow(queueSourceSelectorHost) != compactSourceHostRow ||
            Grid.GetColumn(queueSourceSelectorHost) != compactSourceHostColumn ||
            Grid.GetColumnSpan(queueSourceSelectorHost) != compactSourceHostColumnSpan ||
            Math.Abs(queueSourceSelectorHost.ActualWidth - compactSourceHostWidth) > 0.5 ||
            (queueSourceSelectorHost.TranslatePoint(new Point(0, 0), queueSearchToolbarLayout) -
             compactSourceHostPosition).Length > 0.5)
        {
            throw new InvalidOperationException("Preview 38 自定义来源使紧凑搜索栏发生了重排。 ");
        }
        queueSourceComboBox.SelectedItem = queueSourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "libredmm");
        queueWindow.UpdateLayout();
        if (queueSourceProfileButton.Visibility != Visibility.Collapsed ||
            queueSourceProfileDivider.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("Preview 38 离开自定义来源后内嵌规则入口没有隐藏。 ");
        }
        var queueScrollViewer = movieQueueList.Template.FindName("QueueScrollViewer", movieQueueList) as ScrollViewer
            ?? throw new InvalidOperationException("Preview 3 队列没有使用独立深色滚动视口。 ");
        var accentBrush = application.TryFindResource("AccentBrush") as SolidColorBrush
            ?? throw new InvalidOperationException("Preview 3 主按钮强调色未找到。 ");
        var inputBrush = application.TryFindResource("InputBrush") as SolidColorBrush
            ?? throw new InvalidOperationException("Preview 3 队列背景色未找到。 ");
        movieQueueList.IsEnabled = false;
        queueWindow.UpdateLayout();
        var queueStaysDarkWhenDisabled =
            queueScrollViewer.Background is SolidColorBrush disabledQueueViewportBrush &&
            disabledQueueViewportBrush.Color == inputBrush.Color;
        movieQueueList.IsEnabled = true;
        queueWindow.UpdateLayout();
        if (queuePanel.Visibility != Visibility.Visible ||
            batchModeButton.IsChecked != true ||
            singleSavePanel.Visibility != Visibility.Collapsed ||
            scanFolderButton.Visibility != Visibility.Visible ||
            queueSkipSavePreviewCheckBox.Visibility != Visibility.Visible ||
            movieQueueList.Items.Count != 2 ||
            !queueCountText.Text.Contains("2", StringComparison.Ordinal) ||
            searchQueueButton.Visibility != Visibility.Visible ||
            !searchQueueButton.IsEnabled ||
            !saveSelectedButton.IsEnabled ||
            !saveSelectedButton.Content.ToString()!.Contains("2", StringComparison.Ordinal) ||
            !previousQueueButton.IsEnabled ||
            !nextQueueButton.IsEnabled ||
            saveSelectedButton.Background is not SolidColorBrush saveSelectedBrush ||
            saveSelectedBrush.Color != accentBrush.Color ||
            movieQueueList.Background is not SolidColorBrush queueBackgroundBrush ||
            queueBackgroundBrush.Color != inputBrush.Color ||
            queueScrollViewer.Background is not SolidColorBrush queueViewportBrush ||
            queueViewportBrush.Color != inputBrush.Color ||
            !queueStaysDarkWhenDisabled ||
            VirtualizingPanel.GetScrollUnit(movieQueueList) != ScrollUnit.Pixel ||
            PreciseScrollBehavior.GetAxis(movieQueueList) != PreciseScrollAxis.Vertical ||
            PreciseScrollBehavior.GetAxis(queueMetadataScrollViewer) != PreciseScrollAxis.Vertical ||
            queueSplitter.Visibility != Visibility.Visible ||
            queueWindow.MinWidth != 930 ||
            Math.Abs(queueWindow.Width - 1000) > 0.5 ||
            queueWindow.DataContext is not MovieMetadata { Id: "IPX-126" } firstQueueMetadata)
        {
            throw new InvalidOperationException("Preview 3 多影片队列的显示、主操作色或像素滚动未正确启用。 ");
        }

        var firstQueueJob = movieQueueList.Items[0] as MovieJob
            ?? throw new InvalidOperationException("Phase 3 队列项没有绑定 MovieJob。 ");
        var secondQueueJob = movieQueueList.Items[1] as MovieJob
            ?? throw new InvalidOperationException("Phase 3 第二个队列项没有绑定 MovieJob。 ");
        if (!firstQueueJob.IsSelectedForBatch || !secondQueueJob.IsSelectedForBatch ||
            !FindVisualChildren<TextBlock>(movieQueueList).Any(text =>
                text.Text == "正在查看" && text.IsVisible) ||
            FindVisualChildren<TextBlock>(movieQueueList).Any(text => text.Text == "▶"))
        {
            throw new InvalidOperationException("Preview 23 队列查看状态、影片图标或批量勾选没有明确分离。 ");
        }

        var queueNormalHeight = queueWindow.Height;
        queueWindow.Height = queueWindow.MinHeight;
        queueWindow.UpdateLayout();
        queueMetadataScrollViewer.ScrollToEnd();
        queueWindow.UpdateLayout();
        if (queueMetadataScrollViewer.VerticalOffset <= 0)
        {
            throw new InvalidOperationException("Preview 23 测试条件不足：资料区未产生可滚动内容。 ");
        }
        movieQueueList.SelectedIndex = 1;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" } &&
                  queueMetadataScrollViewer.VerticalOffset <= 0.5,
            "Preview 23 切换影片后没有回到资料顶部。 ");
        movieQueueList.SelectedIndex = 0;
        queueWindow.Height = queueNormalHeight;
        queueWindow.UpdateLayout();

        queueIncludeIdInTitleCheckBox.IsChecked = false;
        WaitForCondition(
            () => firstQueueJob.SaveConfiguration?.SaveOptions.IncludeIdInTitle == false &&
                  secondQueueJob.SaveConfiguration?.SaveOptions.IncludeIdInTitle == false,
            "Preview 10 关闭标题番号没有同步到全部队列项目。 ");
        movieQueueList.SelectedIndex = 1;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" } &&
                  queueIncludeIdInTitleCheckBox.IsChecked == false,
            "Preview 10 切换影片后标题番号全局选项被单片设置覆盖。 ");
        queueIncludeIdInTitleCheckBox.IsChecked = true;
        WaitForCondition(
            () => firstQueueJob.SaveConfiguration?.SaveOptions.IncludeIdInTitle == true &&
                  secondQueueJob.SaveConfiguration?.SaveOptions.IncludeIdInTitle == true,
            "Preview 10 开启标题番号没有同步到全部队列项目。 ");
        movieQueueList.SelectedIndex = 0;

        selectNoneQueueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => !firstQueueJob.IsSelectedForBatch &&
                  !secondQueueJob.IsSelectedForBatch &&
                  !searchQueueButton.IsEnabled &&
                  !saveSelectedButton.IsEnabled,
            "Preview 2 取消全选没有同时影响批量搜索和保存。 ");
        selectAllQueueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => firstQueueJob.IsSelectedForBatch &&
                  secondQueueJob.IsSelectedForBatch &&
                  searchQueueButton.IsEnabled &&
                  saveSelectedButton.IsEnabled,
            "Preview 2 全选没有恢复统一批量选择。 ");
        firstQueueJob.IsSelectedForBatch = false;
        WaitForCondition(
            () => saveSelectedButton.Content.ToString()!.Contains("1", StringComparison.Ordinal),
            "Preview 2 保存按钮没有显示所选数量。 ");

        nextQueueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" },
            "Phase 5 下一个影片导航没有切换选中项。 ");
        previousQueueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-126" },
            "Phase 5 上一个影片导航没有切换选中项。 ");

        movieQueueList.Focus();
        Keyboard.Focus(movieQueueList);
        var queuePresentationSource = PresentationSource.FromVisual(queueWindow)
            ?? throw new InvalidOperationException("Preview 41 队列窗口没有键盘输入来源。 ");
        var queueDown = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            queuePresentationSource,
            Environment.TickCount,
            Key.Down)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = movieQueueList
        };
        movieQueueList.RaiseEvent(queueDown);
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" } &&
                  movieQueueList.IsKeyboardFocusWithin,
            "Preview 41 队列按 Down 后没有切换影片并恢复焦点。 ");
        var queueUp = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            queuePresentationSource,
            Environment.TickCount,
            Key.Up)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = movieQueueList
        };
        movieQueueList.RaiseEvent(queueUp);
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-126" } &&
                  movieQueueList.IsKeyboardFocusWithin,
            "Preview 41 队列连续按 Up 后没有继续切换影片。 ");
        if (!queueDown.Handled || !queueUp.Handled)
        {
            throw new InvalidOperationException("Preview 41 队列方向键没有限制在队列焦点内处理。 ");
        }

        var originalFirstId = firstQueueMetadata.Id;
        firstQueueMetadata.Id = string.Empty;
        queueFilterComboBox.SelectedIndex = 2;
        WaitForCondition(
            () => movieQueueList.Items.Count == 1 && ReferenceEquals(movieQueueList.Items[0], firstQueueJob),
            "Preview 2 问题筛选没有显示缺少番号的影片。 ");
        firstQueueMetadata.Id = originalFirstId;
        queueFilterComboBox.SelectedIndex = 0;
        WaitForCondition(
            () => movieQueueList.Items.Count == 2,
            "Phase 5 返回全部筛选后没有恢复队列。 ");
        firstQueueMetadata.Title = "队列未保存标题";
        movieQueueList.SelectedIndex = 1;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" },
            "Phase 2 无法切换到第二个影片。 ");
        movieQueueList.SelectedIndex = 0;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-126", Title: "队列未保存标题" },
            "Phase 2 队列切换后没有保留第一个影片的编辑。 ");
        queueWriteNfoCheckBox.IsChecked = false;
        WaitForCondition(
            () => firstQueueJob.SaveConfiguration?.SaveOptions.WriteNfo == false,
            "Phase 4 当前影片的保存设置没有写入独立快照。 ");
        movieQueueList.SelectedIndex = 1;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" } &&
                  queueWriteNfoCheckBox.IsChecked == true,
            "Phase 4 切换影片后没有恢复各自的保存设置。 ");
        movieQueueList.SelectedIndex = 0;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-126" } &&
                  queueWriteNfoCheckBox.IsChecked == false,
            "Phase 4 返回影片后没有恢复修改过的保存设置。 ");

        movieQueueList.SelectedIndex = 1;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-127" },
            "Phase 2 删除前无法重新选择第二个影片。 ");
        var jobPreviewsField = typeof(MainWindow).GetField(
            "_jobPreviews",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 5 影片预览缓存未找到。 ");
        var jobPreviews = jobPreviewsField.GetValue(queueWindow) as MoviePreviewCache
            ?? throw new InvalidOperationException("Preview 5 影片预览缓存无法检查。 ");
        if (!jobPreviews.Contains(firstQueueJob) || !jobPreviews.Contains(secondQueueJob))
        {
            throw new InvalidOperationException("Preview 5 测试准备阶段没有建立两个影片预览缓存。 ");
        }

        var invalidateBatchPreviews = typeof(MainWindow).GetMethod(
            "InvalidateSucceededBatchSearchPreviews",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 5 批量搜索预览失效入口未找到。 ");
        invalidateBatchPreviews.Invoke(queueWindow, [new BatchMetadataSearchResult(
        [
            new BatchMetadataSearchItemResult(
                firstQueueJob,
                BatchMetadataSearchItemStatus.Succeeded,
                [],
                null),
            new BatchMetadataSearchItemResult(
                secondQueueJob,
                BatchMetadataSearchItemStatus.Failed,
                [],
                new InvalidOperationException("synthetic search failure"))
        ])]);
        if (jobPreviews.Contains(firstQueueJob) || !jobPreviews.Contains(secondQueueJob))
        {
            throw new InvalidOperationException("Preview 5 批量搜索没有只清除成功影片的旧封套缓存。 ");
        }

        firstQueueJob.IsSelectedForBatch = false;
        secondQueueJob.IsSelectedForBatch = true;
        var removeQueueJobsAsync = typeof(MainWindow).GetMethod(
            "RemoveQueueJobsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 2 队列批量移除入口未找到。 ");
        if (removeQueueJobsAsync.GetParameters().Length != 1)
        {
            throw new InvalidOperationException("Preview 4 队列移除仍保留确认弹窗参数。 ");
        }
        WaitForTask((Task)(removeQueueJobsAsync.Invoke(
            queueWindow,
            new object[] { new[] { secondQueueJob } })
            ?? throw new InvalidOperationException("Preview 2 队列批量移除没有返回任务。 ")));
        WaitForCondition(
            () => movieQueueList.Items.Count == 1 &&
                  queuePanel.Visibility == Visibility.Visible &&
                  queueWindow.DataContext is MovieMetadata { Id: "IPX-126", Title: "队列未保存标题" },
            "Preview 2 移除所选后没有保留批量模式或相邻工作上下文。 ");

        singleModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => queuePanel.Visibility == Visibility.Collapsed &&
                  singleSavePanel.Visibility == Visibility.Visible &&
                  scanFolderButton.Visibility == Visibility.Collapsed,
            "Preview 2 无法一键切换到单片模式。 ");
        batchModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitForCondition(
            () => queuePanel.Visibility == Visibility.Visible &&
                  singleSavePanel.Visibility == Visibility.Collapsed &&
                  scanFolderButton.Visibility == Visibility.Visible,
            "Preview 2 无法一键恢复批量模式。 ");

        var scaleVideos = Enumerable.Range(200, 49)
            .Select(number => Path.Combine(localTestRoot, $"IPX-{number}.mp4"))
            .ToArray();
        foreach (var scaleVideo in scaleVideos)
        {
            File.WriteAllBytes(scaleVideo, [0x08]);
        }

        WaitForTask((Task)(addMovieFilesAsync.Invoke(queueWindow, new object[] { scaleVideos })
            ?? throw new InvalidOperationException("Phase 5 规模队列加入没有返回任务。 ")));
        queueWindow.UpdateLayout();
        WaitForCondition(
            () => movieQueueList.Items.Count == 50 &&
                  queuePanel.Visibility == Visibility.Visible &&
                  queueCountText.Text.Contains("50", StringComparison.Ordinal),
            "Phase 5 不能稳定显示 50 项合成队列。 ");
        movieQueueList.SelectedIndex = 49;
        WaitForCondition(
            () => queueWindow.DataContext is MovieMetadata { Id: "IPX-248" },
            "Phase 5 不能直接切换到 50 项队列末尾。 ");
        if (!clearQueueButton.IsEnabled || !removeSelectedQueueButton.IsEnabled)
        {
            throw new InvalidOperationException("Preview 2 规模队列的移除或清空命令不可用。 ");
        }

        var allQueueJobs = movieQueueList.Items.Cast<MovieJob>().ToArray();
        var removeCompletedQueueJobsAsync = typeof(MainWindow).GetMethod(
            "RemoveCompletedQueueJobsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 3 完成项自动移除入口未找到。 ");
        allQueueJobs[0].MarkSaveCompleted();
        WaitForTask((Task)(removeCompletedQueueJobsAsync.Invoke(
            queueWindow,
            new object[] { new[] { allQueueJobs[0] } })
            ?? throw new InvalidOperationException("Preview 3 完成项自动移除没有返回任务。 ")));
        WaitForCondition(
            () => movieQueueList.Items.Count == 49 &&
                  !movieQueueList.Items.Cast<MovieJob>().Any(job => job.SaveState is MovieSaveState.Completed),
            "Preview 3 保存完成的影片仍然滞留在队列。 ");

        allQueueJobs = movieQueueList.Items.Cast<MovieJob>().ToArray();
        WaitForTask((Task)(removeQueueJobsAsync.Invoke(
            queueWindow,
            new object[] { allQueueJobs })
            ?? throw new InvalidOperationException("Preview 2 清空队列没有返回任务。 ")));
        WaitForCondition(
            () => movieQueueList.Items.Count == 0 &&
                  queuePanel.Visibility == Visibility.Visible &&
                  !clearQueueButton.IsEnabled,
            "Preview 2 清空队列后没有保留空批量工作区。 ");
        queueWindow.Close();

        AppLog.ConfigureDirectory(null);
        Directory.Delete(localTestRoot, true);

        var previewPlan = new SavePlan(
            "C:\\Media\\source.mp4",
            "C:\\Media\\IPX-123\\IPX-123.mp4",
            "C:\\Media\\IPX-123",
            "IPX-123",
            new JavMetaLite.Core.Models.SaveOptions(true, false, false, false, true, false),
            new OrganizationOptions(true, true),
            [
                new PlannedFileChange(PlannedChangeKind.CreateFile, "生成 metadata", "C:\\Media\\IPX-123\\IPX-123.nfo"),
                new PlannedFileChange(PlannedChangeKind.UpdateFile, "更新 NFO", "C:\\Media\\IPX-123\\IPX-123.nfo"),
                new PlannedFileChange(PlannedChangeKind.KeepFile, "poster 内容保持不变", "C:\\Media\\IPX-123\\IPX-123-poster.jpg"),
                new PlannedFileChange(PlannedChangeKind.ReplaceImage, "替换 fanart", "C:\\Media\\IPX-123\\IPX-123-fanart.jpg"),
                new PlannedFileChange(PlannedChangeKind.RemoveFile, "移除未选剧照", "C:\\Media\\IPX-123\\extrafanart\\fanart2.jpg"),
                new PlannedFileChange(PlannedChangeKind.CopyAndVerifyVideo, "安全复制影片", "D:\\Media\\IPX-123\\IPX-123.mp4", "C:\\Media\\source.mp4"),
                new PlannedFileChange(PlannedChangeKind.CopyVideo, "快速复制影片", "E:\\Media\\IPX-123\\IPX-123.mp4", "C:\\Media\\source.mp4")
            ],
            [],
            []);
        var previewWindow = new SavePreviewWindow(previewPlan) { Owner = window };
        previewWindow.Show();
        previewWindow.Width = previewWindow.MinWidth;
        previewWindow.Height = previewWindow.MinHeight;
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
        var previewChangesScrollViewer = FindVisualChildren<ScrollViewer>(previewChanges).FirstOrDefault()
            ?? throw new InvalidOperationException("Preview 37 保存预览滚动视口未创建。 ");
        if (previewWindow.FindName("FooterButtonsPanel") is not StackPanel footerButtonsPanel ||
            previewWindow.FindName("PreviewPathPanel") is not Border previewPathPanel ||
            previewWindow.FindName("ChangesPanel") is not Border changesPanel ||
            !FitsInside(footerButtonsPanel, previewWindow) ||
            !FitsInside(previewPathPanel, previewWindow) ||
            !FitsInside(changesPanel, previewWindow) ||
            footerButtonsPanel.Children.OfType<Button>().Any(button => button.MinHeight < 36) ||
            footerButtonsPanel.Children.OfType<Button>().Single(button => button.IsCancel).IsDefault ||
            previewWindow.FindName("ConfirmButton") is not Button { IsDefault: false } ||
            !VirtualizingPanel.GetIsVirtualizing(previewChanges) ||
            VirtualizingPanel.GetScrollUnit(previewChanges) != ScrollUnit.Pixel ||
            !ScrollViewer.GetCanContentScroll(previewChanges) ||
            ScrollViewer.GetIsDeferredScrollingEnabled(previewChanges) ||
            PreciseScrollBehavior.GetAxis(previewChanges) != PreciseScrollAxis.Vertical ||
            previewChangesScrollViewer.ScrollableHeight <= 0)
        {
            throw new InvalidOperationException("Preview 37 保存预览在最小窗口下布局或像素滚动配置不完整。 ");
        }
        previewChangesScrollViewer.ScrollToTop();
        var savePreviewWheelArgs = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -30)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent,
            Source = previewChanges
        };
        previewChanges.RaiseEvent(savePreviewWheelArgs);
        previewWindow.UpdateLayout();
        if (!savePreviewWheelArgs.Handled ||
            Math.Abs(previewChangesScrollViewer.VerticalOffset - 6.0) > 0.5)
        {
            throw new InvalidOperationException("Preview 37 单片保存预览没有按细粒度像素距离滚动。 ");
        }
        var previewActions = previewChanges.Items.Cast<object>()
            .Select(item => item.GetType().GetProperty("Action")?.GetValue(item)?.ToString())
            .ToArray();
        if (!new[] { "生成", "更新", "保持不变", "替换图片", "移除", "复制并校验", "快速复制" }.All(previewActions.Contains))
        {
            throw new InvalidOperationException("dev4 保存预览没有区分创建、更新、保持不变和替换图片。 ");
        }
        previewWindow.Close();

        var shouldShowSavePreview = typeof(MainWindow).GetMethod(
            "ShouldShowSavePreview",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 14 单片预览安全策略未找到。 ");
        var overwriteSinglePlan = previewPlan with
        {
            OverwriteConflicts = ["C:\\Media\\IPX-123\\IPX-123.nfo"]
        };
        if ((bool)shouldShowSavePreview.Invoke(null, new object[] { true, previewPlan })! ||
            !(bool)shouldShowSavePreview.Invoke(null, new object[] { false, previewPlan })! ||
            !(bool)shouldShowSavePreview.Invoke(null, new object[] { true, overwriteSinglePlan })!)
        {
            throw new InvalidOperationException("Preview 14 单片预览跳过策略没有保留覆盖安全门。 ");
        }

        var batchPreviewJob = new MovieJob();
        batchPreviewJob.ResetForVideo("C:\\Media\\source.mp4", "IPX-123");
        batchPreviewJob.InitializeSaveConfiguration(new MovieSaveConfiguration(
            previewPlan.SaveOptions,
            previewPlan.OrganizationOptions));
        var batchPreviewWindow = new BatchSavePreviewWindow(
            [new BatchSavePreviewItem(batchPreviewJob, previewPlan, batchPreviewJob.ReviewRevision)],
            [new BatchSavePreviewIssue(batchPreviewJob, "缺少有效番号")])
        {
            Owner = window
        };
        batchPreviewWindow.Show();
        batchPreviewWindow.Width = batchPreviewWindow.MinWidth;
        batchPreviewWindow.Height = batchPreviewWindow.MinHeight;
        batchPreviewWindow.UpdateLayout();
        var batchIssuesScrollViewer = batchPreviewWindow.FindName("IssuesScrollViewer") as ScrollViewer
            ?? throw new InvalidOperationException("Preview 37 批量问题滚动视口未创建。 ");
        if (batchPreviewWindow.FindName("PlansList") is not ListBox { Items.Count: 1 } batchPlansList ||
            batchPreviewWindow.FindName("IssuesPanel") is not Border { Visibility: Visibility.Visible } ||
            batchPreviewWindow.FindName("ConfirmButton") is not Button { IsEnabled: true } ||
            FindVisualChildren<Button>(batchPreviewWindow).SingleOrDefault(button => button.IsCancel) is null ||
            !VirtualizingPanel.GetIsVirtualizing(batchPlansList) ||
            VirtualizingPanel.GetScrollUnit(batchPlansList) != ScrollUnit.Pixel ||
            !ScrollViewer.GetCanContentScroll(batchPlansList) ||
            ScrollViewer.GetIsDeferredScrollingEnabled(batchPlansList) ||
            PreciseScrollBehavior.GetAxis(batchPlansList) != PreciseScrollAxis.Vertical ||
            PreciseScrollBehavior.GetAxis(batchIssuesScrollViewer) != PreciseScrollAxis.Vertical)
        {
            throw new InvalidOperationException("Preview 37 批量计划或问题预览未使用统一像素滚动。 ");
        }
        var shouldShowBatchSavePreview = typeof(MainWindow).GetMethod(
            "ShouldShowBatchSavePreview",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 4 批量预览安全策略未找到。 ");
        var cleanBatchItems = new[]
        {
            new BatchSavePreviewItem(batchPreviewJob, previewPlan, batchPreviewJob.ReviewRevision)
        };
        if ((bool)shouldShowBatchSavePreview.Invoke(
                null,
                new object[] { true, cleanBatchItems, Array.Empty<BatchSavePreviewIssue>() })! ||
            !(bool)shouldShowBatchSavePreview.Invoke(
                null,
                new object[] { false, cleanBatchItems, Array.Empty<BatchSavePreviewIssue>() })! ||
            !(bool)shouldShowBatchSavePreview.Invoke(
                null,
                new object[]
                {
                    true,
                    cleanBatchItems,
                    new[] { new BatchSavePreviewIssue(batchPreviewJob, "缺少有效番号") }
                })!)
        {
            throw new InvalidOperationException("Preview 4 批量预览跳过策略没有区分干净批次和问题批次。 ");
        }
        batchPreviewWindow.Close();

        var issueOnlyBatchPreview = new BatchSavePreviewWindow(
            [],
            [new BatchSavePreviewIssue(batchPreviewJob, "目标路径无效")])
        {
            Owner = window
        };
        issueOnlyBatchPreview.Show();
        issueOnlyBatchPreview.UpdateLayout();
        if (issueOnlyBatchPreview.FindName("PlansList") is not ListBox { Items.Count: 0 } ||
            issueOnlyBatchPreview.FindName("IssuesPanel") is not Border { Visibility: Visibility.Visible } ||
            issueOnlyBatchPreview.FindName("ConfirmButton") is not Button { IsEnabled: false })
        {
            throw new InvalidOperationException("Preview 2 只有问题项时仍允许开始保存。 ");
        }
        issueOnlyBatchPreview.Close();

        var overwriteBatchPlan = previewPlan with
        {
            OverwriteConflicts = ["C:\\Media\\IPX-123\\IPX-123.nfo"]
        };
        if (!(bool)shouldShowBatchSavePreview.Invoke(
                null,
                new object[]
                {
                    true,
                    new[]
                    {
                        new BatchSavePreviewItem(
                            batchPreviewJob,
                            overwriteBatchPlan,
                            batchPreviewJob.ReviewRevision)
                    },
                    Array.Empty<BatchSavePreviewIssue>()
                })!)
        {
            throw new InvalidOperationException("Preview 4 未确认覆盖时错误跳过了安全预览。 ");
        }
        var overwriteBatchPreview = new BatchSavePreviewWindow(
            [new BatchSavePreviewItem(batchPreviewJob, overwriteBatchPlan, batchPreviewJob.ReviewRevision)])
        {
            Owner = window
        };
        overwriteBatchPreview.Show();
        overwriteBatchPreview.UpdateLayout();
        var overwriteBatchConfirm = overwriteBatchPreview.FindName("ConfirmButton") as Button
            ?? throw new InvalidOperationException("Phase 4 批量覆盖确认按钮未创建。 ");
        var overwriteBatchCheckBox = overwriteBatchPreview.FindName("OverwriteConfirmCheckBox") as CheckBox
            ?? throw new InvalidOperationException("Phase 4 批量覆盖复选框未创建。 ");
        if (overwriteBatchConfirm.IsEnabled || overwriteBatchCheckBox.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("Phase 4 批量覆盖没有要求明确确认。 ");
        }
        overwriteBatchCheckBox.IsChecked = true;
        if (!overwriteBatchConfirm.IsEnabled || !overwriteBatchPreview.AllowOverwrite)
        {
            throw new InvalidOperationException("Phase 4 批量覆盖确认没有解锁执行。 ");
        }
        overwriteBatchPreview.Close();
        batchPreviewJob.Dispose();

        var browserWindow = new BrowserWindow(new BrowserImportTarget(
            "r18dev",
            "R18.dev",
            "https://r18.dev/videos/vod/movies/detail/-/id=ipzz00850/",
            false,
            "IPZZ-850"));
        var browserRoot = browserWindow.Content as FrameworkElement
            ?? throw new InvalidOperationException("内置浏览器根布局未创建。 ");
        browserRoot.Measure(new Size(browserWindow.MinWidth, browserWindow.MinHeight));
        browserRoot.Arrange(new Rect(0, 0, browserWindow.MinWidth, browserWindow.MinHeight));
        browserRoot.UpdateLayout();
        if (browserWindow.FindName("BrowserToolbar") is not Border browserToolbar ||
            browserWindow.FindName("ImportButton") is not Button importButton ||
            browserWindow.FindName("BrowserInstruction") is not TextBlock browserInstruction ||
            browserWindow.FindName("BrowserStatusPanel") is not Border browserStatusPanel ||
            browserWindow.FindName("BrowserStatusText") is not TextBlock browserStatusText ||
            !browserWindow.Title.Contains("R18.dev", StringComparison.Ordinal) ||
            !browserInstruction.Text.Contains("R18.dev", StringComparison.Ordinal) ||
            browserStatusPanel.Visibility != Visibility.Collapsed ||
            importButton.IsEnabled ||
            !FitsInside(importButton, browserToolbar) ||
            importButton.MinHeight < 36)
        {
            throw new InvalidOperationException("v0.9 dev2 内置浏览器工具栏布局或按钮样式不完整。 ");
        }
        var applyR18PageState = typeof(BrowserWindow).GetMethod(
            "ApplyR18PageState",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 18 R18.dev 页面状态入口未创建。 ");
        applyR18PageState.Invoke(browserWindow, new object[] { BrowserImportPageState.NotFound });
        var notFoundBackground = (browserStatusPanel.Background as SolidColorBrush)?.Color;
        if (browserWindow.CurrentPageState != BrowserImportPageState.NotFound ||
            browserStatusPanel.Visibility != Visibility.Visible ||
            importButton.IsEnabled ||
            !browserStatusText.Text.Contains("R18.dev", StringComparison.Ordinal) ||
            !browserStatusText.Text.Contains("IPZZ-850", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Preview 18 未收录提示或读取按钮禁用状态不正确。 ");
        }
        applyR18PageState.Invoke(browserWindow, new object[] { BrowserImportPageState.Unavailable });
        var unavailableBackground = (browserStatusPanel.Background as SolidColorBrush)?.Color;
        if (browserWindow.CurrentPageState != BrowserImportPageState.Unavailable ||
            browserStatusPanel.Visibility != Visibility.Visible ||
            notFoundBackground == unavailableBackground)
        {
            throw new InvalidOperationException("Preview 18 网站不可用提示没有与未收录状态区分。 ");
        }
        applyR18PageState.Invoke(browserWindow, new object[] { BrowserImportPageState.Ready });
        if (browserWindow.CurrentPageState != BrowserImportPageState.Ready ||
            browserStatusPanel.Visibility != Visibility.Collapsed ||
            !importButton.IsEnabled)
        {
            throw new InvalidOperationException("Preview 18 有效详情页没有启用读取。 ");
        }
        browserWindow.Close();

        var activeJobField = typeof(MainWindow).GetField(
            "_activeJob",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 32 无法检查当前影片搜索状态。 ");
        var feedbackJob = activeJobField.GetValue(window) as MovieJob
            ?? throw new InvalidOperationException("Preview 32 当前影片状态不可用。 ");
        sourceComboBox.SelectedItem = sourceComboBox.Items.OfType<ComboBoxItem>()
            .Single(item => item.Tag?.ToString() == "custom");
        var feedbackMetadata = new MovieMetadata
        {
            Id = "IPZZ-850",
            Title = "Feedback test",
            SourceName = "libredmm",
            SourceDisplayName = "LibreDMM"
        };
        feedbackJob.ApplyOnlineSources(
            feedbackMetadata,
            [feedbackMetadata],
            [
                new MetadataSourceSearchAttempt(
                    "libredmm",
                    "LibreDMM",
                    TimeSpan.FromMilliseconds(150),
                    feedbackMetadata,
                    null,
                    2),
                new MetadataSourceSearchAttempt(
                    "r18dev",
                    "R18.dev",
                    TimeSpan.FromMilliseconds(200),
                    null,
                    new MetadataNotFoundException("R18.dev", "IPZZ-850"),
                    0)
            ]);
        var setStatus = typeof(MainWindow).GetMethod(
            "SetStatus",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview 32 底部状态入口不可用。 ");
        setStatus.Invoke(window, ["底部唯一状态测试", true]);
        window.UpdateLayout();
        if (window.FindName("SearchFeedbackPanel") is not null ||
            statusText.Text != "底部唯一状态测试" ||
            retryFailedSourcesButton.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("Preview 32 搜索状态与失败重试没有收敛到底部状态栏。 ");
        }
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        window.UpdateLayout();
        if (metadataScrollViewer.ActualHeight < 80 ||
            metadataScrollViewer.ScrollableHeight <= 0 ||
            !FitsInside(statusText, window) ||
            !FitsInside(retryFailedSourcesButton, window))
        {
            throw new InvalidOperationException("Preview 32 最小窗口中的底部状态栏或资料区域布局不正确。 ");
        }
        metadataScrollViewer.ScrollToTop();
        var metadataWheelArgs = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -30)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent,
            Source = metadataScrollViewer
        };
        metadataScrollViewer.RaiseEvent(metadataWheelArgs);
        window.UpdateLayout();
        if (!metadataWheelArgs.Handled || Math.Abs(metadataScrollViewer.VerticalOffset - 6.0) > 0.5)
        {
            throw new InvalidOperationException("Preview 37 主资料区没有按统一像素距离滚动。 ");
        }

        Console.WriteLine($"UI PASS  handle={handle} visible={window.IsVisible} title={window.Title} posterFrozen={poster.IsFrozen} comboDark=True dmmRecommendedLabel=True customSourceProfile=True integratedSourceSettings=True stableSourceLayout=True sourceActionGap=True minimalKeyboard=True popupShortcutGuard=True idEnterSearch=True imeEnterSafe=True directCancel=True destructiveCancelDefault=True unidentifiedIdFocus=True localizedShortcutHint=True libreDmm=True fanart=True previewWindow=True previewChangeKinds=True sourceBadges=True candidateMenus=True independentManualWebLookup=True browserSourceRouting=True r18HumanPage=True r18SafeHomeRouting=True r18MissingFallback=True r18UnavailableDistinct=True r18ReadGuard=True webLookupCopy=True fullDarkMenuTemplate=True fieldSwitch=True manualReturn=True unifiedArtworkSource=True artworkMenu=True localNfoLoad=True localNfoSaveEnabled=True localArtworkPreview=True localArtworkDefault=True localOnlineCandidates=True manualCoverPreview=True localManualReturn=True localFailureSafe=True staleCandidatesCleared=True preciseWheelScroll=True unifiedScrollSurfaces=True unifiedSavePreview=True targetModes=True customTargetPreview=True verifiedCopyHint=True cancelOperation=True improvedSpacing=True startupVideo=True recentRoots=True unavailableRootSafe=True workspaceModes=True smartImport=True movieQueue=True queueEditIsolation=True queueRemove=True queueClear=True batchSearchCommand=True batchSelection=True revisionGuard=True perJobSaveSettings=True batchSavePreview=True batchIssuePreview=True queueFilter=True queueNavigation=True metadataScrollReset=True deterministicSearchLayout=True compactBatchColumns=True alignedQueueActions=True collapsibleSaveSettings=True artworkRoleLabels=True artworkViewer=True viewerGlobalPosition=True artworkSourceComparison=True artworkSourceSelector=True sourceScopedOnlineCandidates=True stagedImageSettings=True backgroundExtrafanartPreload=True artworkTypeFilter=True darkArtworkTypeMenu=True artworkFilmstrip=True aspectAwareThumbnails=True artworkDimensions=True unobstructedDimensions=True confirmedDirectLocalDeletion=True localPosterDeletion=True localFanartDeletion=True filmstripSelection=True onlineExtrafanartSelection=True replaceLocalExtrafanart=True bottomOnlyStatus=True queueStatusPills=True queueFocusSeparation=True resizableColumns=True lightweightPlaceholder=True sharedTheme=True minimumLayout=True browserLayout=True dropdownGeometry=True systemFallbackEnglish=True captionColor={(nativeCaptionColor is null ? "unsupported" : "#111821")}");
        window.Close();
        application.Shutdown();
    }

    private static void TestSystemLanguageResolution()
    {
        var localizationType = typeof(MainWindow).Assembly.GetType("JavMetaLite.App.LocalizationService")
            ?? throw new InvalidOperationException("没有找到本地化服务。 ");
        var detectSystemLanguage = localizationType.GetMethod(
            "DetectSystemLanguage",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("没有找到系统语言检测入口。 ");
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var expectations = new[]
            {
                (Culture: "zh-CN", Expected: UiLanguageCodes.SimplifiedChinese),
                (Culture: "zh-TW", Expected: UiLanguageCodes.TraditionalChinese),
                (Culture: "en-US", Expected: UiLanguageCodes.English),
                (Culture: "ja-JP", Expected: UiLanguageCodes.Japanese),
                (Culture: "fr-FR", Expected: UiLanguageCodes.English)
            };
            foreach (var expectation in expectations)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(expectation.Culture);
                var actual = detectSystemLanguage.Invoke(null, null)?.ToString();
                if (!string.Equals(actual, expectation.Expected, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"系统语言 {expectation.Culture} 解析为 {actual}，预期为 {expectation.Expected}。 ");
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void TestPartialSourceQueueUi()
    {
        static string Text(string key, params object[] arguments) => string.Format(CultureInfo.CurrentCulture,
            (string)Application.Current.FindResource(key), arguments);
        var window = new MainWindow();
        window.Show();
        var languageBox = (ComboBox)window.FindName("LanguageComboBox");
        var originalLanguage = languageBox.SelectedItem;
        try
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var jobs = (IList)typeof(MainWindow).GetField("_movieQueue", flags)!.GetValue(window)!;
            var attach = typeof(MainWindow).GetMethod("AttachMovieJob", flags)!;
            using var job = new MovieJob();
            job.ResetForVideo(@"C:\Synthetic\IPX-081.mp4", "IPX-081");
            var source = new MovieMetadata { Id = "IPX-081", Title = "Synthetic source", SourceName = "libredmm", SourceDisplayName = "LibreDMM" };
            var failed = new MetadataSourceSearchAttempt("r18dev", "R18.dev", TimeSpan.Zero, null,
                new MetadataSourceRateLimitException(DateTimeOffset.UtcNow.AddMinutes(2)), 0);
            var success = new MetadataSourceSearchAttempt("libredmm", "LibreDMM", TimeSpan.Zero, source, null, 1);
            job.ApplyOnlineSources(source, [source], [success, failed]);
            attach.Invoke(window, [job]);
            jobs.Add(job);
            var batchSources = (ComboBox)window.FindName("SourceComboBox");
            batchSources.SelectedItem = batchSources.Items.OfType<ComboBoxItem>()
                .Single(item => item.Tag?.ToString() == "custom");
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var list = (ListBox)window.FindName("MovieQueueList");
            var filter = (ComboBox)window.FindName("QueueFilterComboBox");
            var retry = (Button)window.FindName("RetryFailedSourcesButton");
            filter.SelectedIndex = 2;
            window.UpdateLayout();
            WaitForCondition(() => list.Items.Count == 1 && retry.Visibility == Visibility.Visible,
                "Partial-source failure missing from issue filter or batch retry.");
            if (!FindVisualChildren<TextBlock>(list).Any(text => text.Text == Text("Queue.Search.Partial") &&
                text.ToolTip?.ToString() == Text("Error.SourceRateLimited")))
            {
                throw new InvalidOperationException("Partial-source badge or localized 429 explanation is missing.");
            }
            job.IsSelectedForBatch = false;
            WaitForCondition(() => retry.Visibility == Visibility.Collapsed, "Batch retry must respect selection.");
            job.IsSelectedForBatch = true;
            var summary = typeof(MainWindow).GetMethod("ShowBatchSearchSummary", flags)!;
            summary.Invoke(window, [new BatchMetadataSearchResult([
                new BatchMetadataSearchItemResult(job, BatchMetadataSearchItemStatus.PartiallySucceeded, [success, failed], null)]), "test"]);
            if (((TextBlock)window.FindName("StatusText")).Text != Text("Status.BatchSourcesComplete", 0, 1, 0, 0, 0))
            {
                throw new InvalidOperationException("Batch summary counted partial failure as complete success.");
            }
            foreach (var language in new[] { "zh-Hans", "zh-Hant", "en", "ja" })
            {
                languageBox.SelectedItem = languageBox.Items.Cast<ComboBoxItem>().Single(item => item.Tag?.ToString() == language);
                typeof(MainWindow).GetMethod("RefreshRetryFailedSourcesUi", flags)!.Invoke(window, []);
                window.Width = window.MinWidth;
                window.UpdateLayout();
                if (retry.Content?.ToString() != Text("SearchFeedback.RetryQueue", 1) || !FitsInside(retry, window))
                {
                    throw new InvalidOperationException("Localized batch retry label or minimum-width layout is invalid.");
                }
            }
            job.ApplyOnlineSources(source, [source], [success]);
            WaitForCondition(() => list.Items.Count == 0 && retry.Visibility == Visibility.Collapsed,
                "Recovered source did not clear issue filter and retry eligibility.");
            Console.WriteLine("UI PASS partialSourceBadge=True rateLimitTooltip=True issueFilter=True selectedRetry=True partialSummary=True retryLanguages=True recoveredIssueCleared=True");
        }
        finally
        {
            languageBox.SelectedItem = originalLanguage;
            window.Close();
        }
    }

    private static void TestCloseWaitsForOperation(bool observeCancellation)
    {
        var closingWindow = new MainWindow();
        closingWindow.Show();
        var runBusy = typeof(MainWindow).GetMethod("RunBusyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cancellationField = typeof(MainWindow).GetField("_activeOperationCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var recoveryAllowed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryStarted = false;
        var recoveryCompleted = false;
        var actuallyClosed = false;
        closingWindow.Closed += (_, _) => actuallyClosed = true;
        Func<Task> operation = async () =>
        {
            var cancellation = (CancellationTokenSource)cancellationField.GetValue(closingWindow)!;
            if (!observeCancellation)
            {
                // A commit/cleanup section may finish without observing cancellation.
                recoveryStarted = true;
                await recoveryAllowed.Task;
                recoveryCompleted = true;
                return;
            }
            try
            {
                await Task.Delay(Timeout.Infinite, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                recoveryStarted = true;
                await recoveryAllowed.Task;
                recoveryCompleted = true;
                throw;
            }
        };
        var running = (Task)runBusy.Invoke(closingWindow, ["close safety test", operation])!;
        try
        {
            closingWindow.Close();
            WaitForCondition(() => recoveryStarted, "Close did not request cancellation.");
            if (actuallyClosed || !closingWindow.IsVisible || recoveryCompleted)
            {
                throw new InvalidOperationException("Window closed before operation recovery finished.");
            }
            closingWindow.Close(); // Repeated close must still wait, without another dialog.
            if (actuallyClosed)
            {
                throw new InvalidOperationException("Repeated close bypassed pending recovery.");
            }
            recoveryAllowed.TrySetResult();
            WaitForTask(running);
            WaitForCondition(() => actuallyClosed, "Window did not close after safe operation completion.");
            if (!recoveryCompleted)
            {
                throw new InvalidOperationException("Recovery was not completed before disposing the window.");
            }
        }
        finally
        {
            recoveryAllowed.TrySetResult();
            WaitForTask(running);
            if (!actuallyClosed)
            {
                closingWindow.Close();
            }
        }
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

    private static void WaitForCondition(Func<bool> condition, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException(errorMessage);
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(20),
                DispatcherPriority.Background,
                (_, _) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static bool FitsInside(FrameworkElement child, FrameworkElement container, double tolerance = 1.5)
    {
        if (child.ActualWidth <= 0 || child.ActualHeight <= 0 ||
            container.ActualWidth <= 0 || container.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = child.TranslatePoint(new Point(0, 0), container);
        return topLeft.X >= -tolerance &&
               topLeft.Y >= -tolerance &&
               topLeft.X + child.ActualWidth <= container.ActualWidth + tolerance &&
               topLeft.Y + child.ActualHeight <= container.ActualHeight + tolerance;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static int? TryReadDwmIntAttribute(IntPtr handle, int attribute)
    {
        try
        {
            var result = DwmGetWindowAttribute(handle, attribute, out var value, Marshal.SizeOf<int>());
            return result == 0 ? value : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out int attributeValue,
        int attributeSize);
}
