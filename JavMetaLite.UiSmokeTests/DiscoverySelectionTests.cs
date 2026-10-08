using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestDiscoveryRecognizedSelection()
    {
        var scanDialog = MainWindow.CreateScanFolderDialog();
        if (!scanDialog.Multiselect || scanDialog.Title != LocalizationService.Get("Dialog.ChooseScanRoot"))
            throw new InvalidOperationException("Scan folder picker must enable multiple selection and use localized instructions.");
        var roots = new[] { @"C:\Synthetic\One", @"C:\Synthetic\Two" };
        var multiRootWindow = new MovieDiscoveryWindow(roots, [], includeSubdirectories: true);
        if (((TextBlock)multiRootWindow.FindName("RootPathText")).Text != string.Join(Environment.NewLine, roots) ||
            ((CheckBox)multiRootWindow.FindName("IncludeSubdirectoriesCheckBox")).IsChecked != true)
            throw new InvalidOperationException("All chosen folder roots must be displayed in one recursive discovery window.");
        multiRootWindow.Close();
        var window = new MovieDiscoveryWindow(@"C:\Synthetic", []);
        var recognized = new MovieDiscoveryItem(MovieFileSet.Create([@"C:\Synthetic\MIDE-720 OL.mp4"]), false);
        var unknown = new MovieDiscoveryItem(MovieFileSet.Create([@"C:\Synthetic\holiday.mp4"]), false);
        var numeric = new MovieDiscoveryItem(MovieFileSet.Create([@"C:\Synthetic\042619-903.mp4"]), false);
        var duplicate = new MovieDiscoveryItem(MovieFileSet.Create([@"C:\Synthetic\IPX-123.mp4"]), true);
        var multipart = new MovieDiscoveryItem(MovieFileSet.Create([
            @"C:\Synthetic\ABF-193-CD1.mp4", @"C:\Synthetic\ABF-193-CD2.mp4"]), false);
        var fc2 = new MovieDiscoveryItem(MovieFileSet.Create([@"C:\Synthetic\FC2-PPV-1234567.mp4"]), false);
        foreach (var item in new[] { recognized, unknown, numeric, duplicate, multipart, fc2 })
            window.Items.Add(item);

        void Click(string name) => typeof(MovieDiscoveryWindow).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);

        Click("SelectRecognized_Click");
        if (!recognized.IsSelected || !multipart.IsSelected || !fc2.IsSelected ||
            unknown.IsSelected || numeric.IsSelected || duplicate.IsSelected)
            throw new InvalidOperationException("Recognized-only selection must replace the existing selection and exclude duplicates.");
        Click("SelectNone_Click");
        Click("SelectRecognized_Click");
        if (!recognized.IsSelected || !multipart.IsSelected || !fc2.IsSelected)
            throw new InvalidOperationException("Recognized selection must also work after clearing selection.");
        unknown.IsSelected = true;
        if (!unknown.IsSelected) throw new InvalidOperationException("Manual selection must remain available.");
        Click("SelectAll_Click");
        if (!numeric.IsSelected || duplicate.IsSelected)
            throw new InvalidOperationException("Select all must preserve existing behavior.");
        window.Items.Clear();
        window.Items.Add(unknown);
        Click("SelectNone_Click");
        if (((Button)window.FindName("SelectRecognizedButton")).IsEnabled)
            throw new InvalidOperationException("Recognized selection must be disabled when no recognized items exist.");
        var denied = MovieFileDiscoveryDiagnostic.FromException(@"C:\Synthetic\protected",
            new UnauthorizedAccessException("Synthetic permission denial"));
        var ioError = MovieFileDiscoveryDiagnostic.FromException(@"C:\Synthetic\unreadable",
            new System.IO.IOException("Synthetic I/O failure"));
        if (denied.Kind != MovieFileDiscoveryDiagnosticKind.AccessDenied ||
            ioError.Kind != MovieFileDiscoveryDiagnosticKind.ReadFailed)
            throw new InvalidOperationException("Permission errors must be distinct from other read failures.");
        foreach (var diagnostics in new[] { new[] { denied }, new[] { ioError }, new[] { denied, ioError },
            Array.Empty<MovieFileDiscoveryDiagnostic>() })
        {
            var result = new MovieInputDiscoveryResult([@"C:\Synthetic"], true, [], 0, 0, diagnostics);
            typeof(MovieDiscoveryWindow).GetMethod("ApplyDiscovery", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [result]);
            var text = ((TextBlock)window.FindName("DiagnosticsText")).Text;
            if ((((Button)window.FindName("DiagnosticsDetailsButton")).Visibility == Visibility.Visible) !=
                (diagnostics.Length > 0))
                throw new InvalidOperationException("Details must be available only for the current scan's errors.");
            if (text.Contains(LocalizationService.Get("Discovery.AccessDeniedSummary", 1)) != diagnostics.Contains(denied) ||
                text.Contains(LocalizationService.Get("Discovery.Diagnostics", 1)) != diagnostics.Contains(ioError) ||
                (diagnostics.Length == 0 && text.Length != 0))
                throw new InvalidOperationException("Discovery must summarize permission and other failures separately.");
        }
        var details = string.Join(Environment.NewLine, Enumerable.Range(0, 100).Select(n =>
            $@"C:\Synthetic\{new string('x', 180)}\folder-{n}: Permission denied"));
        var detailWindow = new DiscoveryDetailsWindow(details) { Width = 480, Height = 280 };
        detailWindow.Show();
        detailWindow.UpdateLayout();
        var detailText = (TextBox)detailWindow.FindName("DetailsText");
        detailText.SelectAll();
        if (!detailText.IsReadOnly || detailText.Text != details || detailText.SelectedText != details ||
            detailText.VerticalScrollBarVisibility != ScrollBarVisibility.Auto ||
            detailText.TextWrapping != TextWrapping.Wrap || detailText.ExtentHeight <= detailText.ViewportHeight)
            throw new InvalidOperationException("Full long-path diagnostics must remain selectable and scrollable.");
        detailWindow.Close();
        window.Close();
        Console.WriteLine("UI PASS scanFolderMultiselect=True multipleRootsDisplayed=True discoveryRecognizedSelection=True unknownDeselected=True duplicatesExcluded=True multipart=True manualSelection=True");
    }
}
