using System;
using System.Threading;
using Ghostty.Core.Power;
using Microsoft.Extensions.Logging;
using Windows.System.Power;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Ghostty.Power;

/// <summary>
/// Production <see cref="IPowerStateMonitor"/>. Observes WinRT
/// <see cref="PowerManager"/> and <see cref="UISettings"/> events plus
/// <c>GetSystemMetrics(SM_REMOTESESSION)</c> for RDP, feeds the signals
/// through <see cref="PowerStateResolver"/>, debounces bursts, and
/// raises <see cref="LowPowerChanged"/> only when the resolved bool
/// flips.
/// </summary>
internal sealed class WindowsPowerStateMonitor : IPowerStateMonitor, IDisposable
{
    // Debounce window for coalescing bursts. A mode flip, a battery
    // unplug, and a transparency toggle triggered by the OS power
    // profile can all land within a few ms of each other; 150 ms is
    // long enough to coalesce them without being user-perceptible.
    private const int DebounceMs = 150;

    private readonly Func<PowerSaverMode> _readMode;
    private readonly ILogger<WindowsPowerStateMonitor> _logger;
    private readonly Lock _gate = new();
    private readonly UISettings _uiSettings = new();

    private Timer? _debounceTimer;
    private bool _started;
    private bool _disposed;

    // Last-known OS signals. Mutated under _gate on the event-source
    // threads (PowerManager events fire on a thread-pool thread); read
    // under _gate by the debounce callback.
    private bool _batterySaverOn;
    private bool _onBattery;
    private bool _transparencyEffectsOff;
    private bool _remoteSession;

    public bool IsLowPowerActive { get; private set; }
    public PowerSaverTrigger ActiveTriggers { get; private set; }

    public event EventHandler? LowPowerChanged;

    public WindowsPowerStateMonitor(
        Func<PowerSaverMode> readMode,
        ILogger<WindowsPowerStateMonitor> logger)
    {
        _readMode = readMode;
        _logger = logger;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        // Seed state outside the lock: these accessors hit WinRT and
        // can block briefly on the first call. No other thread can
        // observe the fields yet because no event handler is wired up.
        _batterySaverOn = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        _onBattery =
            PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent &&
            PowerManager.BatteryStatus != BatteryStatus.NotPresent;
        _transparencyEffectsOff = !_uiSettings.AdvancedEffectsEnabled;
        _remoteSession = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;

        PowerManager.EnergySaverStatusChanged += OnPowerSignalChanged;
        PowerManager.BatteryStatusChanged += OnPowerSignalChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerSignalChanged;
        _uiSettings.AdvancedEffectsEnabledChanged += OnUiSettingsChanged;

        // Publish the seeded state. Consumers subscribed before Start()
        // will get a LowPowerChanged if the initial resolution is
        // active (previous IsLowPowerActive is false by default).
        ResolveAndMaybeEmit();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;

            PowerManager.EnergySaverStatusChanged -= OnPowerSignalChanged;
            PowerManager.BatteryStatusChanged -= OnPowerSignalChanged;
            PowerManager.PowerSupplyStatusChanged -= OnPowerSignalChanged;
            _uiSettings.AdvancedEffectsEnabledChanged -= OnUiSettingsChanged;

            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    /// <summary>
    /// Called by the Win32 message pump when it sees
    /// <c>WM_WTSSESSION_CHANGE</c>. Re-reads the RDP signal and
    /// schedules a resolve.
    /// </summary>
    public void OnSessionChanged()
    {
        _remoteSession = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;
        ScheduleResolve();
    }

    /// <summary>
    /// Called after <see cref="Ghostty.Services.IConfigService"/>
    /// raises ConfigChanged. The mode thunk will now return a
    /// potentially different value; re-resolve so the active flag
    /// reflects the new policy.
    /// </summary>
    public void OnConfigReloaded() => ScheduleResolve();

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
        }
    }

    // WinRT PowerManager static events project as EventHandler<object>
    // with a nullable sender. The three signals we subscribe to share
    // this shape, so one method handles all of them.
    private void OnPowerSignalChanged(object? sender, object e)
    {
        _batterySaverOn = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        _onBattery =
            PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent &&
            PowerManager.BatteryStatus != BatteryStatus.NotPresent;
        ScheduleResolve();
    }

    private void OnUiSettingsChanged(
        UISettings sender,
        UISettingsAdvancedEffectsEnabledChangedEventArgs args)
    {
        _transparencyEffectsOff = !sender.AdvancedEffectsEnabled;
        ScheduleResolve();
    }

    private void ScheduleResolve()
    {
        lock (_gate)
        {
            if (!_started) return;
            // Lazy-allocate the timer so test harnesses that create a
            // monitor but never Start it don't pay the System.Threading
            // allocation. Period is infinite - we only want one shot
            // per burst.
            _debounceTimer ??= new Timer(_ => ResolveAndMaybeEmit(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private void ResolveAndMaybeEmit()
    {
        bool previousActive;
        bool nextActive;
        PowerSaverTrigger nextTriggers;

        lock (_gate)
        {
            previousActive = IsLowPowerActive;
            (nextActive, nextTriggers) = PowerStateResolver.Resolve(
                mode:                   _readMode(),
                batterySaverOn:         _batterySaverOn,
                onBattery:              _onBattery,
                transparencyEffectsOff: _transparencyEffectsOff,
                remoteSession:          _remoteSession);

            IsLowPowerActive = nextActive;
            ActiveTriggers   = nextTriggers;
        }

        // Raise the event outside the lock. Subscribers run arbitrary
        // code (title-bar glyph update, renderer re-eval) and must not
        // observe us holding _gate, or a re-entrant Stop/Dispose call
        // from a handler would deadlock.
        if (nextActive != previousActive)
        {
            _logger.LogInformation(
                "Power-saver mode {State} (triggers={Triggers})",
                nextActive ? "active" : "inactive",
                nextTriggers);
            LowPowerChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
