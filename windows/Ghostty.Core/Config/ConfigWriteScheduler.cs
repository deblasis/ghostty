using System;
using System.Collections.Generic;

namespace Ghostty.Core.Config;

/// <summary>
/// Default <see cref="IConfigWriteScheduler"/> implementation.
/// Thread-safe: <see cref="Schedule"/> and <see cref="Flush"/> may
/// be called from any thread, but the flush callback runs on the
/// timer's thread (threadpool for the production timer). The
/// post-flush <c>onFlushed</c> callback is the hook the Windows
/// host uses to invoke <see cref="IConfigService.Reload"/> on the
/// dispatcher queue.
/// </summary>
public sealed class ConfigWriteScheduler : IConfigWriteScheduler
{
    private readonly IConfigFileEditor _editor;
    private readonly ISchedulerTimer _timer;
    private readonly TimeSpan _debounce;
    private readonly Action _onFlushed;
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private bool _disposed;

    public ConfigWriteScheduler(
        IConfigFileEditor editor,
        ISchedulerTimer timer,
        TimeSpan debounce,
        Action onFlushed)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(onFlushed);

        _editor = editor;
        _timer = timer;
        _debounce = debounce;
        _onFlushed = onFlushed;
        _timer.Callback = FlushFromTimer;
    }

    public void Schedule(string key, string value)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _pending[key] = value;       // last write wins
            _timer.Schedule(_debounce);  // rearm trailing-edge timer
        }
    }

    public void Flush()
    {
        List<KeyValuePair<string, string>> snapshot;
        lock (_lock)
        {
            if (_pending.Count == 0) { _timer.Cancel(); return; }
            _timer.Cancel();
            snapshot = new List<KeyValuePair<string, string>>(_pending);
            _pending.Clear();
        }
        WriteAndSignal(snapshot);
    }

    private void FlushFromTimer()
    {
        List<KeyValuePair<string, string>> snapshot;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            snapshot = new List<KeyValuePair<string, string>>(_pending);
            _pending.Clear();
        }
        WriteAndSignal(snapshot);
    }

    private void WriteAndSignal(List<KeyValuePair<string, string>> batch)
    {
        foreach (var kv in batch)
            _editor.SetValue(kv.Key, kv.Value);
        _onFlushed();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Flush();
        _timer.Dispose();
    }
}
