using System.Collections;
using System.Windows;
using System.Windows.Controls;
using JavMetaLite.App;
using JavMetaLite.Core.Services;

namespace JavMetaLite.UiSmokeTests;

internal static partial class Program
{
    private static void TestQueueFilterSelection()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            ((RadioButton)window.FindName("BatchModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var queue = (IList)typeof(MainWindow).GetField("_movieQueue", PrivateInstance)!.GetValue(window)!;
            var attach = typeof(MainWindow).GetMethod("AttachMovieJob", PrivateInstance)!;
            var normal = new MovieJob();
            normal.ResetForVideo(@"C:\Synthetic\IPX-123.mp4", "IPX-123");
            var issue = new MovieJob();
            issue.ResetForVideo(@"C:\Synthetic\unknown.mp4", "");
            foreach (var job in new[] { normal, issue }) { attach.Invoke(window, [job]); queue.Add(job); }
            var filter = (ComboBox)window.FindName("QueueFilterComboBox");
            void Select(string tag) => filter.SelectedItem =
                filter.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == tag);
            void Refresh() => typeof(MainWindow).GetMethod("RefreshQueueUi", PrivateInstance)!.Invoke(window, []);
            void CheckCounts(int all, int pending, int issues)
            {
                Refresh();
                filter.IsDropDownOpen = true;
                WaitForCondition(() => filter.Items.Cast<ComboBoxItem>().All(i => i.IsVisible),
                    "Queue filter dropdown did not open.");
                window.UpdateLayout();
                foreach (var (name, expected) in new[] { ("QueueFilterAllItem", all),
                    ("QueueFilterPendingItem", pending), ("QueueFilterIssuesItem", issues) })
                {
                    var item = (ComboBoxItem)window.FindName(name);
                    item.ApplyTemplate();
                    var count = (TextBlock)item.Template.FindName("CategoryCount", item);
                    if (count.Text != $"({expected})" || count.HorizontalAlignment != HorizontalAlignment.Right)
                        throw new InvalidOperationException("Dropdown counts must use parenthesized, right-aligned totals.");
                }
                filter.IsDropDownOpen = false;
            }
            CheckCounts(2, 1, 1);
            void Click(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("SelectPendingQueueButton");
            if (!normal.IsSelectedForBatch || issue.IsSelectedForBatch)
                throw new InvalidOperationException("Pending button must select only matching movies.");
            if ((filter.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "all")
                throw new InvalidOperationException("Selection buttons must not change the display filter.");
            normal.IsSelectedForBatch = false;
            Refresh();
            CheckCounts(2, 1, 1);
            if (normal.IsSelectedForBatch) throw new InvalidOperationException("Refresh must not override manual deselection.");
            Click("SelectPendingQueueButton");
            if (!normal.IsSelectedForBatch || issue.IsSelectedForBatch)
                throw new InvalidOperationException("Pending selection must replace manual selection.");
            Select("issues");
            CheckCounts(2, 1, 1);
            if (!normal.IsSelectedForBatch || issue.IsSelectedForBatch)
                throw new InvalidOperationException("Changing the filter must not alter batch selection.");
            Click("SelectIssuesQueueButton");
            if (normal.IsSelectedForBatch || !issue.IsSelectedForBatch)
                throw new InvalidOperationException("Issues button must replace selection.");
            Click("SelectPendingQueueButton");
            if (!normal.IsSelectedForBatch || issue.IsSelectedForBatch ||
                (filter.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "issues")
                throw new InvalidOperationException("Selection must cover the whole queue without altering an active filter.");
            Select("all");
            Click("SelectAllQueueButton");
            if (!normal.IsSelectedForBatch || !issue.IsSelectedForBatch)
                throw new InvalidOperationException("Select all must retain its original behavior.");
            issue.Metadata.Id = "IPX-456";
            CheckCounts(2, 2, 0);
            Click("SelectIssuesQueueButton");
            if (normal.IsSelectedForBatch || issue.IsSelectedForBatch ||
                ((Button)window.FindName("SaveSelectedButton")).IsEnabled)
                throw new InvalidOperationException("Empty issue category must clear selection.");
            typeof(MainWindow).GetField("_busy", PrivateInstance)!.SetValue(window, true);
            Refresh();
            Click("SelectPendingQueueButton");
            if (((Button)window.FindName("SelectPendingQueueButton")).IsEnabled ||
                ((Button)window.FindName("SelectIssuesQueueButton")).IsEnabled ||
                normal.IsSelectedForBatch || issue.IsSelectedForBatch)
                throw new InvalidOperationException("Active task must prevent category selection.");
            typeof(MainWindow).GetField("_busy", PrivateInstance)!.SetValue(window, false);
            Refresh();
            if (!((Button)window.FindName("SelectPendingQueueButton")).IsEnabled)
                throw new InvalidOperationException("Idle task must restore category selection.");
        }
        finally { window.Close(); }
        Console.WriteLine("UI PASS categoryButtonsSelectMatchingOnly=True filtersUnchanged=True manualSelectionPreserved=True emptyCategorySafe=True busySelectionDisabled=True");
    }
}
