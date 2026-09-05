using System.Windows.Threading;

namespace JavMetaLite.App;

// Property changes often arrive in groups. Keep row bindings immediate, but consolidate
// expensive view/statistics work at the next dispatcher turn or an explicit batch boundary.
internal sealed class QueueRefreshCoordinator(Dispatcher dispatcher, Action<bool> refresh)
{
    private DispatcherOperation? _pending;
    private bool _refreshView;

    public void Request(bool refreshView)
    {
        _refreshView |= refreshView;
        _pending ??= dispatcher.BeginInvoke(DispatcherPriority.Background, () => Flush());
    }

    public void Flush(bool refreshView = false)
    {
        var needsView = refreshView || _refreshView;
        Cancel();
        refresh(needsView);
    }

    public void Cancel()
    {
        _pending?.Abort();
        _pending = null;
        _refreshView = false;
    }
}
