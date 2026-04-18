using System;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Sponsor.Auth;
using Ghostty.Core.Sponsor.Update.Mapping;
using Microsoft.Extensions.Logging;

namespace Ghostty.Core.Sponsor.Update;

/// <summary>
/// Production <see cref="IUpdateDriver"/> backed by Velopack. Wraps
/// <see cref="IVelopackManager"/> (a thin shim over Velopack's
/// UpdateManager) so the state machine is independently testable.
/// Public methods never throw: failures become Error snapshots via
/// <see cref="UpdateStateMapping.FromError"/>. Spec section 5.4.
/// </summary>
internal sealed partial class VelopackUpdateDriver : IUpdateDriver, IDisposable
{
    private readonly IVelopackManager _manager;
    private readonly ISponsorTokenProvider _tokens;
    private readonly ILogger<VelopackUpdateDriver> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UpdateStateSnapshot _current = UpdateStateSnapshot.Idle();
    private CancellationTokenSource? _downloadCts;

    // Snapshot of the last known update so cancel/restart transitions
    // can preserve Version + ReleaseNotesUrl across state hops.
    private VelopackUpdateInfo? _lastInfo;

    public VelopackUpdateDriver(
        IVelopackManager manager,
        ISponsorTokenProvider tokens,
        ILogger<VelopackUpdateDriver> logger)
    {
        _manager = manager;
        _tokens = tokens;
        _logger = logger;
        _tokens.TokenInvalidated += OnTokenInvalidated;
    }

    public UpdateStateSnapshot Current => _current;
    public event EventHandler<UpdateStateSnapshot>? StateChanged;

    public Task CheckAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Task 18");

    public Task DownloadAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Task 19");

    public Task ApplyAndRestartAsync() =>
        throw new NotImplementedException("Task 21");

    public Task DismissAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Task 22");

    public Task CancelDownloadAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Task 20");

    private void OnTokenInvalidated(object? sender, EventArgs e) { /* Task 22 */ }

    private void Emit(UpdateStateSnapshot snap)
    {
        _current = snap;
        StateChanged?.Invoke(this, snap);
    }

    public void Dispose()
    {
        _tokens.TokenInvalidated -= OnTokenInvalidated;
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _gate.Dispose();
    }
}
