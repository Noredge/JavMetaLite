using System.IO;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

public enum ArtworkViewerItemKind
{
    Poster,
    Fanart,
    Extrafanart
}

public sealed record ArtworkViewerLoadedImage(ImageSource Image, int PixelWidth, int PixelHeight);

public sealed class ArtworkViewerItem
{
    public ArtworkViewerItem(
        string title,
        ImageSource? image,
        string? sourceLocation = null,
        bool isSelectableSample = false,
        bool isSelected = true,
        ArtworkViewerItemKind kind = ArtworkViewerItemKind.Poster,
        bool isLocal = false,
        bool canDeleteLocal = true,
        string? artworkSourceName = null,
        bool requiresPosterCrop = false,
        IReadOnlyList<string>? sourceLocations = null,
        string? artworkSourceDisplayName = null,
        string? artworkSourceDescription = null,
        IReadOnlyList<string>? relatedSourceNames = null)
    {
        Title = title;
        Image = image;
        SourceLocations = (sourceLocations ?? [sourceLocation ?? string.Empty])
            .Where(ArtworkLocationHelper.IsSupported)
            .Select(ArtworkLocationHelper.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SourceLocation = SourceLocations.FirstOrDefault();
        IsSelectableSample = isSelectableSample;
        IsSelected = isSelected;
        Kind = isSelectableSample ? ArtworkViewerItemKind.Extrafanart : kind;
        IsLocal = isLocal;
        CanDeleteLocal = isLocal && canDeleteLocal;
        ArtworkSourceName = artworkSourceName;
        ArtworkSourceDisplayName = artworkSourceDisplayName;
        ArtworkSourceDescription = artworkSourceDescription;
        RelatedSourceNames = (relatedSourceNames ?? [])
            .Append(artworkSourceName ?? string.Empty)
            .Where(sourceName => !string.IsNullOrWhiteSpace(sourceName))
            .Select(sourceName => sourceName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        RequiresPosterCrop = requiresPosterCrop;
        if (image is BitmapSource bitmap)
        {
            PixelWidth = bitmap.PixelWidth;
            PixelHeight = bitmap.PixelHeight;
        }
    }

    public string Title { get; }

    public ImageSource? Image { get; internal set; }

    public string? SourceLocation { get; }

    public IReadOnlyList<string> SourceLocations { get; }

    public bool IsSelectableSample { get; }

    public bool IsSelected { get; set; }

    public bool IsSelectedForDeletion { get; set; }

    public ArtworkViewerItemKind Kind { get; }

    public bool IsLocal { get; }

    public bool CanDeleteLocal { get; }

    public string? ArtworkSourceName { get; }

    public string? ArtworkSourceDisplayName { get; }

    public string? ArtworkSourceDescription { get; }

    public IReadOnlyList<string> RelatedSourceNames { get; }

    public bool RequiresPosterCrop { get; }

    public bool IsArtworkSourceCandidate =>
        !string.IsNullOrWhiteSpace(ArtworkSourceName) &&
        Kind is ArtworkViewerItemKind.Poster or ArtworkViewerItemKind.Fanart;

    public bool BelongsToSource(string? sourceName) =>
        !string.IsNullOrWhiteSpace(sourceName) &&
        RelatedSourceNames.Contains(sourceName, StringComparer.OrdinalIgnoreCase);

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    internal Task<ArtworkViewerLoadedImage>? LoadTask { get; set; }

    internal void SetLoadedImage(ArtworkViewerLoadedImage loaded)
    {
        Image = loaded.Image;
        PixelWidth = loaded.PixelWidth;
        PixelHeight = loaded.PixelHeight;
    }
}

public partial class ArtworkViewerWindow : Window
{
    private const int PreloadConcurrency = 4;
    private readonly List<ArtworkViewerItem> _items;
    private readonly Func<ArtworkViewerItem, CancellationToken, Task<ArtworkViewerLoadedImage>>? _imageLoader;
    private readonly Func<IReadOnlyList<ArtworkViewerItem>, bool> _confirmLocalDeletion;
    private readonly Action<string> _deleteLocalImage;
    private readonly List<string> _deletedLocalArtworkLocations = [];
    private readonly List<ArtworkSourceOption> _artworkSourceOptions = [];
    private readonly Dictionary<string, HashSet<string>> _sampleSelectionsBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<ArtworkViewerItem, ArtworkThumbnail> _thumbnails = [];
    private IReadOnlyList<ArtworkViewerItem> _visibleItems = [];
    private int _index;
    private int _preloadCompleted;
    private int _preloadTotal;
    private long _navigationRevision;
    private bool _changingFilter;
    private bool _updatingSelectionUi;
    private bool _filmstripExpanded;
    private bool _closed;
    private string? _initialArtworkSourceName;
    private string? _selectedArtworkSourceName;

    public IReadOnlyList<string> SelectedSampleLocations => _items
        .Where(item => item.IsSelectableSample && item.IsLocal)
        .Select(item => item.SourceLocation!)
        .Concat(GetSelectedOnlineSampleLocations())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<string> DeletedLocalArtworkLocations => _deletedLocalArtworkLocations;

    public string? SelectedArtworkSourceName => _selectedArtworkSourceName;

    public bool ArtworkSourceChanged => !string.Equals(
        _initialArtworkSourceName,
        _selectedArtworkSourceName,
        StringComparison.OrdinalIgnoreCase);

    public ArtworkViewerWindow(IReadOnlyList<ArtworkViewerItem> items, int initialIndex)
        : this(items, initialIndex, null, null, null, null, null)
    {
    }

    public ArtworkViewerWindow(
        IReadOnlyList<ArtworkViewerItem> items,
        int initialIndex,
        Func<ArtworkViewerItem, CancellationToken, Task<ArtworkViewerLoadedImage>>? imageLoader,
        ArtworkViewerItemKind? initialKind = null,
        string? currentArtworkSourceName = null,
        Func<IReadOnlyList<ArtworkViewerItem>, bool>? confirmLocalDeletion = null,
        Action<string>? deleteLocalImage = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("至少需要一张图片。", nameof(items));
        }

        _items = items.ToList();
        _imageLoader = imageLoader;
        _selectedArtworkSourceName = currentArtworkSourceName;
        _confirmLocalDeletion = confirmLocalDeletion ?? ConfirmLocalDeletionWithDialog;
        _deleteLocalImage = deleteLocalImage ?? File.Delete;
        var initialItem = items[Math.Clamp(initialIndex, 0, items.Count - 1)];
        InitializeComponent();
        WindowVisualTheme.ApplyDarkTitleBar(this);
        RefreshArtworkSourceOptions();
        _initialArtworkSourceName = _selectedArtworkSourceName;
        InitializeCurrentSourceSampleSelection();
        ConfigureTypeFilter(initialKind);
        ApplyFilter(initialKind, initialItem);
        StartPreloading();
    }

    private void ConfigureTypeFilter(ArtworkViewerItemKind? initialKind)
    {
        var scopedItems = GetSourceScopedItems();
        var options = new[]
        {
            new ArtworkTypeFilterOption(null, LocalizationService.Get("ArtworkViewer.FilterAll", scopedItems.Count)),
            new ArtworkTypeFilterOption(
                ArtworkViewerItemKind.Poster,
                LocalizationService.Get("ArtworkViewer.FilterPoster", scopedItems.Count(item => item.Kind is ArtworkViewerItemKind.Poster))),
            new ArtworkTypeFilterOption(
                ArtworkViewerItemKind.Fanart,
                LocalizationService.Get("ArtworkViewer.FilterFanart", scopedItems.Count(item => item.Kind is ArtworkViewerItemKind.Fanart))),
            new ArtworkTypeFilterOption(
                ArtworkViewerItemKind.Extrafanart,
                LocalizationService.Get("ArtworkViewer.FilterExtrafanart", scopedItems.Count(item => item.Kind is ArtworkViewerItemKind.Extrafanart)))
        };
        ArtworkTypeComboBox.ItemsSource = options;
        _changingFilter = true;
        ArtworkTypeComboBox.SelectedItem = options.First(option => option.Kind == initialKind);
        _changingFilter = false;
    }

    private void ArtworkType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingFilter || ArtworkTypeComboBox.SelectedItem is not ArtworkTypeFilterOption option)
        {
            return;
        }

        var currentItem = _visibleItems.Count == 0 ? null : _visibleItems[_index];
        ApplyFilter(option.Kind, currentItem);
    }

    private void ApplyFilter(ArtworkViewerItemKind? kind, ArtworkViewerItem? preferredItem)
    {
        _navigationRevision++;
        var scopedItems = GetSourceScopedItems();
        _visibleItems = kind is null
            ? scopedItems
            : scopedItems.Where(item => item.Kind == kind).ToArray();
        EmptyTypeText.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArtworkImage.Visibility = _visibleItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ArtworkFilmstripSection.Visibility = _visibleItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_visibleItems.Count == 0)
        {
            RebuildFilmstrip();
            ArtworkTitleText.Text = LocalizationService.Get("ArtworkViewer.EmptyType");
            ArtworkPositionText.Text = "0 / 0";
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            CurrentSampleSelectionCheckBox.Visibility = Visibility.Collapsed;
            ArtworkDimensionsText.Visibility = Visibility.Collapsed;
            ArtworkLoadStatePanel.Visibility = Visibility.Collapsed;
            RefreshSelectionSummary();
            return;
        }

        var preferredIndex = preferredItem is null ? -1 : IndexOfReference(_visibleItems, preferredItem);
        _index = preferredIndex >= 0 ? preferredIndex : 0;
        RebuildFilmstrip();
        _ = RefreshImageAsync();
    }

    private void RebuildFilmstrip()
    {
        LocalThumbnailPanel.Children.Clear();
        ThumbnailPanel.Children.Clear();
        _thumbnails.Clear();
        for (var index = 0; index < _visibleItems.Count; index++)
        {
            var item = _visibleItems[index];
            var thumbnailImage = new Image
            {
                Source = item.Image,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var placeholder = new TextBlock
            {
                Text = (index + 1).ToString(),
                Foreground = FindResource("MutedBrush") as Brush,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = item.Image is null ? Visibility.Visible : Visibility.Collapsed
            };
            var thumbnailContent = new Grid();
            thumbnailContent.Children.Add(thumbnailImage);
            thumbnailContent.Children.Add(placeholder);

            CheckBox? selectionBox = null;
            if (item.CanDeleteLocal || item.IsSelectableSample)
            {
                selectionBox = new CheckBox
                {
                    IsChecked = item.CanDeleteLocal ? item.IsSelectedForDeletion : item.IsSelected,
                    Style = FindResource("ArtworkThumbnailCheckBox") as Style,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 4, 4, 0),
                    ToolTip = LocalizationService.Get(item.CanDeleteLocal
                        ? "ArtworkViewer.SelectForDeletion"
                        : "ArtworkViewer.IncludeSample")
                };
                var capturedItem = item;
                selectionBox.Click += (_, e) =>
                {
                    var desiredSelection = selectionBox.IsChecked == true;
                    if (capturedItem.CanDeleteLocal)
                    {
                        capturedItem.IsSelectedForDeletion = desiredSelection;
                        RefreshLocalDeletionUi();
                    }
                    else if (!TrySetSampleSelection(capturedItem, desiredSelection))
                    {
                        selectionBox.IsChecked = capturedItem.IsSelected;
                    }
                    if (_visibleItems.Count > 0 && ReferenceEquals(_visibleItems[_index], capturedItem))
                    {
                        RefreshCurrentSelectionUi(capturedItem);
                    }
                    else
                    {
                        RefreshFilmstripSelection();
                    }
                    RefreshSelectionSummary();
                    e.Handled = true;
                };
                Panel.SetZIndex(selectionBox, 2);
                thumbnailContent.Children.Add(selectionBox);
            }

            var container = new Border
            {
                Width = GetThumbnailWidth(item),
                Height = 72,
                Margin = new Thickness(3),
                Padding = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(8, 12, 18)),
                BorderBrush = FindResource("BorderBrush") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                ToolTip = item.Title,
                Tag = item,
                Child = thumbnailContent
            };
            AutomationProperties.SetName(container, item.Title);
            var capturedThumbnailItem = item;
            container.MouseLeftButtonUp += (_, e) =>
            {
                SelectVisibleItem(capturedThumbnailItem);
                e.Handled = true;
            };
            _thumbnails[item] = new ArtworkThumbnail(container, thumbnailImage, placeholder, selectionBox);
            (item.IsLocal ? LocalThumbnailPanel : ThumbnailPanel).Children.Add(container);
        }

        RefreshFilmstripSections();
        RefreshFilmstripSelection();
    }

    private void SelectVisibleItem(ArtworkViewerItem item)
    {
        var index = IndexOfReference(_visibleItems, item);
        if (index < 0)
        {
            return;
        }

        _index = index;
        _ = RefreshImageAsync();
    }

    private void RefreshFilmstripSelection()
    {
        var currentItem = _visibleItems.Count == 0 ? null : _visibleItems[_index];
        foreach (var (item, thumbnail) in _thumbnails)
        {
            var isCurrent = ReferenceEquals(item, currentItem);
            thumbnail.Container.BorderBrush = FindResource(
                isCurrent ? "AccentBorderBrush" : "BorderBrush") as Brush;
            thumbnail.Container.BorderThickness = new Thickness(isCurrent ? 2 : 1);
            if (thumbnail.SelectionBox is not null)
            {
                thumbnail.SelectionBox.IsChecked = item.CanDeleteLocal
                    ? item.IsSelectedForDeletion
                    : item.IsSelected;
            }
        }

        if (currentItem is not null && _thumbnails.TryGetValue(currentItem, out var currentThumbnail))
        {
            currentThumbnail.Container.BringIntoView();
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void Next_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplySelection_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void ArtworkSourceSelector_Click(object sender, RoutedEventArgs e)
    {
        if (_artworkSourceOptions.Count < 2)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = ArtworkSourceSelectorButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Style = (Style)FindResource("CandidateContextMenu")
        };
        foreach (var option in _artworkSourceOptions)
        {
            var isSelected = string.Equals(
                option.Name,
                _selectedArtworkSourceName,
                StringComparison.OrdinalIgnoreCase);
            var sourceText = new TextBlock
            {
                Text = $"{(isSelected ? "✓ " : string.Empty)}{option.DisplayName}",
                Foreground = new SolidColorBrush(Color.FromRgb(111, 168, 255)),
                FontWeight = FontWeights.SemiBold
            };
            var header = new StackPanel();
            header.Children.Add(sourceText);
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                header.Children.Add(new TextBlock
                {
                    Text = option.Description,
                    Foreground = new SolidColorBrush(Color.FromRgb(184, 197, 214)),
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            var menuItem = new MenuItem
            {
                Header = header,
                Tag = option.Name,
                Style = (Style)FindResource("CandidateMenuItem")
            };
            AutomationProperties.SetName(menuItem, option.DisplayName);
            menuItem.Click += (_, _) => SelectArtworkSource(option);
            menu.Items.Add(menuItem);
        }

        ArtworkSourceSelectorButton.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void SelectArtworkSource(ArtworkSourceOption option)
    {
        var filterKind = (ArtworkTypeComboBox.SelectedItem as ArtworkTypeFilterOption)?.Kind;
        var currentItem = _visibleItems.Count == 0 ? null : _visibleItems[_index];
        _selectedArtworkSourceName = option.Name;
        EnsureSampleSelectionForSource(option.Name, selectAllWhenNew: true);
        SynchronizeSampleSelectionUi();
        RefreshArtworkSourceSelector();
        ConfigureTypeFilter(filterKind);
        var scopedItems = GetSourceScopedItems();
        var matchingItem = currentItem is null
            ? null
            : scopedItems.FirstOrDefault(item =>
                item.Kind == currentItem.Kind &&
                item.BelongsToSource(option.Name));
        ApplyFilter(filterKind, matchingItem);
    }

    private void SelectAllSamples_Click(object sender, RoutedEventArgs e) => SetAllSamplesSelected(true);

    private void SelectNoSamples_Click(object sender, RoutedEventArgs e) => SetAllSamplesSelected(false);

    private void ToggleFilmstrip_Click(object sender, RoutedEventArgs e)
    {
        _filmstripExpanded = !_filmstripExpanded;
        RefreshFilmstripDisclosure();
    }

    private void SelectAllLocal_Click(object sender, RoutedEventArgs e) => SetAllVisibleLocalSelected(true);

    private void SelectNoLocal_Click(object sender, RoutedEventArgs e) => SetAllVisibleLocalSelected(false);

    private void DeleteSelectedLocal_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _items
            .Where(item => item.CanDeleteLocal && item.IsSelectedForDeletion)
            .ToArray();
        if (selectedItems.Length == 0 || !_confirmLocalDeletion(selectedItems))
        {
            return;
        }

        var deletedItems = new List<ArtworkViewerItem>();
        var failures = new List<string>();
        foreach (var item in selectedItems)
        {
            if (!ArtworkLocationHelper.TryGetLocalPath(item.SourceLocation, out var localPath))
            {
                failures.Add(item.Title);
                continue;
            }

            try
            {
                _deleteLocalImage(localPath);
                if (!_deletedLocalArtworkLocations.Contains(localPath, StringComparer.OrdinalIgnoreCase))
                {
                    _deletedLocalArtworkLocations.Add(localPath);
                }
                deletedItems.Add(item);
            }
            catch (Exception exception)
            {
                AppLog.Warning($"查看器删除本地图片失败：{localPath}", exception);
                failures.Add($"{Path.GetFileName(localPath)}: {exception.Message}");
            }
        }

        if (deletedItems.Count > 0)
        {
            RemoveArtworkItems(deletedItems);
        }

        if (failures.Count > 0)
        {
            var dialog = new AppDialogWindow(
                LocalizationService.Get("ArtworkViewer.DeleteLocalTitle"),
                LocalizationService.Get("ArtworkViewer.DeleteLocalFailed", string.Join(Environment.NewLine, failures)),
                LocalizationService.Get("Common.Close"),
                showCancel: false,
                destructive: false)
            {
                Owner = this
            };
            _ = dialog.ShowDialog();
        }
    }

    private void RemoveArtworkItems(IReadOnlyCollection<ArtworkViewerItem> items)
    {
        var filterKind = (ArtworkTypeComboBox.SelectedItem as ArtworkTypeFilterOption)?.Kind;
        var currentItem = _visibleItems.Count == 0 ? null : _visibleItems[_index];
        var removedIndex = currentItem is null ? 0 : Math.Max(0, IndexOfReference(_visibleItems, currentItem));
        foreach (var item in items)
        {
            _items.Remove(item);
        }
        RefreshArtworkSourceOptions();
        EnsureSampleSelectionForSource(_selectedArtworkSourceName, selectAllWhenNew: false);
        SynchronizeSampleSelectionUi();
        var scopedItems = GetSourceScopedItems();
        var remainingVisibleItems = filterKind is null
            ? scopedItems
            : scopedItems.Where(candidate => candidate.Kind == filterKind).ToList();
        var preferredItem = currentItem is not null && _items.Contains(currentItem)
            ? currentItem
            : remainingVisibleItems.Count == 0
                ? null
                : remainingVisibleItems[Math.Min(removedIndex, remainingVisibleItems.Count - 1)];
        ConfigureTypeFilter(filterKind);
        ApplyFilter(filterKind, preferredItem);
        RefreshSelectionSummary();
    }

    private void CurrentSampleSelection_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSelectionUi ||
            _visibleItems.Count == 0 ||
            !_visibleItems[_index].IsSelectableSample ||
            _visibleItems[_index].IsLocal)
        {
            return;
        }

        var item = _visibleItems[_index];
        var desiredSelection = CurrentSampleSelectionCheckBox.IsChecked == true;
        if (!TrySetSampleSelection(item, desiredSelection))
        {
            RefreshCurrentSelectionUi(item);
            return;
        }

        RefreshFilmstripSelection();
        RefreshSelectionSummary();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            Move(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            Move(1);
            e.Handled = true;
        }
    }

    private void Move(int offset)
    {
        if (_visibleItems.Count < 2)
        {
            return;
        }

        _index = (_index + offset + _visibleItems.Count) % _visibleItems.Count;
        _ = RefreshImageAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    private async Task RefreshImageAsync()
    {
        if (_visibleItems.Count == 0)
        {
            return;
        }

        var revision = ++_navigationRevision;
        var item = _visibleItems[_index];
        ArtworkTitleText.Text = item.Title;
        ArtworkPositionText.Text = LocalizationService.Get(
            "ArtworkViewer.Position",
            _index + 1,
            _visibleItems.Count);
        PreviousButton.IsEnabled = _visibleItems.Count > 1;
        NextButton.IsEnabled = _visibleItems.Count > 1;
        RefreshCurrentSelectionUi(item);
        RefreshSelectionSummary();

        if (item.Image is not null)
        {
            ShowImage(item);
            return;
        }

        ArtworkImage.Source = null;
        if (_imageLoader is null || string.IsNullOrWhiteSpace(item.SourceLocation))
        {
            ShowLoadState(LocalizationService.Get("ArtworkViewer.LoadFailed"), string.Empty);
            return;
        }

        ShowLoadState(
            LocalizationService.Get("ArtworkViewer.Loading"),
            item.SourceLocation);
        try
        {
            _ = await EnsureImageLoadedAsync(item);
            if (!_closed && revision == _navigationRevision)
            {
                ShowImage(item);
            }
        }
        catch (OperationCanceledException) when (_closed)
        {
        }
        catch (Exception exception)
        {
            AppLog.Warning($"查看器图片载入失败：{item.SourceLocation}", exception);
            if (!_closed && revision == _navigationRevision)
            {
                ShowLoadState(
                    LocalizationService.Get("ArtworkViewer.LoadFailed"),
                    exception.Message);
            }
        }
    }

    private Task<ArtworkViewerLoadedImage> EnsureImageLoadedAsync(ArtworkViewerItem item)
    {
        if (item.Image is not null)
        {
            return Task.FromResult(new ArtworkViewerLoadedImage(
                item.Image,
                item.PixelWidth,
                item.PixelHeight));
        }

        item.LoadTask ??= LoadImageCoreAsync(item);
        return item.LoadTask;
    }

    private async Task<ArtworkViewerLoadedImage> LoadImageCoreAsync(ArtworkViewerItem item)
    {
        try
        {
            var loaded = await _imageLoader!(item, _lifetimeCancellation.Token);
            item.SetLoadedImage(loaded);
            RefreshThumbnailImage(item);
            return loaded;
        }
        catch
        {
            item.LoadTask = null;
            throw;
        }
    }

    private void StartPreloading()
    {
        var loadableItems = _items
            .Where(item => item.Image is null &&
                           !string.IsNullOrWhiteSpace(item.SourceLocation) &&
                           _imageLoader is not null)
            .OrderByDescending(item => item.IsLocal)
            .ToArray();
        _preloadTotal = loadableItems.Length;
        RefreshPreloadProgress();
        if (_preloadTotal > 0)
        {
            _ = PreloadImagesAsync(loadableItems);
        }
    }

    private async Task PreloadImagesAsync(IReadOnlyList<ArtworkViewerItem> items)
    {
        using var gate = new SemaphoreSlim(PreloadConcurrency);
        var tasks = items.Select(async item =>
        {
            try
            {
                await gate.WaitAsync(_lifetimeCancellation.Token);
                try
                {
                    await EnsureImageLoadedAsync(item);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException) when (_closed)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLog.Warning($"查看器后台预载失败：{item.SourceLocation}", exception);
            }

            if (!_closed)
            {
                _preloadCompleted++;
                RefreshPreloadProgress();
            }
        }).ToArray();
        await Task.WhenAll(tasks);
    }

    private void RefreshPreloadProgress()
    {
        PreloadProgressText.Visibility = _preloadTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
        PreloadProgressText.Text = LocalizationService.Get(
            "ArtworkViewer.PreloadProgress",
            _preloadCompleted,
            _preloadTotal);
    }

    private void ShowImage(ArtworkViewerItem item)
    {
        ArtworkImage.Source = item.Image;
        ArtworkImage.Visibility = Visibility.Visible;
        ArtworkLoadStatePanel.Visibility = Visibility.Collapsed;
        ArtworkLoadDetailText.Text = string.Empty;
        RefreshArtworkDimensions(item);
    }

    private void RefreshThumbnailImage(ArtworkViewerItem item)
    {
        if (!_closed && _thumbnails.TryGetValue(item, out var thumbnail))
        {
            thumbnail.Image.Source = item.Image;
            thumbnail.Container.Width = GetThumbnailWidth(item);
            thumbnail.Placeholder.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowLoadState(string message, string detail)
    {
        ArtworkDimensionsText.Visibility = Visibility.Collapsed;
        ArtworkLoadStateText.Text = message;
        ArtworkLoadDetailText.Text = detail;
        ArtworkLoadDetailText.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArtworkLoadStatePanel.Visibility = Visibility.Visible;
    }

    private void SetAllSamplesSelected(bool selected)
    {
        foreach (var item in _visibleItems.Where(item => item.IsSelectableSample && !item.IsLocal))
        {
            TrySetSampleSelection(item, selected);
        }

        if (_visibleItems.Count > 0)
        {
            RefreshCurrentSelectionUi(_visibleItems[_index]);
        }
        RefreshSelectionSummary();
    }

    private void SetAllVisibleLocalSelected(bool selected)
    {
        foreach (var item in _visibleItems.Where(item => item.CanDeleteLocal))
        {
            item.IsSelectedForDeletion = selected;
        }

        RefreshFilmstripSelection();
        RefreshLocalDeletionUi();
    }

    private void RefreshCurrentSelectionUi(ArtworkViewerItem item)
    {
        _updatingSelectionUi = true;
        try
        {
            CurrentSampleSelectionCheckBox.Visibility = item.IsSelectableSample && !item.IsLocal
                ? Visibility.Visible
                : Visibility.Collapsed;
            CurrentSampleSelectionCheckBox.Content = LocalizationService.Get("ArtworkViewer.IncludeSample");
            CurrentSampleSelectionCheckBox.IsChecked = item.IsSelected;
        }
        finally
        {
            _updatingSelectionUi = false;
        }

        RefreshFilmstripSelection();
    }

    private void RefreshArtworkSourceOptions()
    {
        _artworkSourceOptions.Clear();
        foreach (var item in _items.Where(item => item.IsArtworkSourceCandidate))
        {
            if (_artworkSourceOptions.Any(option => string.Equals(
                    option.Name,
                    item.ArtworkSourceName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _artworkSourceOptions.Add(new ArtworkSourceOption(
                item.ArtworkSourceName!,
                string.IsNullOrWhiteSpace(item.ArtworkSourceDisplayName)
                    ? item.ArtworkSourceName!
                    : item.ArtworkSourceDisplayName,
                item.ArtworkSourceDescription ?? string.Empty));
        }

        if (_artworkSourceOptions.Count == 0)
        {
            _selectedArtworkSourceName = null;
        }
        else if (!_artworkSourceOptions.Any(option => string.Equals(
                     option.Name,
                     _selectedArtworkSourceName,
                     StringComparison.OrdinalIgnoreCase)))
        {
            _selectedArtworkSourceName = _artworkSourceOptions[0].Name;
        }

        RefreshArtworkSourceSelector();
    }

    private void RefreshArtworkSourceSelector()
    {
        ArtworkSourceSelectorPanel.Visibility = _artworkSourceOptions.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_artworkSourceOptions.Count == 0)
        {
            return;
        }

        var selectedOption = _artworkSourceOptions.FirstOrDefault(option => string.Equals(
            option.Name,
            _selectedArtworkSourceName,
            StringComparison.OrdinalIgnoreCase)) ?? _artworkSourceOptions[0];
        ArtworkSourceSelectorButton.Content = $"{selectedOption.DisplayName} ▾";
        ArtworkSourceSelectorButton.IsEnabled = _artworkSourceOptions.Count > 1;
        ArtworkSourceSelectorButton.ToolTip = LocalizationService.Get(
            "ArtworkViewer.ArtworkSourceTooltip",
            _artworkSourceOptions.Count);
    }

    private void InitializeCurrentSourceSampleSelection()
    {
        EnsureSampleSelectionForSource(_selectedArtworkSourceName, selectAllWhenNew: false);
        SynchronizeSampleSelectionUi();
    }

    private void EnsureSampleSelectionForSource(string? sourceName, bool selectAllWhenNew)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || _sampleSelectionsBySource.ContainsKey(sourceName))
        {
            return;
        }

        var selectedLocations = _items
            .Where(item =>
                item.IsSelectableSample &&
                !item.IsLocal &&
                item.BelongsToSource(sourceName) &&
                (selectAllWhenNew || item.IsSelected) &&
                !string.IsNullOrWhiteSpace(item.SourceLocation))
            .Select(item => item.SourceLocation!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _sampleSelectionsBySource[sourceName] = selectedLocations;
    }

    private void SynchronizeSampleSelectionUi()
    {
        if (string.IsNullOrWhiteSpace(_selectedArtworkSourceName))
        {
            return;
        }

        EnsureSampleSelectionForSource(_selectedArtworkSourceName, selectAllWhenNew: false);
        var selectedLocations = _sampleSelectionsBySource[_selectedArtworkSourceName];
        foreach (var item in _items.Where(item => item.IsSelectableSample && !item.IsLocal))
        {
            item.IsSelected = item.BelongsToSource(_selectedArtworkSourceName) &&
                              !string.IsNullOrWhiteSpace(item.SourceLocation) &&
                              selectedLocations.Contains(item.SourceLocation);
        }
    }

    private IEnumerable<string> GetSelectedOnlineSampleLocations()
    {
        if (string.IsNullOrWhiteSpace(_selectedArtworkSourceName))
        {
            return _items
                .Where(item => item.IsSelectableSample && !item.IsLocal && item.IsSelected)
                .Select(item => item.SourceLocation!);
        }

        if (!_sampleSelectionsBySource.TryGetValue(_selectedArtworkSourceName, out var selectedLocations))
        {
            return [];
        }

        return _items
            .Where(item =>
                item.IsSelectableSample &&
                !item.IsLocal &&
                item.BelongsToSource(_selectedArtworkSourceName) &&
                !string.IsNullOrWhiteSpace(item.SourceLocation) &&
                selectedLocations.Contains(item.SourceLocation))
            .Select(item => item.SourceLocation!);
    }

    private IReadOnlyList<ArtworkViewerItem> GetSourceScopedItems()
    {
        if (_artworkSourceOptions.Count == 0 || string.IsNullOrWhiteSpace(_selectedArtworkSourceName))
        {
            return _items;
        }

        return _items
            .Where(item => item.IsLocal || item.BelongsToSource(_selectedArtworkSourceName))
            .ToArray();
    }

    private bool TrySetSampleSelection(ArtworkViewerItem item, bool selected)
    {
        if (item.IsSelected == selected)
        {
            return true;
        }

        item.IsSelected = selected;
        if (!item.IsLocal &&
            !string.IsNullOrWhiteSpace(item.SourceLocation) &&
            !string.IsNullOrWhiteSpace(_selectedArtworkSourceName) &&
            item.BelongsToSource(_selectedArtworkSourceName))
        {
            EnsureSampleSelectionForSource(_selectedArtworkSourceName, selectAllWhenNew: false);
            var selectedLocations = _sampleSelectionsBySource[_selectedArtworkSourceName];
            if (selected)
            {
                selectedLocations.Add(item.SourceLocation);
            }
            else
            {
                selectedLocations.Remove(item.SourceLocation);
            }
        }
        return true;
    }

    private bool ConfirmLocalDeletionWithDialog(IReadOnlyList<ArtworkViewerItem> items)
    {
        var message = items.Count == 1
            ? LocalizationService.Get(
                "ArtworkViewer.DeleteLocalConfirm",
                Path.GetFileName(items[0].SourceLocation))
            : LocalizationService.Get("ArtworkViewer.DeleteLocalConfirmMany", items.Count);
        var dialog = new AppDialogWindow(
            LocalizationService.Get("ArtworkViewer.DeleteLocalTitle"),
            message,
            LocalizationService.Get("ArtworkViewer.ConfirmDelete"))
        {
            Owner = this
        };
        return dialog.ShowDialog() == true;
    }

    private void RefreshArtworkDimensions(ArtworkViewerItem item)
    {
        if (item.PixelWidth <= 0 || item.PixelHeight <= 0)
        {
            ArtworkDimensionsText.Visibility = Visibility.Collapsed;
            return;
        }

        ArtworkDimensionsText.Text = LocalizationService.Get(
            "ArtworkViewer.Dimensions",
            item.PixelWidth,
            item.PixelHeight);
        ArtworkDimensionsText.Visibility = Visibility.Visible;
    }

    private static double GetThumbnailWidth(ArtworkViewerItem item)
    {
        if (item.PixelWidth <= 0 || item.PixelHeight <= 0)
        {
            return 112;
        }

        var aspectRatio = (double)item.PixelWidth / item.PixelHeight;
        return Math.Clamp((68 * aspectRatio) + 4, 56, 136);
    }

    private void RefreshSelectionSummary()
    {
        var sampleCount = _visibleItems.Count(item => item.IsSelectableSample && !item.IsLocal);
        var showOnlineSelection = sampleCount > 0;
        var visibility = showOnlineSelection ? Visibility.Visible : Visibility.Collapsed;
        SampleSelectionSummaryText.Visibility = visibility;
        SelectAllSamplesButton.Visibility = visibility;
        SelectNoSamplesButton.Visibility = visibility;
        ApplySelectionButton.Visibility = showOnlineSelection || _artworkSourceOptions.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        SampleSelectionSummaryText.Text = LocalizationService.Get(
            "ArtworkViewer.SelectionSummary",
            _visibleItems.Count(item => item.IsSelectableSample && !item.IsLocal && item.IsSelected),
            sampleCount);
    }

    private void RefreshFilmstripSections()
    {
        var localCount = _visibleItems.Count(item => item.IsLocal);
        var onlineCount = _visibleItems.Count - localCount;
        LocalThumbnailSection.Visibility = localCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        var showEmptyOnlineSource = onlineCount == 0 && _artworkSourceOptions.Count > 0;
        OnlineThumbnailSection.Visibility = onlineCount > 0 || showEmptyOnlineSource
            ? Visibility.Visible
            : Visibility.Collapsed;
        OnlineThumbnailSection.Margin = localCount > 0
            ? new Thickness(0, 8, 0, 0)
            : new Thickness(0);
        OnlineEmptySourceText.Visibility = showEmptyOnlineSource
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (showEmptyOnlineSource)
        {
            var selectedSource = _artworkSourceOptions.FirstOrDefault(option => string.Equals(
                option.Name,
                _selectedArtworkSourceName,
                StringComparison.OrdinalIgnoreCase));
            OnlineEmptySourceText.Text = LocalizationService.Get(
                "ArtworkViewer.NoOnlineCandidatesForSource",
                selectedSource?.DisplayName ?? _selectedArtworkSourceName ?? string.Empty);
        }
        LocalImagesHeaderText.Text = LocalizationService.Get("ArtworkViewer.LocalImagesHeader", localCount);
        OnlineImagesHeaderText.Text = LocalizationService.Get("ArtworkViewer.OnlineImagesHeader", onlineCount);
        ArtworkFilmstripSummaryText.Text = LocalizationService.Get(
            "ArtworkViewer.ImageListSummary",
            localCount,
            onlineCount);
        RefreshFilmstripDisclosure();
        RefreshLocalDeletionUi();
    }

    private void RefreshFilmstripDisclosure()
    {
        ArtworkFilmstripPanel.Visibility = _filmstripExpanded && _visibleItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ((RotateTransform)ArtworkFilmstripChevronPath.RenderTransform).Angle =
            _filmstripExpanded ? 90 : 0;
        ArtworkFilmstripToggleButton.ToolTip = LocalizationService.Get(
            _filmstripExpanded
                ? "ArtworkViewer.CollapseImageList"
                : "ArtworkViewer.ExpandImageList");
    }

    private void RefreshLocalDeletionUi()
    {
        var localCount = _visibleItems.Count(item => item.IsLocal);
        var deletableCount = _visibleItems.Count(item => item.CanDeleteLocal);
        var selectedCount = _items.Count(item => item.CanDeleteLocal && item.IsSelectedForDeletion);
        SelectAllLocalButton.IsEnabled = deletableCount > 0;
        SelectNoLocalButton.IsEnabled = selectedCount > 0;
        DeleteSelectedLocalButton.IsEnabled = selectedCount > 0;
        DeleteSelectedLocalButton.Content = LocalizationService.Get(
            "ArtworkViewer.DeleteSelectedLocal",
            selectedCount);
    }

    private static int IndexOfReference(IReadOnlyList<ArtworkViewerItem> items, ArtworkViewerItem target)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record ArtworkTypeFilterOption(ArtworkViewerItemKind? Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ArtworkSourceOption(string Name, string DisplayName, string Description);

    private sealed record ArtworkThumbnail(
        Border Container,
        Image Image,
        TextBlock Placeholder,
        CheckBox? SelectionBox);
}
