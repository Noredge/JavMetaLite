using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JavMetaLite.App;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static async Task TestDuplicateMoviesAsync()
    {
        foreach (var mode in new[] { "cancel", "skip", "preview-cancel", "one", "parts" })
        {
            var root = Path.Combine(Path.GetTempPath(), "JavMetaLite-duplicates-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var window = new MainWindow(new AppPreferencesStore(Path.Combine(root, "settings")))
                { ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            try
            {
                ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var options = new SaveOptions(true, false, false, false, false, false);
                var organization = new OrganizationOptions(OrganizationTargetMode.CustomRootNumberFolder, true, target);
                MovieJob Add(string suffix)
                {
                    var path = Path.Combine(root, "RBD-487" + suffix + ".mp4");
                    File.WriteAllText(path, suffix);
                    var metadata = new MovieMetadata { Id = "RBD-487", Title = "Synthetic " + suffix };
                    var job = FinalReviewAddJob(window, "RBD-487", path, metadata);
                    job.InitializeSaveConfiguration(new(options, organization));
                    job.IsSelectedForBatch = true;
                    return job;
                }
                var a = Add("a"); var b = Add("b");
                var sourceA = a.VideoPath!; var sourceB = b.VideoPath!;
                await FinalReviewActivate(window, a, false);
                ((CheckBox)window.FindName("IncludeIdInTitleCheckBox")).IsChecked = false;
                ((CheckBox)window.FindName("SkipSavePreviewCheckBox")).IsChecked = true;
                BatchSavePreviewItem Item(MovieJob job) => new(job, FileOrganizationService.BuildPlan(job.VideoPaths,
                    job.Metadata, options, organization, job.CreateLocalSaveContext()), job.ReviewRevision);
                var originals = new[] { Item(a), Item(b) };
                CoverAssert(DuplicateMovieResolution.CanCombine(originals), "Valid a/b parts should be confirmable.");
                b.UpdateSaveConfiguration(new(options with { IncludeIdInTitle = true }, organization));
                CoverAssert(!DuplicateMovieResolution.CanCombine(originals), "Different settings must prevent combination.");
                b.UpdateSaveConfiguration(new(options, organization));
                var fakeRetirement = originals[0] with { Plan = originals[0].Plan with { SourcePathsToRetire = [sourceB] } };
                CoverAssert(BatchSavePathConflicts.Find([fakeRetirement], [originals[1]]).ContainsKey(a),
                    "Saving one must not retire the skipped source.");

                _ = window.Dispatcher.BeginInvoke(() => ((Button)window.FindName("SaveSelectedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                await UntilStableAsync(() => Application.Current.Windows.OfType<DuplicateMovieWindow>().Any(), "Missing duplicate chooser.");
                var chooser = Application.Current.Windows.OfType<DuplicateMovieWindow>().Single();
                var candidates = (ListBox)chooser.FindName("CandidatesList");
                CoverAssert(candidates.Background == chooser.FindResource("InputBrush") &&
                    candidates.Foreground == chooser.FindResource("TextBrush"), "Duplicate list must use dark background and readable text.");
                CoverAssert(!((Button)chooser.FindName("SaveOneButton")).IsEnabled && !((Button)chooser.FindName("SavePartsButton")).IsEnabled,
                    "Chooser must not default to a winner or parts.");
                if (mode == "cancel")
                {
                    candidates.SelectedIndex = 0;
                    chooser.UpdateLayout();
                    var row = (ListBoxItem)candidates.ItemContainerGenerator.ContainerFromIndex(0);
                    row.ApplyTemplate();
                    var border = (Border)row.Template.FindName("RowBorder", row);
                    CoverAssert(((SolidColorBrush)border.Background).Color == Color.FromRgb(32, 61, 104),
                        "Selected row must also stay dark.");
                    var bitmap = new RenderTargetBitmap((int)chooser.ActualWidth, (int)chooser.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(chooser);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var screenshot = Path.Combine(root, "duplicate-dialog.png");
                    using (var stream = File.Create(screenshot)) encoder.Save(stream);
                    Console.WriteLine("Duplicate dialog screenshot: " + screenshot);
                }
                if (mode == "cancel") chooser.DialogResult = false;
                else if (mode == "skip") FinalReviewInvoke(chooser, "Skip_Click", chooser, new RoutedEventArgs());
                else
                {
                    var list = (ListBox)chooser.FindName("CandidatesList");
                    list.SelectedIndex = 1;
                    if (mode == "parts")
                    {
                        FinalReviewInvoke(chooser, "Up_Click", chooser, new RoutedEventArgs());
                        ((CheckBox)chooser.FindName("ConfirmPartsCheckBox")).IsChecked = true;
                        CoverAssert(((Button)chooser.FindName("SavePartsButton")).IsEnabled, "Parts confirmation stayed disabled for matching settings.");
                        FinalReviewInvoke(chooser, "SaveParts_Click", chooser, new RoutedEventArgs());
                    }
                    else FinalReviewInvoke(chooser, "SaveOne_Click", chooser, new RoutedEventArgs());
                    await UntilStableAsync(() => Application.Current.Windows.OfType<BatchSavePreviewWindow>().Any(), "Resolution must force final preview.");
                    var preview = Application.Current.Windows.OfType<BatchSavePreviewWindow>().Single();
                    CoverAssert(preview.Items.Count == 1 && preview.Issues.Count == 0, "Resolved group must produce one safe plan.");
                    if (mode == "parts")
                        CoverAssert(preview.Items[0].Plan.VideoTransfers.Count == 2 && preview.Items[0].Plan.VideoTransfers[0].SourcePath == sourceB,
                            "Confirmed order lost before save.");
                    preview.DialogResult = mode != "preview-cancel";
                }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await UntilStableAsync(() => !IsWindowBusy(window), "Duplicate save did not finish.");
                var queue = (IList)FinalReviewField(window, "_movieQueue");
                if (mode is "cancel" or "skip" or "preview-cancel")
                {
                    CoverAssert(queue.Count == 2 && File.ReadAllText(sourceA) == "a" && File.ReadAllText(sourceB) == "b" &&
                        !Directory.EnumerateFileSystemEntries(target).Any(), "Cancel/skip/preview changed files or queue.");
                }
                else if (mode == "one")
                {
                    CoverAssert(queue.Count == 1 && queue.Contains(a) && File.ReadAllText(sourceA) == "a" && !File.Exists(sourceB),
                        "Saving one must leave the skipped source and queue entry intact.");
                    CoverAssert(File.ReadAllText(Path.Combine(target, "RBD-487", "RBD-487.mp4")) == "b", "Wrong winner saved.");
                }
                else
                {
                    CoverAssert(queue.Count == 0 && !File.Exists(sourceA) && !File.Exists(sourceB), "Parts participants were not completed together.");
                    CoverAssert(File.ReadAllText(Path.Combine(target, "RBD-487", "RBD-487-cd1.mp4")) == "b" &&
                        File.ReadAllText(Path.Combine(target, "RBD-487", "RBD-487-cd2.mp4")) == "a", "Saved part order incorrect.");
                }
            }
            finally
            {
                foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog.Owner == window).ToArray()) dialog.Close();
                window.Close();
            }
        }
        Console.WriteLine("UI PASS duplicateCancel=True duplicateSkip=True duplicateOne=True confirmedPartsOrder=True forcedPreview=True skippedSourceProtected=True");
    }
}
