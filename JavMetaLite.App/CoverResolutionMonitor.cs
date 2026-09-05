using System.Windows.Threading;
using JavMetaLite.Core.Services;

namespace JavMetaLite.App;

// Dispatcher-owned scheduling: two workers, no task per queued movie and no bitmap retention.
// Track only after an explicit successful search; loading a large local queue never starts HTTP checks.
internal sealed class CoverResolutionMonitor(
    Dispatcher dispatcher,
    Func<IReadOnlyList<string>, CancellationToken, Task<CoverResolutionCheck>> measure,
    Action changed) : IDisposable
{
    private sealed class Entry(MovieJob job, long revision, IReadOnlyList<string> locations)
    {
        public MovieJob Job { get; } = job;
        public long Revision { get; } = revision;
        public IReadOnlyList<string> Locations { get; } = locations;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Started { get; set; }
    }

    private readonly HashSet<MovieJob> _tracked = [];
    private readonly Dictionary<MovieJob, Entry> _entries = [];
    private DispatcherOperation? _scheduled;
    private int _running;
    private bool _paused;
    private bool _disposed;
    internal int PendingCount => _entries.Count;
    internal int RunningCount => _running;

    public void Track(MovieJob job)
    {
        if (_disposed) return;
        _tracked.Add(job);
        Refresh(job);
    }

    public void Refresh(MovieJob job)
    {
        if (_disposed || !_tracked.Contains(job)) return;
        if (job.SearchState != MovieSearchState.NeedsReview)
        {
            RemoveEntry(job);
            changed();
            return;
        }
        if (_entries.TryGetValue(job, out var old) && old.Revision == job.CoverResolutionRevision) return;
        RemoveEntry(job);
        if (job.SearchState == MovieSearchState.NeedsReview && job.CoverResolution is null)
        {
            var locations = CoverResolutionCheck.GetLocations(job.ArtworkReview.SelectedCandidate);
            if (locations.Count > 0)
                _entries[job] = new(job, job.CoverResolutionRevision, locations);
        }
        Schedule();
        changed();
    }

    public void Forget(MovieJob job)
    {
        _tracked.Remove(job);
        RemoveEntry(job);
        changed();
    }

    public void Observe(MovieJob job, long revision, CoverResolutionCheck resolution)
    {
        // A preview already has the intrinsic dimensions. Do not re-read it or allow a late
        // background result to overwrite the image the user just saw. No online tracking is added.
        if (_disposed || !job.TrySetCoverResolution(revision, resolution)) return;
        RemoveEntry(job);
        changed();
    }

    public void Pause(bool paused)
    {
        _paused = paused;
        if (!paused) Schedule();
    }

    public void CancelAll()
    {
        // Explicit cancellation stays canceled until another search or source change.
        foreach (var job in _entries.Keys.ToArray()) RemoveEntry(job);
        _scheduled?.Abort();
        _scheduled = null;
        changed();
    }

    private void RemoveEntry(MovieJob job)
    {
        if (!_entries.Remove(job, out var entry)) return;
        entry.Cancellation.Cancel();
        if (!entry.Started) entry.Cancellation.Dispose();
    }

    private void Schedule()
    {
        if (_disposed || _paused || _scheduled is not null) return;
        _scheduled = dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scheduled = null;
            if (_disposed || _paused) return;
            foreach (var entry in _entries.Values.Where(e => !e.Started).Take(Math.Max(0, 2 - _running)).ToArray())
            {
                entry.Started = true;
                _running++;
                _ = RunAsync(entry);
            }
        });
    }

    private async Task RunAsync(Entry entry)
    {
        try
        {
            // Includes decoding on a worker, not the UI thread. All job changes resume on the dispatcher.
            var result = await Task.Run(() => measure(entry.Locations, entry.Cancellation.Token));
            if (!_disposed && !entry.Cancellation.IsCancellationRequested &&
                _entries.TryGetValue(entry.Job, out var current) && ReferenceEquals(current, entry))
                entry.Job.TrySetCoverResolution(entry.Revision, result);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed && !entry.Cancellation.IsCancellationRequested)
            {
                AppLog.Warning("后台封套尺寸检查失败（不影响搜索或保存）", exception);
                entry.Job.TrySetCoverResolution(entry.Revision, new(string.Empty, 0, 0));
            }
        }
        finally
        {
            if (_entries.TryGetValue(entry.Job, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(entry.Job);
            entry.Cancellation.Dispose();
            _running--;
            if (!_disposed) { changed(); Schedule(); }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelAll();
        _tracked.Clear();
    }
}
