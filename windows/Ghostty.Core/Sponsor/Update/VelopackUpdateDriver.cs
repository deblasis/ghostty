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

    public async Task CheckAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var token = await _tokens.GetTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[sponsor/update] CheckAsync aborted: no JWT");
                Emit(UpdateStateMapping.FromError(
                    new UpdateCheckException(UpdateErrorKind.NoToken, "no JWT"),
                    targetVersion: _lastInfo?.Version));
                return;
            }

            VelopackUpdateInfo? info;
            try
            {
                info = await _manager.CheckForUpdatesAsync(ct).ConfigureAwait(false);
            }
            catch (UpdateCheckException uce)
            {
                _logger.LogWarning("[sponsor/update] CheckAsync known failure: {Kind}", uce.Kind);
                Emit(UpdateStateMapping.FromError(uce, targetVersion: _lastInfo?.Version));
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[sponsor/update] CheckAsync unexpected failure");
                Emit(UpdateStateMapping.FromError(
                    new UpdateCheckException(UpdateErrorKind.ServerError, ex.Message, ex),
                    targetVersion: _lastInfo?.Version));
                return;
            }

            _lastInfo = info;
            Emit(UpdateStateMapping.FromCheckResult(info));
        }
        finally
        {
            _gate.Release();
        }
    }

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
