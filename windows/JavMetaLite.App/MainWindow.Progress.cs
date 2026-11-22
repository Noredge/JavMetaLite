using System.Windows;
using JavMetaLite.Core.Models;

namespace JavMetaLite.App;

public partial class MainWindow
{
    private async Task ShowActivityIfStillBusyAsync(CancellationTokenSource operation)
    {
        // Quick refreshes should finish without moving the footer controls.
        await Task.Delay(300);
        if (_activeOperationCancellation == operation && OperationProgressBar.IsIndeterminate)
            OperationProgressPanel.Visibility = Visibility.Visible;
    }

    private void ShowOverallProgress(int percent)
    {
        OperationProgressBar.IsIndeterminate = false;
        OperationProgressBar.Value = Math.Clamp(percent, 0, 100);
        ProgressPercent.Visibility = Visibility.Visible;
        OperationProgressPanel.Visibility = Visibility.Visible;
    }

    private IProgress<int> BeginSearchProgress()
    {
        var operation = _activeOperationCancellation;
        ShowOverallProgress(0);
        return new Progress<int>(percent =>
        {
            if (operation == _activeOperationCancellation && operation?.IsCancellationRequested == false &&
                percent > OperationProgressBar.Value)
                ShowOverallProgress(percent);
        });
    }

    // Save transactions run sequentially, including the parts of a multipart movie.
    private sealed class SaveProgress
    {
        private readonly MainWindow _owner;
        private readonly double _totalWork;
        private readonly int _planCount;
        private double _finishedWork;
        private int _finishedPlans;

        internal SaveProgress(MainWindow owner, IReadOnlyList<SavePlan> plans)
        {
            _owner = owner;
            _planCount = plans.Count;
            _totalWork = plans.Sum(PlannedWork);
            if (_totalWork > 0 || _planCount > 1) owner.ShowOverallProgress(0);
        }

        // Full verification reads the staged target and then rechecks the source before retirement.
        private static double PlannedWork(SavePlan plan) => plan.CrossVolumeVideoBytes *
            (plan.OrganizationOptions.CrossVolumeVerification == CrossVolumeVerificationMode.FullSha256 ? 3d : 1d);

        internal async Task<OrganizedSaveResult> ExecuteAsync(SavePlan plan, MovieMetadata metadata,
            bool allowOverwrite, CancellationToken token, bool batch = false)
        {
            var planned = PlannedWork(plan);
            double processed = 0;
            var active = true;
            var updates = new Progress<FileTransactionProgress>(update =>
            {
                if (!active || token.IsCancellationRequested) return;
                processed += update.BytesAdvanced;
                if (_totalWork > 0 && update.BytesAdvanced > 0)
                {
                    var completed = _finishedWork + Math.Min(processed, planned);
                    _owner.ShowOverallProgress(Math.Min(99, (int)(100 * completed / _totalWork)));
                }
                var stage = GetLocalizedTransactionProgress(update);
                var message = batch ? LocalizationService.Get("Status.BatchSaveProgress", metadata.Id, stage) : stage;
                if (message != _owner.StatusText.Text) _owner.SetStatus(message, null);
            });
            try
            {
                var result = await _owner._fileOrganizationService.ExecuteAsync(plan, metadata, allowOverwrite, token, updates);
                _finishedWork += planned;
                _finishedPlans++;
                // Even when all bytes have been written, commit and cleanup may still be pending.
                var percent = _totalWork > 0 ? 100 * _finishedWork / _totalWork : 100d * _finishedPlans / _planCount;
                _owner.ShowOverallProgress(_finishedPlans == _planCount ? 100 : Math.Min(99, (int)percent));
                return result;
            }
            finally { active = false; }
        }
    }
}
