using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

public partial class MovieInputPicker : Window
{
    private readonly Dictionary<string, Entry> _picked = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Entry> _directoryRows = [];
    private CancellationTokenSource? _navigation;
    private bool _showSelection;
    private bool _syncDrive;
    private bool _loading;
    private string? _listingError;
    public string CurrentDirectory { get; private set; }
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];

    public MovieInputPicker(string initialDirectory)
    {
        CurrentDirectory = initialDirectory;
        InitializeComponent();
        WindowVisualTheme.Attach(this);
        DriveBox.ItemsSource = Environment.GetLogicalDrives();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) =>
        await NavigateAsync(CurrentDirectory);

    private void Window_Closed(object? sender, EventArgs e) => _navigation?.Cancel();

    internal async Task NavigateAsync(string input)
    {
        var prior = _navigation;
        prior?.Cancel();
        using var request = new CancellationTokenSource();
        _navigation = request;
        _showSelection = false;
        _loading = true;
        _listingError = null;
        _directoryRows = [];
        RefreshView();
        try
        {
            var path = Path.GetFullPath(input.Trim().Trim('"'), CurrentDirectory);
            path = Path.TrimEndingDirectorySeparator(path);
            var result = await Task.Run(() => ListDirectory(path, request.Token), request.Token);
            if (request.IsCancellationRequested) return;
            CurrentDirectory = result.Folder;
            _directoryRows = result.Rows;
            if (result.File is { } file) _picked.TryAdd(file.FullPath, file);
            AddressBox.Text = CurrentDirectory;
            _syncDrive = true;
            try { DriveBox.SelectedItem = Path.GetPathRoot(CurrentDirectory); }
            finally { _syncDrive = false; }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!request.IsCancellationRequested)
                _listingError = LocalizationService.Get("Intake.ReadFailed", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_navigation, request))
            {
                _navigation = null;
                _loading = false;
                RefreshView();
            }
        }
    }

    private static (string Folder, List<Entry> Rows, Entry? File) ListDirectory(string path, CancellationToken token)
    {
        Entry? file = null;
        if (VideoFileSupport.IsSupportedExistingFile(path))
        {
            file = new Entry(path, false);
            path = Path.GetDirectoryName(path)!;
        }
        var rows = new List<Entry>();
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            var isFolder = item is DirectoryInfo;
            if (isFolder || VideoFileSupport.HasSupportedExtension(item.Name))
                rows.Add(new Entry(item.FullName, isFolder));
        }
        rows.Sort((a, b) => a.IsFolder == b.IsFolder
            ? StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name)
            : b.IsFolder.CompareTo(a.IsFolder));
        return (path, rows, file);
    }

    private void RefreshView()
    {
        var visible = _showSelection ? _picked.Values.ToArray() : _directoryRows;
        foreach (var entry in visible) entry.IsPicked = _picked.ContainsKey(entry.FullPath);
        EntriesList.ItemsSource = visible;
        PickPageBox.Visibility = _showSelection ? Visibility.Collapsed : Visibility.Visible;
        PickPageBox.IsEnabled = !_loading && !_showSelection && visible.Count > 0;
        var checkedHere = visible.Count(row => row.IsPicked);
        PickPageBox.IsChecked = checkedHere == 0 ? false : checkedHere == visible.Count ? true : null;
        BrowserStatus.Text = _showSelection ? string.Empty : _loading ? LocalizationService.Get("Discovery.Scanning") :
            _listingError ?? (visible.Count == 0 ? LocalizationService.Get("Intake.Empty") : string.Empty);
        UpButton.IsEnabled = !_showSelection && Directory.GetParent(CurrentDirectory) != null;
        PickCurrentButton.IsEnabled = !_loading && !_showSelection && _listingError == null;
        PickCurrentButton.SetResourceReference(ContentProperty, _picked.ContainsKey(CurrentDirectory) ? "Intake.UnselectCurrent" : "Intake.CurrentFolder");
        AddButton.IsEnabled = !_loading && _picked.Count > 0;
        var folders = _picked.Values.Count(e => e.IsFolder);
        SelectionViewButton.Content = _showSelection ? LocalizationService.Get("Intake.Back") :
            LocalizationService.Get("Intake.Count", folders, _picked.Count - folders);
        SelectionViewButton.IsEnabled = _showSelection || _picked.Count > 0;
    }

    private void Toggle(Entry entry)
    {
        if (!_picked.Remove(entry.FullPath)) _picked.Add(entry.FullPath, entry);
        entry.IsPicked = _picked.ContainsKey(entry.FullPath);
        RefreshView();
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: Entry entry }) Toggle(entry);
    }

    private void SetDirectorySelection(bool select)
    {
        foreach (var row in _directoryRows)
        {
            if (select) _picked.TryAdd(row.FullPath, row);
            else _picked.Remove(row.FullPath);
        }
        RefreshView();
    }

    private void PickPage_Click(object sender, RoutedEventArgs e) =>
        SetDirectorySelection(_directoryRows.Any(row => !row.IsPicked));

    private void PickCurrent_Click(object sender, RoutedEventArgs e) =>
        Toggle(new Entry(CurrentDirectory, true));

    private void SelectionView_Click(object sender, RoutedEventArgs e)
    {
        _showSelection = !_showSelection;
        RefreshView();
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Entry { IsFolder: true } row }) await NavigateAsync(row.FullPath);
    }

    private async void Entries_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin ||
            ItemsControl.ContainerFromElement(EntriesList, origin) is not ListBoxItem { Content: Entry row }) return;
        if (origin is FrameworkElement element && (element.TemplatedParent is Button or CheckBox || element is Button or CheckBox)) return;
        e.Handled = true;
        if (row.IsFolder) await NavigateAsync(row.FullPath);
        else Toggle(row);
    }

    private async void Entries_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_showSelection && !_loading && e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SetDirectorySelection(true);
            return;
        }
        if (e.OriginalSource is not ListBoxItem { Content: Entry row }) return;
        if (e.Key == Key.Space) { e.Handled = true; Toggle(row); }
        else if (e.Key == Key.Enter && row.IsFolder) { e.Handled = true; await NavigateAsync(row.FullPath); }
    }

    private async void Drive_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncDrive && DriveBox.SelectedItem is string drive) await NavigateAsync(drive);
    }

    private async void Go_Click(object sender, RoutedEventArgs e) => await NavigateAsync(AddressBox.Text);

    private async void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await NavigateAsync(AddressBox.Text);
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.GetParent(CurrentDirectory) is { } parent) await NavigateAsync(parent.FullName);
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _picked.Count == 0) return;
        SelectedPaths = _picked.Keys.ToArray();
        DialogResult = true;
    }

    internal sealed class Entry(string fullPath, bool isFolder) : INotifyPropertyChanged
    {
        private bool _picked;
        public string FullPath { get; } = fullPath;
        public string Name => Path.GetFileName(FullPath) is { Length: > 0 } name ? name : FullPath;
        public bool IsFolder { get; } = isFolder;
        public bool IsPicked
        {
            get => _picked;
            set
            {
                if (_picked == value) return;
                _picked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPicked)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
