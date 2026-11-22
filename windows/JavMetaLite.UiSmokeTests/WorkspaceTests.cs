using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static readonly Lazy<DirectoryInfo> TestSettingsRoot =
        new(() => Directory.CreateTempSubdirectory("JavMetaLite-ui-smoke-"));

    private static AppPreferencesStore TestPreferences() =>
        new(Path.Combine(TestSettingsRoot.Value.FullName, Guid.NewGuid().ToString("N")));

    private static void ClickWorkspaceButton(MainWindow window, string name)
    {
        var control = (FrameworkElement)window.FindName(name);
        control.RaiseEvent(new RoutedEventArgs(control is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));
        if (control is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu menu) menu.IsOpen = false;
        window.UpdateLayout();
    }

    private static Button[] OpenManualWebsites(MainWindow window)
    {
        var source = (ComboBox)window.FindName("SourceComboBox");
        var manual = (ComboBoxItem)window.FindName("ManualSearchItem");
        var choices = (StackPanel)window.FindName("ManualWebsiteChoices");
        var automatic = source.SelectedItem;
        source.IsDropDownOpen = true;
        window.UpdateLayout();
        if (!manual.IsVisible || !manual.IsEnabled)
            throw new InvalidOperationException("Manual search is unavailable in the source dropdown.");

        void Hover(UIElement row) => row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            { RoutedEvent = Mouse.MouseEnterEvent, Source = row });
        Hover(manual);
        WaitForCondition(() => choices.IsVisible, "Hover did not open the website submenu.");
        if (!ReferenceEquals(automatic, source.SelectedItem) || !source.IsDropDownOpen ||
            PresentationSource.FromVisual(manual) != PresentationSource.FromVisual(choices))
            throw new InvalidOperationException("Hover changed the provider or closed the parent menu.");
        Hover((ComboBoxItem)source.Items[0]);
        if (choices.IsVisible) throw new InvalidOperationException("Moving onto another source left the submenu open.");
        Hover(manual);
        WaitForCondition(() => choices.IsVisible, "Website submenu did not reopen.");
        var popup = (System.Windows.Controls.Primitives.Popup)source.Template.FindName("PART_Popup", source);
        var surface = (FrameworkElement)popup.Child;
        surface.UpdateLayout();
        var buttons = choices.Children.OfType<Button>().ToArray();
        foreach (var button in buttons)
        {
            var hit = surface.InputHitTest(button.TranslatePoint(
                new Point(button.ActualWidth / 2, button.ActualHeight / 2), surface)) as DependencyObject;
            if (hit is null || (hit != button && !button.IsAncestorOf(hit)))
                throw new InvalidOperationException($"Website option cannot receive pointer input: {button.Content} ({button.RenderSize} on {surface.RenderSize}, hit {hit?.GetType().Name}).");
        }
        return buttons;
    }

    private static void SaveManualMenuSnapshot(MainWindow window, string path)
    {
        var source = (ComboBox)window.FindName("SourceComboBox");
        var parent = (System.Windows.Controls.Primitives.Popup)source.Template.FindName("PART_Popup", source);
        var choices = (StackPanel)window.FindName("ManualWebsiteChoices");
        var root = (FrameworkElement)window.Content;
        OpenManualWebsites(window);
        try
        {
            var canvas = new Canvas { Width = root.ActualWidth, ClipToBounds = true };
            double bottom = 0;
            foreach (var surface in new[] { root, (FrameworkElement)parent.Child })
            {
                var position = root.PointFromScreen(surface.PointToScreen(new Point()));
                var tile = new System.Windows.Shapes.Rectangle
                {
                    Width = surface.ActualWidth, Height = surface.ActualHeight,
                    Fill = new VisualBrush(surface) { ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(surface.RenderSize) }
                };
                Canvas.SetLeft(tile, position.X);
                Canvas.SetTop(tile, position.Y);
                canvas.Children.Add(tile);
                if (surface != root) bottom = Math.Max(bottom, position.Y + tile.Height);
            }
            canvas.Height = Math.Min(root.ActualHeight, Math.Ceiling(bottom + 12));
            var size = new Size(canvas.Width, canvas.Height);
            canvas.Measure(size);
            canvas.Arrange(new Rect(size));
            SaveReadabilitySnapshot(canvas, path);
        }
        finally
        {
            source.IsDropDownOpen = false;
            WaitForCondition(() => !choices.IsVisible, "Website menu stayed open after capture.");
        }
    }

    private static void SetQueueOpen(MainWindow window, bool open)
    {
        if ((((Border)window.FindName("QueuePanel")).Visibility == Visibility.Visible) != open)
            ClickWorkspaceButton(window, "QueueToggleButton");
    }


    private static int RunWorkspaceChecks(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/JavMetaLite;component/Resources/Theme.xaml", UriKind.Relative)
        });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var root = Directory.CreateTempSubdirectory("JavMetaLite-workspace-");
        var prefs = new AppPreferencesStore(root.FullName);
        var window = new MainWindow(prefs) { Width = 1120, Height = 760 };
        try
        {
            window.LoadPreferences();
            window.Show();
            var language = (ComboBox)window.FindName("LanguageComboBox");
            language.SelectedItem = language.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "zh-Hant"));
            T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            bool Visible(string name) => Find<FrameworkElement>(name).IsVisible;
            void Check(bool ok, string message)
            {
                if (!ok) throw new InvalidOperationException(message);
                Console.WriteLine("PASS " + message);
            }
            void Import(params string[] files)
            {
                foreach (var file in files)
                {
                    if (args.Length > 3 && Path.GetFileName(file) == "ABC-102.mp4") File.Copy(args[3], file);
                    else File.WriteAllBytes(file, [0x00]);
                }
                WaitForTask((Task)typeof(MainWindow).GetMethod("AddMovieFilesAsync", PrivateInstance)!.Invoke(window, [files])!);
                window.UpdateLayout();
            }
            var appearance = Find<ComboBox>("AppearanceChoice");
            void Theme(string value)
            {
                appearance.SelectedItem = appearance.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, value));
                window.UpdateLayout();
            }
            Check(Visible("EmptyDesk") && !Visible("QueuePanel") && !Visible("OutputDrawer"), "Empty workspace");
            Check(!Visible("SingleSavePanel") && !Visible("FileNameText") && !Visible("FilePathText"), "No empty file or save summary");
            foreach (var name in new[] { "QueueToggleButton", "EditReviewButton", "PreferencesButton", "SaveSettingsToggleButton" })
            {
                var b = Find<Button>(name);
                Check(b.Content is System.Windows.Shapes.Path && b.ToolTip != null && b.Focusable &&
                    !string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)), name + " vector and accessible name");
            }
            var manual = Find<ComboBoxItem>("ManualSearchItem");
            Check(!manual.IsVisible && Find<TextBlock>("ManualSearchLabel").Text == "手動搜尋" && !manual.IsEnabled, "Manual search text and empty-ID state");
            var first = Path.Combine(root.FullName, "ARC-101.mp4");
            if (args.Length > 2) File.Copy(args[2], Path.Combine(root.FullName, "ARC-101-poster.png"));
            if (args.Length > 4) File.Copy(args[4], Path.Combine(root.FullName, "ARC-101-fanart.png"));
            Import(first);
            Check(manual.IsEnabled, "Current movie ID enables manual search");
            var sites = OpenManualWebsites(window);
            Check(sites.Select(i => i.Content).SequenceEqual(new[] { "LibreDMM", "R18.dev", "JAVLibrary" }),
                "Source menu offers all three manual import websites");
            Find<ComboBox>("SourceComboBox").IsDropDownOpen = false;
            WaitForCondition(() => !Find<StackPanel>("ManualWebsiteChoices").IsVisible, "Closing sources left the website submenu open.");
            var list = Find<ListBox>("MovieQueueList");
            var fields = FindVisualChildren<TextBox>(Find<Grid>("MetadataFields")).ToArray();
            var properties = fields.Select(f => f.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path).ToHashSet();
            Check(properties.SetEquals(new[] { "Title", "OriginalTitle", "ReleaseDate", "RuntimeMinutes", "Maker", "Director", "Label", "ActorsText", "Rating", "GenresText", "Plot" }) &&
                fields.All(f => f.IsVisible && f.IsReadOnly), "All metadata fields including empty values are visible and locked");
            Check(!Visible("QueuePanel") && Visible("MetadataScrollViewer") &&
                list.Items.Count == 1 && ((MovieJob)list.Items[0]).IsSelectedForBatch, "Single import");
            ClickWorkspaceButton(window, "EditReviewButton");
            Check(fields.All(f => !f.IsReadOnly), "Pencil unlocks the same fields");
            var draft = "潮汐來信";
            Find<TextBox>("TitleEditor").Text = draft;
            ClickWorkspaceButton(window, "FinishEditButton");
            Check(fields.All(f => f.IsReadOnly) && Find<TextBox>("TitleEditor").Text == draft, "Finishing relocks the fields and keeps changes");
            Import(Path.Combine(root.FullName, "ABC-102.mp4"), Path.Combine(root.FullName, "unidentified.mp4"));
            Check(Visible("QueuePanel") && !Find<ContextMenu>("QueueOptions").IsOpen && list.Items.Count == 3 &&
                list.Items.Cast<MovieJob>().All(j => j.IsSelectedForBatch), "Multiple import opens queue with all movies checked");
            void WaitForThumbnails()
            {
                QueueThumbnail Thumbnail(int i) => FindVisualChildren<QueueThumbnail>(
                    (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i)).Single();
                if (args.Length > 2)
                    WaitForCondition(() => Thumbnail(0).Source is not null, "Local poster was not shown in the queue.");
                if (args.Length > 3)
                    WaitForCondition(() => Thumbnail(1).Source is not null, "Valid MP4 did not produce a Windows video thumbnail.");
            }
            WaitForThumbnails();
            var listHeight = list.ActualHeight;
            ClickWorkspaceButton(window, "QueueOptionsButton");
            Check(Find<ContextMenu>("QueueOptions").IsOpen && Math.Abs(list.ActualHeight - listHeight) < 1,
                "Queue commands open without shrinking the movie list");
            ClickWorkspaceButton(window, "SelectRecognizedQueueButton");
            Check(list.Items.Cast<MovieJob>().Count(j => j.IsSelectedForBatch) == 2 &&
                !list.Items.Cast<MovieJob>().Single(j => j.Metadata.Id.Length == 0).IsSelectedForBatch, "Select recognized across queue");
            list.SelectedIndex = 0;
            WaitForCondition(() => ((MovieMetadata)window.DataContext).Title == draft, "First draft did not survive import.");
            var active = window.DataContext;
            var meta = (MovieMetadata)active;
            meta.ActorsText = "林青、陳安"; meta.Maker = "北岸影像"; meta.ReleaseDate = "2026-11-20";
            meta.RuntimeMinutes = "92"; meta.Plot = "沿著海岸線，記錄一座小鎮放映室的日常。";
            meta.OriginalTitle = "Letters from the Tide"; meta.Director = "江澄"; meta.Label = "海岸紀錄";
            meta.Rating = "8.2"; meta.GenresText = "紀錄片、地方文化";
            foreach (var theme in new[] { "light", "dark" })
            {
                Theme(theme);
                var bg = (SolidColorBrush)window.Background;
                Check((bg.Color.R > 180) == (theme == "light"), theme + " palette");
                window.Dispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
                if (args.Length > 1)
                {
                    Directory.CreateDirectory(args[1]);
                    WaitForThumbnails();
                    window.Dispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
                    SaveReadabilitySnapshot((FrameworkElement)window.Content, Path.Combine(args[1], theme + ".png"));
                    if (theme == "dark") SaveManualMenuSnapshot(window, Path.Combine(args[1], "manual-search.png"));
                }
            }
            SetQueueOpen(window, false);
            if (args.Length > 1)
            {
                ClickWorkspaceButton(window, "SaveSettingsToggleButton");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                SaveReadabilitySnapshot((FrameworkElement)window.Content, Path.Combine(args[1], "save-settings.png"));
                ClickWorkspaceButton(window, "CloseOutputButton");
            }
            var shortcut = typeof(MainWindow).GetMethod("ResolveMainShortcut", PrivateInstance)!;
            Check(!Visible("QueuePanel") && ((ColumnDefinition)window.FindName("QueueColumn")).ActualWidth == 0 &&
                ReferenceEquals(active, window.DataContext) && list.Items.Cast<MovieJob>().Count(j => j.IsSelectedForBatch) == 2 &&
                ReferenceEquals(shortcut.Invoke(window, [Key.S, ModifierKeys.Control]), Find<Button>("SaveSelectedButton")) &&
                Find<Button>("SaveSelectedButton").Content.ToString()!.Contains('2'), "Hidden queue preserves scope, draft and Ctrl+S");
            foreach (var width in new[] { 930, 1380 })
            {
                SetLayoutTestWidth(window, width);
                ClickWorkspaceButton(window, "SaveSettingsToggleButton");
                var drawer = Find<Border>("OutputDrawer");
                Check(drawer.IsVisible && drawer.ActualWidth >= 250 && Visible("CloseOutputButton") &&
                    FitsInside(drawer, (FrameworkElement)drawer.Parent, 1), "Output settings fit at " + width);
                ClickWorkspaceButton(window, "CloseOutputButton");
            }
            ClickWorkspaceButton(window, "SaveSettingsToggleButton");
            Find<CheckBox>("WriteNfoCheckBox").IsChecked = false;
            var jobs = list.Items.Cast<MovieJob>().ToArray();
            Check(!jobs[0].SaveConfiguration!.SaveOptions.WriteNfo && jobs.Skip(1).All(j => j.SaveConfiguration!.SaveOptions.WriteNfo),
                "Current movie settings stay local");
            ClickWorkspaceButton(window, "ApplySelectionSettingsButton");
            Check(jobs.All(j => j.SaveConfiguration!.SaveOptions.WriteNfo != j.IsSelectedForBatch), "Apply settings only to checked movies");
            ClickWorkspaceButton(window, "CloseOutputButton");
            Import(Path.Combine(root.FullName, "ABC-103.mp4"));
            Check(!Visible("QueuePanel"), "Explicit queue choice survives import");
            Theme("system");
            window.Close();
            var reopen = new MainWindow(prefs);
            reopen.LoadPreferences();
            try
            {
                Check(WindowVisualTheme.Appearance == "system" && prefs.Load().Preferences.QueueVisible == false, "Saved appearance and queue choice");
            }
            finally { reopen.Close(); }
            TestUnifiedIntake(root.FullName, args.Length > 1 ? args[1] : null);
            return 0;
        }
        finally
        {
            if (window.IsVisible) window.Close();
            app.Shutdown();
            root.Delete(true);
        }
    }
}
