using System.Windows;
using System.Windows.Media;
using JavMetaLite.Core.Models;

namespace JavMetaLite.App;

public partial class SavePreviewWindow : Window
{
    private readonly SavePlan _plan;

    public SavePreviewWindow(SavePlan plan)
    {
        InitializeComponent();
        _plan = plan;
        SourcePathTextBox.Text = plan.SourceVideoPath;
        TargetPathTextBox.Text = plan.TargetVideoPath;
        ChangesList.ItemsSource = plan.Changes.Select(CreateRow).ToArray();
        ConfigureWarnings();
    }

    public bool AllowOverwrite { get; private set; }

    private void ConfigureWarnings()
    {
        if (_plan.HasBlockingConflicts)
        {
            WarningPanel.Visibility = Visibility.Visible;
            WarningPanel.Background = new SolidColorBrush(Color.FromRgb(57, 25, 29));
            WarningPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(133, 54, 61));
            WarningText.Foreground = new SolidColorBrush(Color.FromRgb(255, 157, 166));
            WarningText.Text = string.Join(Environment.NewLine, _plan.BlockingConflicts);
            OverwriteConfirmCheckBox.Visibility = Visibility.Collapsed;
            ConfirmButton.IsEnabled = false;
            SafetyText.Text = "请取消并处理目标影片冲突后重新预览。";
            return;
        }

        if (_plan.OverwriteConflicts.Count == 0)
        {
            AllowOverwrite = _plan.SaveOptions.OverwriteExisting;
            WarningPanel.Visibility = Visibility.Collapsed;
            ConfirmButton.IsEnabled = true;
            return;
        }

        WarningPanel.Visibility = Visibility.Visible;
        WarningText.Text = $"检测到 {_plan.OverwriteConflicts.Count} 个已有 metadata 文件。" +
                           (_plan.SaveOptions.OverwriteExisting
                               ? " 已启用覆盖；确认后会替换这些文件。"
                               : " 必须明确确认后才能覆盖。影片文件永远不会被覆盖。");
        if (_plan.SaveOptions.OverwriteExisting)
        {
            AllowOverwrite = true;
            OverwriteConfirmCheckBox.Visibility = Visibility.Collapsed;
            ConfirmButton.IsEnabled = true;
        }
        else
        {
            AllowOverwrite = false;
            OverwriteConfirmCheckBox.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
        }
    }

    private void OverwriteConfirm_Changed(object sender, RoutedEventArgs e)
    {
        if (_plan.HasBlockingConflicts)
        {
            return;
        }

        AllowOverwrite = OverwriteConfirmCheckBox.IsChecked == true;
        ConfirmButton.IsEnabled = AllowOverwrite;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_plan.HasBlockingConflicts ||
            (_plan.OverwriteConflicts.Count > 0 && !AllowOverwrite))
        {
            return;
        }

        DialogResult = true;
    }

    private static PreviewRow CreateRow(PlannedFileChange change)
    {
        var (action, background, foreground) = change.Kind switch
        {
            PlannedChangeKind.CreateFolder => ("创建文件夹", "#193A2A", "#72E3A6"),
            PlannedChangeKind.MoveVideo => ("移动影片", "#193152", "#8DB8FF"),
            PlannedChangeKind.RenameVideo => ("重命名影片", "#193152", "#8DB8FF"),
            PlannedChangeKind.MoveAndRenameVideo => ("移动并重命名", "#193152", "#8DB8FF"),
            PlannedChangeKind.OverwriteFile => ("覆盖", "#4A3218", "#FFD18A"),
            _ => ("生成", "#233044", "#B8CFF0")
        };
        if (change.IsBlocking)
        {
            action = "冲突";
            background = "#4A2025";
            foreground = "#FF9DA6";
        }

        return new PreviewRow(
            action,
            change.Description,
            change.SourcePath is null
                ? change.DestinationPath
                : $"{change.SourcePath}  →  {change.DestinationPath}",
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground)));
    }

    private sealed record PreviewRow(
        string Action,
        string Description,
        string Path,
        Brush BadgeBackground,
        Brush BadgeForeground);
}
