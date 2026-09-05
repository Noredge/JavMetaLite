using System.Windows;
using JavMetaLite.Core.Models;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

public sealed record BatchSavePreviewItem(
    MovieJob Job,
    SavePlan Plan,
    long ReviewRevision);

public sealed record BatchSavePreviewIssue(
    MovieJob Job,
    string Message);

public partial class BatchSavePreviewWindow : Window
{
    private readonly bool _hasBlockingConflicts;
    private readonly bool _requiresOverwriteConfirmation;

    public BatchSavePreviewWindow(
        IReadOnlyList<BatchSavePreviewItem> items,
        IReadOnlyList<BatchSavePreviewIssue>? issues = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        issues ??= [];
        if (items.Count == 0 && issues.Count == 0)
        {
            throw new ArgumentException("批量保存预览至少需要一个项目或问题。", nameof(items));
        }

        InitializeComponent();
        WindowVisualTheme.ApplyDarkTitleBar(this);
        Items = items;
        Issues = issues;
        PlansList.ItemsSource = items.Select(CreateGroup).ToArray();
        IssuesList.ItemsSource = issues.Select(CreateIssueRow).ToArray();
        IssuesPanel.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _hasBlockingConflicts = items.Any(item => item.Plan.HasBlockingConflicts);
        _requiresOverwriteConfirmation = items.Any(item =>
            item.Plan.OverwriteConflicts.Count > 0 && !item.Plan.SaveOptions.OverwriteExisting);
        ConfigureWarnings();
        SummaryText.Text = LocalizationService.Get(
            "BatchPreview.Summary",
            items.Count,
            issues.Count,
            items.Sum(item => item.Plan.Changes.Count));
    }

    internal IReadOnlyList<BatchSavePreviewItem> Items { get; }

    internal IReadOnlyList<BatchSavePreviewIssue> Issues { get; }

    public bool AllowOverwrite { get; private set; }

    private void ConfigureWarnings()
    {
        if (_hasBlockingConflicts)
        {
            WarningPanel.Visibility = Visibility.Visible;
            WarningPanel.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(57, 25, 29));
            WarningPanel.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(133, 54, 61));
            WarningText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 157, 166));
            WarningText.Text = LocalizationService.Get("BatchPreview.Blocked");
            OverwriteConfirmCheckBox.Visibility = Visibility.Collapsed;
            ConfirmButton.IsEnabled = false;
            return;
        }

        if (_requiresOverwriteConfirmation)
        {
            WarningPanel.Visibility = Visibility.Visible;
            WarningText.Text = LocalizationService.Get("BatchPreview.OverwriteWarning");
            ConfirmButton.IsEnabled = false;
            return;
        }

        WarningPanel.Visibility = Visibility.Collapsed;
        ConfirmButton.IsEnabled = Items.Count > 0;
    }

    private void OverwriteConfirm_Changed(object sender, RoutedEventArgs e)
    {
        AllowOverwrite = OverwriteConfirmCheckBox.IsChecked == true;
        ConfirmButton.IsEnabled = Items.Count > 0 &&
                                  !_hasBlockingConflicts &&
                                  (!_requiresOverwriteConfirmation || AllowOverwrite);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton.IsEnabled)
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static BatchPlanGroup CreateGroup(BatchSavePreviewItem item) => new(
        $"{item.Job.Metadata.Id} · {item.Job.FileName}",
        item.Plan.SourceVideoPath,
        item.Plan.TargetVideoPath,
        item.Plan.Changes.Select(change => new BatchChangeRow(
            LocalizeAction(change.Kind),
            change.SourcePath is null
                ? change.DestinationPath
                : $"{change.SourcePath}  →  {change.DestinationPath}")).ToArray());

    private static BatchIssueRow CreateIssueRow(BatchSavePreviewIssue issue) => new(
        $"{issue.Job.Metadata.Id} · {issue.Job.FileName}",
        issue.Message);

    private static string LocalizeAction(PlannedChangeKind kind) =>
        LocalizationService.Get(kind switch
        {
            PlannedChangeKind.CreateFolder => "Preview.Action.CreateFolder",
            PlannedChangeKind.MoveVideo => "Preview.Action.MoveVideo",
            PlannedChangeKind.RenameVideo => "Preview.Action.RenameVideo",
            PlannedChangeKind.MoveAndRenameVideo => "Preview.Action.MoveAndRename",
            PlannedChangeKind.CopyAndVerifyVideo => "Preview.Action.CopyVerify",
            PlannedChangeKind.CopyVideo => "Preview.Action.CopyFast",
            PlannedChangeKind.UpdateFile => "Preview.Action.Update",
            PlannedChangeKind.KeepFile => "Preview.Action.Keep",
            PlannedChangeKind.ReplaceImage => "Preview.Action.ReplaceImage",
            PlannedChangeKind.OverwriteFile => "Preview.Action.Overwrite",
            PlannedChangeKind.RemoveFile => "Preview.Action.Remove",
            _ => "Preview.Action.Generate"
        });

    private sealed record BatchPlanGroup(
        string Header,
        string SourcePath,
        string TargetPath,
        IReadOnlyList<BatchChangeRow> Changes);

    private sealed record BatchIssueRow(string Header, string Message);

    private sealed record BatchChangeRow(string Action, string Path);
}
