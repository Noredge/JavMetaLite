using System.IO;
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
        WindowVisualTheme.ApplyDarkTitleBar(this);
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
            WarningText.Text = string.Join(
                Environment.NewLine,
                _plan.BlockingConflicts.Select(LocalizeBlockingConflict));
            OverwriteConfirmCheckBox.Visibility = Visibility.Collapsed;
            ConfirmButton.IsEnabled = false;
            SafetyText.Text = LocalizationService.Get("Preview.SafetyConflict");
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
        WarningText.Text = LocalizationService.Get(
            _plan.SaveOptions.OverwriteExisting
                ? "Preview.WarningOverwriteEnabled"
                : "Preview.WarningOverwriteRequired",
            _plan.OverwriteConflicts.Count);
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
            PlannedChangeKind.CreateFolder => (LocalizationService.Get("Preview.Action.CreateFolder"), "#193A2A", "#72E3A6"),
            PlannedChangeKind.MoveVideo => (LocalizationService.Get("Preview.Action.MoveVideo"), "#193152", "#8DB8FF"),
            PlannedChangeKind.RenameVideo => (LocalizationService.Get("Preview.Action.RenameVideo"), "#193152", "#8DB8FF"),
            PlannedChangeKind.MoveAndRenameVideo => (LocalizationService.Get("Preview.Action.MoveAndRename"), "#193152", "#8DB8FF"),
            PlannedChangeKind.CopyAndVerifyVideo => (LocalizationService.Get("Preview.Action.CopyVerify"), "#193152", "#8DB8FF"),
            PlannedChangeKind.UpdateFile => (LocalizationService.Get("Preview.Action.Update"), "#1B3C36", "#72E3C1"),
            PlannedChangeKind.KeepFile => (LocalizationService.Get("Preview.Action.Keep"), "#252D38", "#A9B7C8"),
            PlannedChangeKind.ReplaceImage => (LocalizationService.Get("Preview.Action.ReplaceImage"), "#4A3218", "#FFD18A"),
            PlannedChangeKind.OverwriteFile => (LocalizationService.Get("Preview.Action.Overwrite"), "#4A3218", "#FFD18A"),
            _ => (LocalizationService.Get("Preview.Action.Generate"), "#233044", "#B8CFF0")
        };
        if (change.IsBlocking)
        {
            action = LocalizationService.Get("Preview.Action.Conflict");
            background = "#4A2025";
            foreground = "#FF9DA6";
        }

        return new PreviewRow(
            action,
            LocalizeDescription(change),
            change.SourcePath is null
                ? change.DestinationPath
                : $"{change.SourcePath}  →  {change.DestinationPath}",
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground)));
    }

    private static string LocalizeDescription(PlannedFileChange change)
    {
        var videoKey = change.Kind switch
        {
            PlannedChangeKind.CreateFolder => "Preview.Description.CreateFolder",
            PlannedChangeKind.MoveVideo => "Preview.Description.MoveVideo",
            PlannedChangeKind.RenameVideo => "Preview.Description.RenameVideo",
            PlannedChangeKind.MoveAndRenameVideo => "Preview.Description.MoveAndRename",
            PlannedChangeKind.CopyAndVerifyVideo => "Preview.Description.CopyVerify",
            _ => null
        };
        if (videoKey is not null)
        {
            return LocalizationService.Get(videoKey);
        }

        var extension = Path.GetExtension(change.DestinationPath);
        if (extension.Equals(".nfo", StringComparison.OrdinalIgnoreCase))
        {
            var key = change.Kind switch
            {
                PlannedChangeKind.CreateFile => "Preview.Description.GenerateNfo",
                PlannedChangeKind.OverwriteFile => "Preview.Description.OverwriteNfo",
                PlannedChangeKind.UpdateFile when change.Description.Contains("未知 XML", StringComparison.Ordinal) =>
                    "Preview.Description.UpdateNfoPreserve",
                PlannedChangeKind.UpdateFile => "Preview.Description.UpdateNfo",
                PlannedChangeKind.KeepFile => "Preview.Description.KeepNfo",
                _ => null
            };
            return key is null ? change.Description : LocalizationService.Get(key);
        }

        var fileName = Path.GetFileName(change.DestinationPath);
        var artworkRole = fileName.Contains("poster", StringComparison.OrdinalIgnoreCase)
            ? "poster"
            : fileName.Contains("fanart", StringComparison.OrdinalIgnoreCase)
                ? "fanart"
                : null;
        if (artworkRole is null &&
            change.DestinationPath.Contains("extrafanart", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get(
                change.Kind is PlannedChangeKind.ReplaceImage or PlannedChangeKind.OverwriteFile
                    ? "Preview.Description.ReplaceStill"
                    : "Preview.Description.GenerateStill");
        }

        if (artworkRole is not null)
        {
            var artworkRoleLabel = LocalizationService.Get(
                artworkRole == "poster" ? "Artwork.Role.Poster" : "Artwork.Role.Fanart");
            if (change.Description.Contains("缺失", StringComparison.Ordinal))
            {
                return LocalizationService.Get("Preview.Description.MissingArtwork", artworkRoleLabel);
            }

            var key = change.Kind switch
            {
                PlannedChangeKind.CreateFile => "Preview.Description.GenerateArtwork",
                PlannedChangeKind.ReplaceImage or PlannedChangeKind.OverwriteFile =>
                    "Preview.Description.ReplaceArtwork",
                PlannedChangeKind.KeepFile when change.SourcePath is not null &&
                                                !string.Equals(
                                                    Path.GetFullPath(change.SourcePath),
                                                    Path.GetFullPath(change.DestinationPath),
                                                    StringComparison.OrdinalIgnoreCase) =>
                    "Preview.Description.Migrate",
                PlannedChangeKind.KeepFile => "Preview.Description.KeepArtwork",
                _ => null
            };
            return key is null
                ? change.Description
                : LocalizationService.Get(key, artworkRoleLabel);
        }

        return change.Description;
    }

    private static string LocalizeBlockingConflict(string conflict)
    {
        var mappings = new (string Prefix, string Key)[]
        {
            ("自定义目标根目录路径已被文件占用：", "Preview.Conflict.CustomRootFile"),
            ("自定义目标根目录当前不可用，程序不会自动创建该根目录：", "Preview.Conflict.CustomRootUnavailable"),
            ("目标文件夹路径已被文件占用：", "Preview.Conflict.TargetFolderFile"),
            ("目标影片已经存在，软件不会覆盖影片：", "Preview.Conflict.TargetMovieExists")
        };
        foreach (var (prefix, key) in mappings)
        {
            if (conflict.StartsWith(prefix, StringComparison.Ordinal))
            {
                return LocalizationService.Get(key, conflict[prefix.Length..]);
            }
        }

        return conflict;
    }

    private sealed record PreviewRow(
        string Action,
        string Description,
        string Path,
        Brush BadgeBackground,
        Brush BadgeForeground);
}
