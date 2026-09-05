using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestReadabilityCloseout()
    {
        var originalLanguage = LocalizationService.CurrentLanguageCode;
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "final-fix56", "readability");
        Directory.CreateDirectory(directory);
        var window = new MainWindow(new AppPreferencesStore(Path.Combine(directory, "unused-settings.json")));
        window.Show();
        var language = (ComboBox)window.FindName("LanguageComboBox");
        try
        {
            foreach (var code in new[] { "zh-Hans", "zh-Hant", "en", "ja" })
            {
                language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == code);
                foreach (var batch in new[] { false, true })
                {
                    ((RadioButton)window.FindName(batch ? "BatchModeButton" : "SingleModeButton"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    foreach (var width in new[] { 930d, 1120d })
                    {
                        window.Width = width;
                        var source = (Button)window.FindName("ArtworkSourceButton");
                        source.Content = "LibreDMM ▾";
                        source.Visibility = Visibility.Visible;
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();
                        var context = $"{code}/{(batch ? "batch" : "single")}/{width}";
                        var header = (Grid)window.FindName("ArtworkSourceHeader");
                        var heading = (TextBlock)window.FindName("ArtworkPreviewHeading");
                        var actions = (StackPanel)source.Parent;
                        var headingBottom = heading.TranslatePoint(new Point(0, heading.ActualHeight), header).Y;
                        if (actions.TranslatePoint(new Point(), header).Y < headingBottom + 3 ||
                            !FitsInside(actions, header, 0.5))
                            throw new InvalidOperationException($"Artwork header actions overlap or clip: {context}");
                        AssertReadableText(heading, header, context);
                        if (batch)
                        {
                            var remove = (Button)window.FindName("RemoveSelectedQueueButton");
                            var text = FindVisualChildren<TextBlock>(remove).Single();
                            AssertReadableText(text, remove, context);
                        }
                        var target = (ComboBox)window.FindName("TargetModeComboBox");
                        foreach (var item in target.Items.OfType<ComboBoxItem>())
                        {
                            target.SelectedItem = item;
                            window.UpdateLayout();
                            var text = FindVisualChildren<TextBlock>(target)
                                .FirstOrDefault(candidate => candidate.IsVisible && candidate.Text == item.Content?.ToString())
                                ?? throw new InvalidOperationException($"Selected target text missing: {context}");
                            AssertReadableText(text, target, context);
                            if (!FitsInside(target, (FrameworkElement)target.Parent, 0.5))
                                throw new InvalidOperationException($"Target selector outside layout: {context}");
                        }
                        if (width == 1120)
                            SaveReadabilitySnapshot((FrameworkElement)window.Content,
                                Path.Combine(directory, $"{code}-{(batch ? "batch" : "single")}.png"));
                    }
                }
            }
            Console.WriteLine("UI PASS fourLanguageArtworkHeaderReadable=True queueRemovalTextReadable=True targetLabelsReadable=True defaultAndMinimumWidth=True");
        }
        finally
        {
            language.SelectedItem = language.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == originalLanguage);
            window.Close();
        }
    }

    private static void AssertReadableText(TextBlock text, FrameworkElement parent, string context)
    {
        var measured = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        if (text.TextWrapping != TextWrapping.NoWrap)
            measured.MaxTextWidth = Math.Max(1, text.ActualWidth);
        if (!FitsInside(text, parent, 0.5) || measured.Width > text.ActualWidth + 1 ||
            measured.Height > text.ActualHeight + 1)
            throw new InvalidOperationException($"Text clipped: {context}, '{text.Text}' actual={text.ActualWidth}x{text.ActualHeight}, needed={measured.Width}x{measured.Height}");
    }

    private static void SaveReadabilitySnapshot(FrameworkElement element, string path)
    {
        var drawing = new DrawingVisual();
        var area = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element)
                { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = area, Stretch = Stretch.Fill }, null, area);
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        image.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
