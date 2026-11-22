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
        var window = new MainWindow(TestPreferences()) { MaxWidth = hostMaximumWidth };
        var minimumWidth = window.MinWidth;
        window.Show();
        var language = (ComboBox)window.FindName("LanguageComboBox");

        var toolbar = (Border)window.FindName("SearchToolbar");
        var layout = (Grid)window.FindName("SearchToolbarLayout");
        var source = (ComboBox)window.FindName("SourceComboBox");
        var host = (Grid)window.FindName("SourceSelectorHost");
        var gear = (Button)window.FindName("SourceProfileButton");
        var output = (Button)window.FindName("SaveSettingsToggleButton");
        var batchSearch = (Button)window.FindName("SearchQueueButton");
        var manual = (ComboBoxItem)window.FindName("ManualSearchItem");
        var search = (Button)window.FindName("SearchButton");
        var id = (TextBox)window.FindName("IdTextBox");
        var label = (TextBlock)window.FindName("MovieIdTextBlock");
        var sourceLabel = (TextBlock)window.FindName("AutomaticSourceText");
        try
        {
            foreach (var code in UiLanguageCodes.Supported)
            {
                language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == code);
                foreach (var batch in new[] { false, true })
                {
                    SetQueueOpen(window, batch);
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    SetLayoutTestWidth(window, 1120);
                    var offset = window.ActualWidth - layout.ActualWidth;
                    var rowWidth = host.TranslatePoint(new Point(host.ActualWidth, 0), layout).X;
                    var widths = new[] { 930d, 1000, 1120, 1320 }
                        .Concat(new[] { rowWidth - 1, rowWidth, rowWidth + 1 }.Select(width => width + offset))
                        .Where(width => width >= minimumWidth).Distinct().Order().ToArray();
                    foreach (var width in widths)
                    {
                        SetLayoutTestWidth(window, width);
                        var position = host.TranslatePoint(new Point(), layout);
                        var hostWidth = host.ActualWidth;
                        var row = Grid.GetRow(host);
                        foreach (var mode in new[] { "libredmm", "r18dev", "custom" })
                        {
                            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == mode);
                            SetLayoutTestWidth(window, width);
                            var context = $"{code}/{(batch ? "batch" : "single")}/{width:0}/{mode} actualWindow={window.ActualWidth:0} toolbar={toolbar.ActualWidth:0}";
                            if (Grid.GetRow(host) != row || Math.Abs(host.ActualWidth - hostWidth) > 0.5 ||
                                (position - host.TranslatePoint(new Point(), layout)).Length > 0.5)
                                throw new InvalidOperationException($"Source change rearranged toolbar: {context}, row={row}/{Grid.GetRow(host)}, width={hostWidth}/{host.ActualWidth}, position={position}/{host.TranslatePoint(new Point(), layout)}.");
                            if (Math.Abs(width - 1120) < 0.5 && row != 0)
                                throw new InvalidOperationException($"Default window must use one search row: {context}");
                            foreach (var control in new FrameworkElement[] { label, id, search, batchSearch, host })
                                if (!FitsInside(control, layout, 0.5))
                                    throw new InvalidOperationException($"Toolbar control clipped: {control.Name}, {context}");
                            var batchEdge = batchSearch.TranslatePoint(new Point(batchSearch.ActualWidth, 0), toolbar).X;
                            var outputOrigin = output.TranslatePoint(new Point(), toolbar);
                            if (Math.Abs(toolbar.ActualWidth - toolbar.Padding.Right - outputOrigin.X - output.ActualWidth) > 1 ||
                                outputOrigin.X < batchEdge || !FitsInside(output, toolbar) ||
                                new Control[] { id, search, source, batchSearch }.Any(control => Math.Abs(control.ActualHeight - 36) > 0.5))
                                throw new InvalidOperationException($"Toolbar height or right pinning regressed: {context}.");
                            var controls = row == 0
                                ? new FrameworkElement[] { label, id, search, sourceLabel, host }
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
                            var gap = batchSearch.TranslatePoint(new Point(), layout).X -
                                source.TranslatePoint(new Point(source.ActualWidth, 0), layout).X;
                            if (gap < 3 || gap > 12 || !batchSearch.IsVisible || !batchSearch.Focusable)
                                throw new InvalidOperationException($"Batch search is not beside the source: {context}");
                            var displayed = source.Resources["SearchSourceSelectionLabel"]?.ToString() ?? string.Empty;
                            var expectedLabel = mode == "libredmm" ? "LibreDMM" : ((ComboBoxItem)source.SelectedItem).Content?.ToString();
                            if (displayed != expectedLabel || ((TextBlock)window.FindName("ManualSearchLabel")).Text != LocalizationService.Get("Desk.WebLookup") ||
                                System.Windows.Automation.AutomationProperties.GetName(manual) != LocalizationService.Get("Desk.WebLookup") ||
                                string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetHelpText(manual)))
                                throw new InvalidOperationException($"Search controls lost their label: {context}");
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
                            if (Math.Abs(width - 1120) < 0.5)
                            {
                                source.IsDropDownOpen = true;
                                SetLayoutTestWidth(window, width);
                                var dmm = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "libredmm");
                                if (!manual.IsVisible || dmm.Content?.ToString() != LocalizationService.Get("Main.Source.LibreDmmRecommended") ||
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
            Console.WriteLine("UI PASS toolbarDefaultSingleRowBothModes=True toolbarFourLanguages=True toolbarBreakpointEdges=True sourceMenuFullLabels=True sourceSwitchStable=True gearArrowSpacing=True manualLookupInSourceMenu=True batchSearchBesideSource=True");
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
