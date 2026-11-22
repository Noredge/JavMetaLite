using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestUnifiedIntake(string testDirectory, string? screenshots)
    {
        var root = Directory.CreateDirectory(Path.Combine(testDirectory, "待整理")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(root, "海岸影集")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(folder, "北岸")).FullName;
        var loose = Path.Combine(root, "ARC-107.mkv");
        var other = Path.Combine(folder, "ARC-105.mp4");
        var deep = Path.Combine(nested, "ARC-104.mp4");
        foreach (var file in new[] { loose, other, deep }) File.WriteAllBytes(file, [1, 2, 3]);
        File.WriteAllText(Path.Combine(root, "片单.txt"), "fixture");
        var window = new MainWindow(TestPreferences());
        using var handler = new RoutingHandler();
        using var http = new HttpClient(handler);
        ReplaceStabilityClient(window, "_libreDmmClient", new LibreDmmClient(http));
        typeof(MainWindow).GetField("_lastInputFolder", PrivateInstance)!.SetValue(window, root);
        var dragSource = new Window { Title = "Drag source", Width = 240, Height = 180 };
        window.Show();
        try
        {
            void PickMixedInputs() => InteractWithModal<MovieInputPicker>(window, () => ClickWorkspaceButton(window, "ChooseVideoButton"), picker =>
            {
                var list = (ListBox)picker.FindName("EntriesList");
                WaitForCondition(() => list.Items.Count == 2, "Mixed picker did not show both folder and video.");
                void Pick(string path)
                {
                    var row = list.Items.Cast<MovieInputPicker.Entry>().Single(e => e.FullPath == path);
                    list.ScrollIntoView(row);
                    list.UpdateLayout();
                    var box = FindVisualChildren<CheckBox>((ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row)).Single();
                    box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                    if (!row.IsPicked) throw new InvalidOperationException("Checkbox did not select " + row.Name);
                }
                var selectAll = (CheckBox)picker.FindName("PickPageBox");
                selectAll.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                if (!list.Items.Cast<MovieInputPicker.Entry>().All(e => e.IsPicked)) throw new InvalidOperationException("Select all missed visible items.");
                selectAll.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                if (list.Items.Cast<MovieInputPicker.Entry>().Any(e => e.IsPicked)) throw new InvalidOperationException("Clear selection left checked items.");
                Pick(folder);
                Pick(loose);
                if (screenshots != null)
                {
                    picker.UpdateLayout();
                    picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    SaveReadabilitySnapshot((FrameworkElement)picker.Content, Path.Combine(screenshots, "mixed-picker.png"));
                }
                WaitForTask(picker.NavigateAsync(folder));
                Pick(other);
                ((Button)picker.FindName("SelectionViewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (list.Items.Count != 3) throw new InvalidOperationException("Cross-folder selection was lost.");
                ((Button)picker.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            var queue = (ListBox)window.FindName("MovieQueueList");
            InteractWithModal<MovieDiscoveryWindow>(window, PickMixedInputs, review =>
            {
                if (queue.Items.Count != 0 || review.Items.Count != 3 || !review.Items.All(item => item.IsSelected))
                    throw new InvalidOperationException("Mixed picker must review each discovered movie before adding it.");
                ((Button)review.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            WaitForCondition(() => queue.Items.Count == 3 && !IsWindowBusy(window), "Mixed selection was not imported.");
            SetQueueOpen(window, false);
            var newVideo = Path.Combine(root, "ARC-108.mkv");
            var part1 = Path.Combine(nested, "ARC-109-CD1.mp4");
            var part2 = Path.Combine(nested, "ARC-109-CD2.mp4");
            var unknown = Path.Combine(nested, "holiday.mp4");
            foreach (var file in new[] { newVideo, part1, part2, unknown }) File.WriteAllBytes(file, [1, 2, 3]);
            var target = (TextBox)window.FindName("IdTextBox");
            var data = new DataObject(DataFormats.FileDrop, new[] { newVideo, folder, loose, Path.Combine(root, "片单.txt") });
            DragEventArgs RaiseDropEvent(RoutedEvent routedEvent)
            {
                var drag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                    [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(1, 1)], null)!;
                drag.RoutedEvent = routedEvent;
                target.RaiseEvent(drag);
                return drag;
            }
            var overlay = (Grid)window.FindName("DropOverlay");
            dragSource.Show();
            dragSource.Activate();
            if (!dragSource.IsActive) throw new InvalidOperationException("Drag source must start active.");
            if (RaiseDropEvent(DragDrop.PreviewDragOverEvent).Effects != DragDropEffects.Copy || !overlay.IsVisible)
                throw new InvalidOperationException("Dragging movies over the ID field did not show the import target.");
            if (!dragSource.IsActive) throw new InvalidOperationException("Hovering over the target activated it too early.");
            InteractWithModal<MovieDiscoveryWindow>(window, () => RaiseDropEvent(DragDrop.PreviewDropEvent), review =>
            {
                if (!review.IsActive) throw new InvalidOperationException("Import review opened behind the drag source.");
                if (queue.Items.Count != 3 || review.Items.Count != 6)
                    throw new InvalidOperationException("Mixed drop changed the queue before confirmation.");
                ((Button)review.FindName("SelectRecognizedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var add = (Button)review.FindName("AddButton");
                var selected = review.Items.Where(item => item.IsSelected).ToArray();
                if (review.Items.Count(item => !item.CanSelect) != 3 ||
                    !selected.Select(item => item.DisplayName).ToHashSet().SetEquals(["ARC-108", "ARC-109"]) ||
                    !Equals(add.Content, LocalizationService.Get("Discovery.AddFound", 2)))
                    throw new InvalidOperationException("Recognized-only selection included duplicates or unrecognized files.");
                if (screenshots != null)
                {
                    review.UpdateLayout();
                    SaveReadabilitySnapshot(review, Path.Combine(screenshots, "import-review.png"));
                }
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            WaitForCondition(() => queue.Items.Count == 5 && !IsWindowBusy(window), "Recognized movies were not imported.");
            if (!window.IsActive) throw new InvalidOperationException("Main window remained behind the source after batch confirmation.");
            var importedPaths = queue.Items.Cast<MovieJob>().SelectMany(job => job.VideoPaths).ToHashSet();
            if (!importedPaths.SetEquals([loose, other, deep, newVideo, part1, part2]))
                throw new InvalidOperationException("Import ignored the review selection or split a multipart movie.");
            if (!((Border)window.FindName("QueuePanel")).IsVisible) throw new InvalidOperationException("Dropping multiple movies did not reopen the hidden queue.");
            if (overlay.IsVisible || handler.Calls != 0) throw new InvalidOperationException("Drop state or offline import is incorrect.");

            var looseUnknown = Path.Combine(root, "untitled.mp4");
            File.WriteAllBytes(looseUnknown, [1, 2, 3]);
            dragSource.Activate();
            data = new DataObject(DataFormats.FileDrop, new[] { looseUnknown, unknown });
            InteractWithModal<MovieDiscoveryWindow>(window, () => RaiseDropEvent(DragDrop.PreviewDropEvent), review =>
            {
                if (((Button)review.FindName("SelectRecognizedButton")).IsEnabled ||
                    review.Items.Count != 2 || review.Items.Any(item => !item.CanSelect))
                    throw new InvalidOperationException("Unrecognized files must remain available for manual selection.");
                review.DialogResult = false;
            });
            if (!window.IsActive) throw new InvalidOperationException("Canceling batch import left the source in front.");
            if (queue.Items.Count != 5) throw new InvalidOperationException("Canceling a file-only batch changed the queue.");

            // The shell can still have activation while asynchronous discovery finishes.
            dragSource.Activate();
            InteractWithModal<MovieDiscoveryWindow>(window, () => _ = window.ImportInputsAsync([looseUnknown, unknown]), review =>
            {
                if (!review.IsActive) throw new InvalidOperationException("The review must be active before dismissal.");
                review.DialogResult = false;
            });
            if (!window.IsActive) throw new InvalidOperationException("Dismissing the active review returned to the previous window.");

            dragSource.Activate();
            data = new DataObject(DataFormats.FileDrop, new[] { looseUnknown });
            RaiseDropEvent(DragDrop.PreviewDropEvent);
            if (!window.IsActive) throw new InvalidOperationException("A single movie drop did not activate the receiving window.");
            dragSource.Activate();
            WaitForCondition(() => !IsWindowBusy(window) && queue.Items.Count == 6, "Single drop did not finish.");
            if (!dragSource.IsActive || window.Topmost) throw new InvalidOperationException("Completing import overrode the user's window switch.");
            window.Activate();

            queue.SelectedItem = queue.Items.Cast<MovieJob>().Single(j => j.VideoPath == loose);
            WaitForCondition(() => !IsWindowBusy(window) && ((MovieMetadata)window.DataContext).Id == "ARC-107", "Active movie did not switch.");
            foreach (var job in queue.Items.Cast<MovieJob>()) job.IsSelectedForBatch = job.VideoPath == loose;
            WaitForCondition(() => ((Button)window.FindName("SearchQueueButton")).Content.ToString()!.Contains('1'), "Incorrect query scope.");
            SetQueueOpen(window, false);
            ClickWorkspaceButton(window, "SearchQueueButton");
            WaitForCondition(() => !IsWindowBusy(window) && ((MovieMetadata)window.DataContext).Title == "Libre fixture", "Checked movie did not receive search results.");
            var batchButton = (Button)window.FindName("SearchQueueButton");
            WaitForCondition(() => batchButton.IsEnabled &&
                Equals(batchButton.Content, LocalizationService.Get("Desk.SearchCheckedCount", 1)),
                "A checked movie must remain searchable after a successful lookup.");
            ClickWorkspaceButton(window, "SearchQueueButton");
            WaitForCondition(() => !IsWindowBusy(window) && handler.Calls == 2,
                "Repeating the checked lookup on the same source did not issue a fresh request.");
            ClickWorkspaceButton(window, "EditReviewButton");
            ((TextBox)window.FindName("TitleEditor")).Text = "海岸样片修订";
            ClickWorkspaceButton(window, "FinishEditButton");
            ((CheckBox)window.FindName("DownloadPosterCheckBox")).IsChecked = false;
            ((CheckBox)window.FindName("DownloadFanartCheckBox")).IsChecked = false;
            InteractWithModal<BatchSavePreviewWindow>(window, () => ClickWorkspaceButton(window, "SaveSelectedButton"), preview =>
            {
                if (preview.Items.Count != 1 || preview.Items[0].Job.VideoPath != loose)
                    throw new InvalidOperationException("Save preview includes unchecked movies.");
                ((Button)preview.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            WaitForCondition(() => !IsWindowBusy(window) && Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories).Any(),
                "Confirmed save did not produce NFO.");
            var nfo = Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories).Single();
            if (handler.Calls != 2 || !XDocument.Load(nfo).Descendants("title").Any(t => t.Value.Contains("海岸样片修订")) ||
                !File.ReadAllBytes(loose).SequenceEqual(new byte[] { 1, 2, 3 }))
                throw new InvalidOperationException("Saved metadata or movie content is incorrect.");
            Console.WriteLine("UI PASS dropForeground=True reviewConfirmFocus=True reviewCancelFocus=True switchAwayDuringImport=True");
            Console.WriteLine("UI PASS actualMixedPicker=True nestedFolders=True crossFolderChecks=True duplicateRoots=True routedMixedDrop=True recognizedOnlyBeforeImport=True fileOnlyBatchCancel=True multipartPreserved=True nonVideoIgnored=True offlineImport=True checkedQuery=True editedNfoOnDisk=True uncheckedSkipped=True");
        }
        finally { dragSource.Close(); window.Close(); }
    }

    private static void InteractWithModal<T>(MainWindow owner, Action open, Action<T> interact) where T : Window
    {
        Exception? error = null;
        var handled = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            var modal = Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.Owner == owner && w.IsVisible);
            if (modal == null) return;
            timer.Stop();
            try { interact(modal); handled = true; }
            catch (Exception ex) { error = ex; }
            finally { if (modal.IsVisible) modal.Close(); }
        };
        timer.Start();
        try
        {
            open();
            WaitForCondition(() => handled || error != null, "Expected modal was not shown: " + typeof(T).Name);
            if (error != null) throw new InvalidOperationException("Modal interaction failed.", error);
        }
        finally { timer.Stop(); }
    }
}
