using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace JavMetaLite.App;

public partial class DuplicateMovieWindow : Window
{
    internal sealed record Candidate(BatchSavePreviewItem Item)
    {
        public string Description => $"{Item.Job.Metadata.Id} · {Size()}\n{Item.Job.VideoPath}";
        private string Size()
        {
            try { return $"{new FileInfo(Item.Job.VideoPath!).Length / 1048576d:N1} MiB"; }
            catch (IOException) { return "—"; }
            catch (UnauthorizedAccessException) { return "—"; }
        }
    }

    private readonly ObservableCollection<Candidate> _candidates;
    private readonly bool _canCombine;
    internal IReadOnlyList<BatchSavePreviewItem> ChosenItems { get; private set; } = [];
    internal bool AsParts { get; private set; }

    public DuplicateMovieWindow(IReadOnlyList<BatchSavePreviewItem> items)
    {
        _candidates = new(items.Select(item => new Candidate(item)));
        _canCombine = DuplicateMovieResolution.CanCombine(items);
        InitializeComponent();
        CandidatesList.ItemsSource = _candidates;
        ConfirmPartsCheckBox.IsEnabled = _canCombine;
        RefreshButtons();
        WindowVisualTheme.ApplyDarkTitleBar(this);
    }

    private void Selection_Changed(object sender, RoutedEventArgs e) => RefreshButtons();
    private void RefreshButtons()
    {
        if (SaveOneButton is null || SavePartsButton is null) return;
        SaveOneButton.IsEnabled = CandidatesList.SelectedItem is Candidate;
        SavePartsButton.IsEnabled = _canCombine && ConfirmPartsCheckBox.IsChecked == true;
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta)
    {
        var index = CandidatesList.SelectedIndex;
        if (index < 0 || index + delta < 0 || index + delta >= _candidates.Count) return;
        _candidates.Move(index, index + delta);
        CandidatesList.SelectedIndex = index + delta;
        ConfirmPartsCheckBox.IsChecked = false;
    }
    private void SaveOne_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatesList.SelectedItem is not Candidate candidate) return;
        ChosenItems = [candidate.Item];
        DialogResult = true;
    }
    private void SaveParts_Click(object sender, RoutedEventArgs e)
    {
        if (!_canCombine || ConfirmPartsCheckBox.IsChecked != true) return;
        ChosenItems = _candidates.Select(candidate => candidate.Item).ToArray();
        AsParts = true;
        DialogResult = true;
    }
    private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
