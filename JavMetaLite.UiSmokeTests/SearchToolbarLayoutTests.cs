using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JavMetaLite.App;
using JavMetaLite.Core.Models;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestSearchToolbarLayout(double hostMaximumWidth = double.PositiveInfinity)
    {
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        var window = new MainWindow { MaxWidth = hostMaximumWidth };
        var minimumWidth = window.MinWidth;
        window.Show();
        var language = (ComboBox)window.FindName("LanguageComboBox");
        var toolbar = (Border)window.FindName("SearchToolbar");
        var layout = (Grid)window.FindName("SearchToolbarLayout");
        var source = (ComboBox)window.FindName("SourceComboBox");
        var host = (Grid)window.FindName("SourceSelectorHost");
        var gear = (Button)window.FindName("SourceProfileButton");
        var browser = (Button)window.FindName("BrowserImportButton");
        var search = (Button)window.FindName("SearchButton");
        var id = (TextBox)window.FindName("IdTextBox");
        var label = (TextBlock)window.FindName("MovieIdTextBlock");
        var hint = (TextBlock)window.FindName("BatchSourceHint");
        try
        {
            foreach (var code in UiLanguageCodes.Supported)
            {
                language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == code);
                foreach (var batch in new[] { false, true })
                {
                    ((RadioButton)window.FindName(batch ? "BatchModeButton" : "SingleModeButton"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    SetLayoutTestWidth(window, 1120);
                    var offset = window.ActualWidth - toolbar.ActualWidth;
                    var widths = new[] { 930d, 1000, 1120, 1320 }
                        .Concat(new[] { 539d, 540, 541, 649, 650, 651, 719, 720, 721 }.Select(width => width + offset))
                        .Where(width => width >= minimumWidth).Distinct().Order().ToArray();
                    foreach (var width in widths)
                    {
                        SetLayoutTestWidth(window, width);
                        var position = host.TranslatePoint(new Point(), layout);
                        var hostWidth = host.ActualWidth;
                        var row = Grid.GetRow(host);
                        foreach (var mode in MetadataSearchSourceModes.Supported)
                        {
                            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == mode);
                            SetLayoutTestWidth(window, width);
                            var context = $"{code}/{(batch ? "batch" : "single")}/{width:0}/{mode} actualWindow={window.ActualWidth:0} toolbar={toolbar.ActualWidth:0}";
                            if (Grid.GetRow(host) != row || Math.Abs(host.ActualWidth - hostWidth) > 0.5 ||
                                (position - host.TranslatePoint(new Point(), layout)).Length > 0.5)
                                throw new InvalidOperationException($"Source change rearranged toolbar: {context}");
                            if (Math.Abs(width - 1120) < 0.5 && row != 0)
                                throw new InvalidOperationException($"Default window must use one search row: {context}");
                            foreach (var control in new FrameworkElement[] { label, id, search, browser, host })
                                if (!FitsInside(control, layout, 0.5))
                                    throw new InvalidOperationException($"Toolbar control clipped: {control.Name}, {context}");
                            var controls = row == 0
                                ? new FrameworkElement[] { label, id, search, browser, host }
                                : [label, id, search];
                            for (var index = 1; index < controls.Length; index++)
                            {
                                var previous = controls[index - 1];
                                var next = controls[index];
                                var right = previous.TranslatePoint(new Point(previous.ActualWidth, previous.ActualHeight / 2), layout);
                                var left = next.TranslatePoint(new Point(0, next.ActualHeight / 2), layout);
                                if (left.X - right.X < 3 || Math.Abs(left.Y - right.Y) > 1)
                                    throw new InvalidOperationException($"Toolbar spacing/alignment failed: {context}");
                            }
                            if (row != 0 && browser.TranslatePoint(new Point(browser.ActualWidth, 0), layout).X + 3 > position.X)
                                throw new InvalidOperationException($"Wrapped source overlaps web lookup: {context}");
                            var displayed = source.Resources["SearchSourceSelectionLabel"]?.ToString() ?? string.Empty;
                            var expectedLabel = toolbar.ActualWidth < 650 && mode == "libredmm"
                                ? "LibreDMM" : ((ComboBoxItem)source.SelectedItem).Content?.ToString();
                            var expectedBrowserLabel = LocalizationService.Get(toolbar.ActualWidth < 650
                                ? "Main.BrowserImportChooseCompact" : "Main.BrowserImportChoose");
                            if (displayed != expectedLabel || browser.Content?.ToString() != expectedBrowserLabel ||
                                string.IsNullOrWhiteSpace(browser.ToolTip?.ToString()))
                                throw new InvalidOperationException($"Compact labels did not refresh on resize/language/source change: {context}");
                            var presenter = FindVisualChildren<ContentPresenter>(source)
                                .FirstOrDefault(item => item.Content?.ToString() == displayed);
                            if (string.IsNullOrEmpty(displayed) || presenter is null || !FitsInside(presenter, source, 0.5))
                                throw new InvalidOperationException($"Source selection text is missing or clipped: {context}");
                            var text = new FormattedText(displayed, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch),
                                source.FontSize, Brushes.White, VisualTreeHelper.GetDpi(source).PixelsPerDip);
                            var available = source.ActualWidth - source.Padding.Left - source.Padding.Right - 2;
                            if (text.WidthIncludingTrailingWhitespace > available + 0.5)
                                throw new InvalidOperationException($"Source text encroaches on arrow/gear: {context}, text={text.WidthIncludingTrailingWhitespace:0.0}, available={available:0.0}");
                            if (mode == "custom")
                            {
                                var arrow = FindVisualChildren<System.Windows.Shapes.Path>(source)
                                    .Single(path => Math.Abs(path.Width - 8) < 0.5 && Math.Abs(path.Height - 5) < 0.5);
                                if (gear.ActualWidth < 24 ||
                                    arrow.TranslatePoint(new Point(), host).X - gear.TranslatePoint(new Point(gear.ActualWidth, 0), host).X < 3)
                                    throw new InvalidOperationException($"Source gear/arrow spacing regressed: {context}");
                            }
                            if (batch && mode != "manual" &&
                                (hint.Visibility != Visibility.Visible || !FitsInside(hint, layout) ||
                                 hint.TranslatePoint(new Point(), layout).Y < host.TranslatePoint(new Point(0, host.ActualHeight), layout).Y + 7))
                                throw new InvalidOperationException($"Batch hint must remain below actions: {context}");
                            if (!batch && hint.Visibility != Visibility.Collapsed)
                                throw new InvalidOperationException($"Single mode shows batch hint: {context}");
                            if (Math.Abs(width - 1120) < 0.5)
                            {
                                source.IsDropDownOpen = true;
                                SetLayoutTestWidth(window, width);
                                var dmm = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "libredmm");
                                if (dmm.Content?.ToString() != LocalizationService.Get("Main.Source.LibreDmmRecommended") ||
                                    Grid.GetRow(host) != row || Math.Abs(host.ActualWidth - hostWidth) > 0.5)
                                    throw new InvalidOperationException($"Opening source menu lost recommendation or changed layout: {context}");
                                source.IsDropDownOpen = false;
                                if (mode is "libredmm" or "custom")
                                    SaveToolbarPreview(toolbar, $"{code}-{(batch ? "batch" : "single")}-{mode}");
                            }
                        }
                    }
                }
            }
            Console.WriteLine("UI PASS toolbarDefaultSingleRowBothModes=True toolbarFourLanguages=True toolbarBreakpointEdges=True sourceMenuFullLabels=True sourceSwitchStable=True gearArrowSpacing=True batchHintSeparate=True");
        }
        finally
        {
            language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == originalLanguage);
            window.Close();
        }
    }

    private static void SaveToolbarPreview(FrameworkElement toolbar, string name)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-preview48");
        Directory.CreateDirectory(directory);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(toolbar), null, new Rect(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(toolbar.ActualWidth), (int)Math.Ceiling(toolbar.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }
}
