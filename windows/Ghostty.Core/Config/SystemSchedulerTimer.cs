using System;
using System.Threading;

namespace Ghostty.Core.Config;

/// <summary>
/// Real-time <see cref="ISchedulerTimer"/> backed by
/// <see cref="System.Threading.Timer"/>. The callback runs on a
/// threadpool thread; the caller is responsible for marshaling onto
/// the UI thread if needed.
/// </summary>
public sealed class SystemSchedulerTimer : ISchedulerTimer
{
    private readonly Timer _timer;

    public SystemSchedulerTimer()
    {
        _timer = new Timer(_ => Callback?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public Action? Callback { get; set; }

    public void Schedule(TimeSpan delay)
        => _timer.Change(delay, Timeout.InfiniteTimeSpan);

    public void Cancel()
        => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose() => _timer.Dispose();
}
