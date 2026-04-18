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

    public async Task DownloadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_current.State != UpdateState.UpdateAvailable || _lastInfo is null)
            {
                _logger.LogDebug("[sponsor/update] DownloadAsync no-op: state={State}", _current.State);
                return;
            }

            var info = _lastInfo;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _downloadCts = cts;

            int lastEmitted = -1;
            var progress = new SyncProgress(p =>
            {
                var clamped = Math.Clamp(p, 0, 100);
                if (clamped > lastEmitted)
                {
                    lastEmitted = clamped;
                    _current = UpdateStateMapping.FromDownloadProgress(
                        clamped, info.Version, info.ReleaseNotesUrl);
                    StateChanged?.Invoke(this, _current);
                }
            });

            try
            {
                await _manager.DownloadUpdatesAsync(info, progress, cts.Token).ConfigureAwait(false);
                if (lastEmitted < 100)
                {
                    _current = UpdateStateMapping.FromDownloadProgress(
                        100, info.Version, info.ReleaseNotesUrl);
                    StateChanged?.Invoke(this, _current);
                }
                Emit(UpdateStateMapping.FromDownloadComplete(info.Version, info.ReleaseNotesUrl));
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[sponsor/update] DownloadAsync cancelled");
                Emit(UpdateStateMapping.FromCancel(info.Version, info.ReleaseNotesUrl));
            }
            catch (UpdateCheckException uce)
            {
                _logger.LogWarning("[sponsor/update] DownloadAsync known failure: {Kind}", uce.Kind);
                Emit(UpdateStateMapping.FromError(uce, info.Version));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[sponsor/update] DownloadAsync unexpected failure");
                Emit(UpdateStateMapping.FromError(
                    new UpdateCheckException(UpdateErrorKind.ServerError, ex.Message, ex),
                    info.Version));
            }
            finally
            {
                _downloadCts = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ApplyAndRestartAsync() =>
        throw new NotImplementedException("Task 21");

    public Task DismissAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Task 22");

    public Task CancelDownloadAsync(CancellationToken ct = default)
    {
        _downloadCts?.Cancel();
        return Task.CompletedTask;
    }

    // Invokes the callback synchronously on the caller thread instead of
    // posting to a SynchronizationContext. Progress<T> would capture the
    // threadpool context in tests (no SC present), causing the handler to
    // run after DownloadUpdatesAsync returns and breaking progress-order
    // assertions. Synchronous dispatch keeps the fake's pump and the
    // driver's lastEmitted guard on the same thread.
    private sealed class SyncProgress : IProgress<int>
    {
        private readonly Action<int> _action;
        public SyncProgress(Action<int> action) { _action = action; }
        public void Report(int value) => _action(value);
    }

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
