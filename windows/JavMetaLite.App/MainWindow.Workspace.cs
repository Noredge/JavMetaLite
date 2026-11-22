using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

public partial class MainWindow
{
    private bool? _queuePreference;
    private bool? _lastQueueVisibility;
    private bool _showImportedBatch;
    private double _queueWidth = 240;
    public static readonly DependencyProperty IsEditingMetadataProperty = DependencyProperty.Register(
        nameof(IsEditingMetadata), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public bool IsEditingMetadata
    {
        get => (bool)GetValue(IsEditingMetadataProperty);
        private set => SetValue(IsEditingMetadataProperty, value);
    }

    private MovieJob? _rangeAnchor;

    private bool QueueIsVisible => _showImportedBatch || (_queuePreference ?? _movieQueue.Count > 1);
    private bool UseBatchActions => _movieQueue.Count > 1 ||
        (_movieQueue.Count == 1 && !ReferenceEquals(_movieQueue[0], _activeJob));

    private void ToggleQueue_Click(object sender, RoutedEventArgs e)
    {
        _queuePreference = !QueueIsVisible;
        _showImportedBatch = false;
        if (!QueueIsVisible) ReleaseQueueNavigationFocus();
        RefreshQueueUi();
    }

    private void UpdateQueueLayout()
    {
        var visible = QueueIsVisible;
        if (_lastQueueVisibility == visible) return;
        if (_lastQueueVisibility == true) _queueWidth = Math.Clamp(QueueColumn.ActualWidth, 180, 400);
        QueueColumn.MinWidth = visible ? 180 : 0;
        QueueColumn.MaxWidth = visible ? 400 : 0;
        QueueColumn.Width = new GridLength(visible ? _queueWidth : 0);
        QueueSpacerColumn.Width = new GridLength(visible ? 8 : 0);
        ArtworkColumn.Width = new GridLength(visible ? 250 : 300);
        QueueToggleButton.SetResourceReference(BackgroundProperty, visible ? "SelectionBrush" : "ChromeBrush");
        _lastQueueVisibility = visible;
    }

    private void RefreshWorkspacePresentation()
    {
        if (!_uiInitialized) return;
        RefreshWorkspaceDetails();
        RefreshSaveSettingsPresentation();
        RefreshSearchToolbarLayout();
    }

    private void RefreshWorkspaceDetails()
    {
        var hasVideo = _activeJob.VideoPath is not null;
        EmptyDesk.Visibility = !hasVideo && !IsEditingMetadata &&
            string.IsNullOrEmpty(_activeJob.Metadata.Id) ? Visibility.Visible : Visibility.Collapsed;
        MetadataPanel.Visibility = EmptyDesk.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        EditReviewButton.Visibility = IsEditingMetadata ? Visibility.Collapsed : Visibility.Visible;
        FinishEditButton.Visibility = IsEditingMetadata ? Visibility.Visible : Visibility.Collapsed;
        InputFileDetails.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        RemoveCurrentVideoButton.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        RemoveCurrentVideoButton.IsEnabled = !_busy && hasVideo;
    }

    private void ToggleEditor_Click(object sender, RoutedEventArgs e)
    {
        IsEditingMetadata = !IsEditingMetadata;
        RefreshWorkspaceDetails();
        RefreshSourceBadges();
        if (IsEditingMetadata && MetadataScrollViewer.VerticalOffset < 1)
            TitleEditor.Focus();
        else if (IsEditingMetadata)
            FinishEditButton.Focus();
        else
            EditReviewButton.Focus();
    }

    private void ArtworkHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ArtworkPreviewHeading.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Grid.SetRow(ArtworkActions, ArtworkPreviewHeading.DesiredSize.Width + 68 > e.NewSize.Width ? 1 : 0);
    }

    private void ArtViewport_SizeChanged(object sender, SizeChangedEventArgs e) => SizeArtwork();

    private void SizeArtwork()
    {
        if (ArtViewport is null || FanartCaption is null || ArtViewport.ActualWidth <= 0) return;
        var width = ArtViewport.ActualWidth;
        var measure = new Size(width, double.PositiveInfinity);
        PosterRoleText.Measure(measure);
        FanartCaption.Measure(measure);
        var height = Math.Max(0, ArtViewport.ActualHeight - PosterRoleText.DesiredSize.Height - FanartCaption.DesiredSize.Height);
        var candidate = _activeJob.ArtworkReview.SelectedCandidate;
        var posterRatio = ArtworkRatio(PosterImage, candidate?.HasPoster != false, 1.4);
        var sleeveRatio = ArtworkRatio(FanartImage, candidate?.HasFanart != false, 0.67);
        var placeholderHeight = Math.Min(64, height / 3);
        var reserved = (posterRatio == 0 ? placeholderHeight : 0) + (sleeveRatio == 0 ? placeholderHeight : 0);
        var ratios = posterRatio + sleeveRatio;
        if (ratios > 0) width = Math.Min(width, (height - reserved) / ratios);

        ArtworkStack.Width = width;
        PosterPreviewBorder.Height = posterRatio == 0 ? placeholderHeight : width * posterRatio;
        FanartPreviewBorder.Height = sleeveRatio == 0 ? placeholderHeight : width * sleeveRatio;
    }

    private static double ArtworkRatio(Image image, bool expected, double loadingRatio) =>
        image.Source is { Width: > 0, Height: > 0 } source ? source.Height / source.Width : expected ? loadingRatio : 0;

    private void Preferences_Click(object sender, RoutedEventArgs e) =>
        AppearancePopup.IsOpen = true;

    private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiInitialized && AppearanceChoice.SelectedItem is ComboBoxItem { Tag: string appearance })
            WindowVisualTheme.SetAppearance(appearance);
    }

    private void QueueOptions_Click(object sender, RoutedEventArgs e)
    {
        QueueOptions.PlacementTarget = QueueOptionsButton;
        QueueOptions.IsOpen = true;
    }

    private void QueueCheckAll_Click(object sender, RoutedEventArgs e)
    {
        SetBatchSelection(!_movieQueue.All(job => job.IsSelectedForBatch));
        _rangeAnchor = null;
    }

    private void QueueCheck_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy || sender is not CheckBox { DataContext: MovieJob job }) return;
        var first = _rangeAnchor is null ? -1 : MovieQueueList.Items.IndexOf(_rangeAnchor);
        var last = MovieQueueList.Items.IndexOf(job);
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || first < 0 || last < 0)
        {
            _rangeAnchor = job;
            return;
        }
        var check = !job.IsSelectedForBatch;
        for (var i = Math.Min(first, last); i <= Math.Max(first, last); i++)
            ((MovieJob)MovieQueueList.Items[i]).IsSelectedForBatch = check;
        e.Handled = true;
        RefreshQueueUi();
    }

    private void WorkspaceSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !SearchQueueButton.IsEnabled) return;
        if (UseBatchActions) SearchQueue_Click(sender, e);
        else Search_Click(sender, e);
    }

    private void ApplySelectionSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _applyingSaveConfiguration || !_uiInitialized) return;
        var config = CaptureCurrentSaveConfiguration();
        foreach (var job in _movieQueue.Where(job => job.IsSelectedForBatch))
            job.UpdateSaveConfiguration(config);
    }
}
