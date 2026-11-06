using System.Diagnostics;
using System.Globalization;

namespace JavMetaLite.Core.Services;

// One summary per metadata/transaction operation, not one disk log write per image.
// Stages measure elapsed wall time, not CPU time or summed concurrent-request time.
internal sealed class SaveTimingLog(string area, string id) : IDisposable
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, double> _stages = [];
    private long _stageStarted;
    private string? _stage;
    private bool _completed;

    public void Begin(string stage)
    {
        EndStage();
        _stage = stage;
        _stageStarted = Stopwatch.GetTimestamp();
    }

    public void Complete() => _completed = true;

    private void EndStage()
    {
        if (_stage is not null)
            _stages[_stage] = _stages.GetValueOrDefault(_stage) +
                Stopwatch.GetElapsedTime(_stageStarted).TotalMilliseconds;
    }

    public void Dispose()
    {
        EndStage();
        var phases = string.Join(" ", _stages.Select(pair =>
            $"{pair.Key}Ms={pair.Value.ToString("F2", CultureInfo.InvariantCulture)}"));
        AppLog.Info($"保存耗时 area={area} id={id} result={(_completed ? "completed" : "incomplete")} " +
            $"totalMs={Stopwatch.GetElapsedTime(_started).TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} {phases}");
    }
}
