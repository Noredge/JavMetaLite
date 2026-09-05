using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

public partial class MovieDiscoveryWindow : Window
{
    private readonly string[] _inputPaths;
    private readonly HashSet<string> _existingVideoPaths;
    private MovieInputDiscoveryResult? _initialResult;
    private CancellationTokenSource? _scanCancellation;
    private bool _loaded;

    public MovieDiscoveryWindow(string rootPath, IEnumerable<string> existingVideoPaths)
        : this([rootPath], existingVideoPaths, includeSubdirectories: true)
    {
    }

    public MovieDiscoveryWindow(
        IEnumerable<string> inputPaths,
        IEnumerable<string> existingVideoPaths,
        bool includeSubdirectories,
        MovieInputDiscoveryResult? initialResult = null)
    {
        _inputPaths = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_inputPaths.Length == 0)
        {
            throw new ArgumentException("至少需要一个影片文件或文件夹。", nameof(inputPaths));
        }

        _existingVideoPaths = existingVideoPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _initialResult = initialResult;
        InitializeComponent();
        WindowVisualTheme.ApplyDarkTitleBar(this);
        IncludeSubdirectoriesCheckBox.IsChecked = includeSubdirectories;
        RootPathText.Text = string.Join(Environment.NewLine, _inputPaths);
        RootPathText.ToolTip = RootPathText.Text;
        MovieGroupsList.ItemsSource = Items;
    }

    public ObservableCollection<MovieDiscoveryItem> Items { get; } = [];

    public IReadOnlyList<string> SelectedVideoPaths { get; private set; } = [];

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        if (_initialResult is not null &&
            _initialResult.IncludedSubdirectories == (IncludeSubdirectoriesCheckBox.IsChecked == true))
        {
            ApplyDiscovery(_initialResult);
            _initialResult = null;
            return;
        }

        _initialResult = null;
        await RefreshDiscoveryAsync();
    }

    private async void IncludeSubdirectories_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            _initialResult = null;
            await RefreshDiscoveryAsync();
        }
    }

    private async Task RefreshDiscoveryAsync()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        AddButton.IsEnabled = false;
        SelectAllButton.IsEnabled = false;
        SelectNoneButton.IsEnabled = false;
        Items.Clear();
        DiagnosticsText.Text = string.Empty;
        SummaryText.Text = LocalizationService.Get("Discovery.Scanning");

        try
        {
            var result = await MovieInputDiscovery.DiscoverAsync(
                _inputPaths,
                IncludeSubdirectoriesCheckBox.IsChecked == true,
                cancellation.Token);
            if (!ReferenceEquals(_scanCancellation, cancellation))
            {
                return;
            }

            ApplyDiscovery(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SelectedVideoPaths = [];
            SummaryText.Text = LocalizationService.Get("Discovery.FailedSummary");
            DiagnosticsText.Text = LocalizationService.Get("Discovery.ReadFailed");
            DiagnosticsText.ToolTip = exception.Message;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedVideoPaths = Items
            .Where(item => item.CanSelect && item.IsSelected)
            .SelectMany(item => item.FileSet.VideoPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (SelectedVideoPaths.Count > 0)
        {
            DialogResult = true;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(item => item.CanSelect))
        {
            item.IsSelected = true;
        }

        UpdateSelectionState();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(item => item.CanSelect))
        {
            item.IsSelected = false;
        }

        UpdateSelectionState();
    }

    private void ItemSelection_Changed(object sender, RoutedEventArgs e) =>
        UpdateSelectionState();

    private void ApplyDiscovery(MovieInputDiscoveryResult result)
    {
        Items.Clear();
        foreach (var fileSet in result.MovieFileSets)
        {
            var isDuplicate = fileSet.VideoPaths.All(_existingVideoPaths.Contains);
            Items.Add(new MovieDiscoveryItem(fileSet, isDuplicate));
        }

        var availableCount = Items.Count(item => item.CanSelect);
        var duplicateCount = Items.Count - availableCount;
        SummaryText.Text = LocalizationService.Get(
            "Discovery.Summary",
            Items.Count,
            availableCount,
            duplicateCount,
            result.IgnoredFileCount,
            result.SkippedDirectoryCount);
        DiagnosticsText.Text = result.Diagnostics.Count == 0
            ? string.Empty
            : LocalizationService.Get("Discovery.Diagnostics", result.Diagnostics.Count);
        DiagnosticsText.ToolTip = result.Diagnostics.Count == 0
            ? null
            : string.Join(Environment.NewLine, result.Diagnostics.Select(DiscoveryDiagnosticText.Format));
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var hasSelectableItems = Items.Any(item => item.CanSelect);
        AddButton.IsEnabled = Items.Any(item => item.CanSelect && item.IsSelected);
        SelectAllButton.IsEnabled = hasSelectableItems;
        SelectNoneButton.IsEnabled = hasSelectableItems;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }
}

public sealed class MovieDiscoveryItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public MovieDiscoveryItem(MovieFileSet fileSet, bool isDuplicate)
    {
        FileSet = fileSet;
        CanSelect = !isDuplicate;
        _isSelected = CanSelect;
        var id = MovieIdParser.TryExtract(fileSet.MovieBaseName);
        DisplayName = id ?? fileSet.MovieBaseName;
        Detail = fileSet.Parts.Count > 1
            ? LocalizationService.Get("Discovery.PartCount", fileSet.Parts.Count)
            : Path.GetFileName(fileSet.PrimaryPath);
        StatusText = isDuplicate
            ? LocalizationService.Get("Discovery.Status.Duplicate")
            : id is null
                ? LocalizationService.Get("Discovery.Status.NeedsId")
                : LocalizationService.Get("Discovery.Status.Ready");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MovieFileSet FileSet { get; }

    public string DisplayName { get; }

    public string Detail { get; }

    public string PrimaryPath => FileSet.PrimaryPath;

    public string StatusText { get; }

    public bool CanSelect { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value || !CanSelect)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
