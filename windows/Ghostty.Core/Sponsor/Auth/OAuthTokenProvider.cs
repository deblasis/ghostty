using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ghostty.Core.Sponsor.Auth;

/// <summary>
/// Production <see cref="ISponsorTokenProvider"/> backed by the GitHub
/// OAuth loopback flow and a DPAPI-encrypted JWT cache. Replaces
/// <see cref="EnvTokenProvider"/> in the DI root for SPONSOR_BUILD
/// output, with WINTTY_DEV_JWT preserved as a short-circuit
/// override for dev smoke paths.
/// </summary>
internal sealed class OAuthTokenProvider : ISponsorTokenProvider, IDisposable
{
    private readonly WinttyAuthClient _auth;
    private readonly IJwtStore _store;
    private readonly IBrowserLauncher _browser;
    private readonly ILoopbackListener _listener;
    private readonly Uri _apiBase;
    private readonly ILogger<OAuthTokenProvider> _logger;
    private readonly TimeProvider _time;
    private readonly Func<string, string?> _envVarLookup;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    private volatile string? _cached;
    private JwtClaims? _claims;
    private bool _envVarMode;
    private bool _disposed;

    private ITimer? _refreshTimer;
    private static readonly TimeSpan RefreshLead    = TimeSpan.FromHours(24);
    private static readonly TimeSpan TransientRetry = TimeSpan.FromMinutes(10);

    public OAuthTokenProvider(
        WinttyAuthClient auth,
        IJwtStore store,
        IBrowserLauncher browser,
        ILoopbackListener listener,
        Uri apiBase,
        ILogger<OAuthTokenProvider> logger,
        TimeProvider time,
        Func<string, string?>? envVarLookup = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(apiBase);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);

        _auth = auth;
        _store = store;
        _browser = browser;
        _listener = listener;
        _apiBase = apiBase;
        _logger = logger;
        _time = time;
        _envVarLookup = envVarLookup ?? Environment.GetEnvironmentVariable;
    }

    public Task<string?> GetTokenAsync(CancellationToken ct = default)
        => Task.FromResult(_cached);

    /// <summary>
    /// Reactive 401 handler called by <c>WinttyManifestClient</c> when
    /// the Worker rejects our current bearer. Kicks off a single-flight
    /// background refresh. Returns synchronously so the calling HTTP
    /// retry can proceed. In env-var mode this is a no-op (no refresh
    /// path exists for env tokens). If another refresh is already in
    /// flight, this caller's retry rides on its result.
    /// </summary>
    public void Invalidate()
    {
        if (_envVarMode) return;
        _ = Task.Run(() => TryRefreshAsync(_lifetime.Token));
    }

    private async Task TryRefreshAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            // Another refresh already in flight; let it handle things.
            return;
        }
        try
        {
            var current = _cached;
            if (string.IsNullOrEmpty(current)) return;

            try
            {
                var refreshed = await _auth.RefreshAsync(current, ct).ConfigureAwait(false);
                var parsed = JwtClaims.Parse(refreshed);
                await _store.WriteAsync(Encoding.UTF8.GetBytes(refreshed), ct).ConfigureAwait(false);
                _cached = refreshed;
                _claims = parsed;
                _logger.LogInformation("[sponsor/auth] reactive refresh succeeded");
                ScheduleNextRefresh();
            }
            catch (AuthException ex) when (ex.Kind == AuthErrorKind.Unauthorized)
            {
                _logger.LogInformation("[sponsor/auth] reactive refresh rejected; clearing cache");
                _cached = null;
                _claims = null;
                await SafeDeleteAsync(ct).ConfigureAwait(false);
                TokenInvalidated?.Invoke(this, EventArgs.Empty);
            }
            catch (AuthException ex)
            {
                _logger.LogWarning(ex, "[sponsor/auth] reactive refresh transient failure");
                // Leave cache alone; next 401 will retry.
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[sponsor/auth] reactive refresh unexpected failure");
        }
        finally
        {
            _gate.Release();
        }
    }

    public event EventHandler? TokenInvalidated;

    /// <summary>
    /// Raised after a successful sign-in or proactive/reactive refresh
    /// acquires a new token. The shell-side palette source subscribes
    /// to re-register the "Sign out" entry visibility.
    /// </summary>
    public event EventHandler? TokenAcquired;

    // Internal helper for subsequent tasks that will invoke the event.
    private void RaiseTokenAcquired() => TokenAcquired?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Called once at app launch from <c>SponsorOverlayBootstrapper</c>.
    /// Loads any cached JWT from the store, validates shape + freshness,
    /// attempts a synchronous refresh if the token is expired-but-within-
    /// 24h, and deletes the blob on any unrecoverable state. Does NOT
    /// schedule the proactive refresh timer - that's Task 12's job so
    /// tests can assert boot behavior deterministically without wall
    /// clock interference.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Env-var short-circuit: dev path, never refreshes, skips store.
            var env = _envVarLookup("WINTTY_DEV_JWT");
            if (!string.IsNullOrEmpty(env))
            {
                try
                {
                    _claims = JwtClaims.Parse(env);
                    _cached = env;
                    _envVarMode = true;
                    _logger.LogInformation("[sponsor/auth] using WINTTY_DEV_JWT (expires {Exp})", _claims.ExpiresAt);
                }
                catch (AuthException ex)
                {
                    _logger.LogWarning(ex, "[sponsor/auth] WINTTY_DEV_JWT is malformed; ignoring");
                }
                return;
            }

            byte[]? blob;
            try
            {
                blob = await _store.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[sponsor/auth] JWT store read failed");
                blob = null;
            }

            if (blob is null || blob.Length == 0)
            {
                _logger.LogInformation("[sponsor/auth] no cached JWT; sign-in required");
                return;
            }

            var jwt = Encoding.UTF8.GetString(blob);
            JwtClaims parsed;
            try
            {
                parsed = JwtClaims.Parse(jwt);
            }
            catch (AuthException ex)
            {
                _logger.LogWarning(ex, "[sponsor/auth] cached JWT malformed; deleting");
                await SafeDeleteAsync(ct).ConfigureAwait(false);
                return;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            if (parsed.ExpiresAt <= now - TimeSpan.FromHours(24))
            {
                _logger.LogInformation("[sponsor/auth] cached JWT stale by more than 24h; deleting");
                await SafeDeleteAsync(ct).ConfigureAwait(false);
                return;
            }

            if (parsed.ExpiresAt <= now)
            {
                try
                {
                    var refreshed = await _auth.RefreshAsync(jwt, ct).ConfigureAwait(false);
                    var newClaims = JwtClaims.Parse(refreshed);
                    await _store.WriteAsync(Encoding.UTF8.GetBytes(refreshed), ct).ConfigureAwait(false);
                    _cached = refreshed;
                    _claims = newClaims;
                    _logger.LogInformation("[sponsor/auth] refreshed expired JWT on boot");
                    ScheduleNextRefresh();
                    return;
                }
                catch (AuthException ex) when (ex.Kind == AuthErrorKind.Unauthorized)
                {
                    _logger.LogInformation("[sponsor/auth] expired JWT rejected on refresh; deleting");
                    await SafeDeleteAsync(ct).ConfigureAwait(false);
                    return;
                }
                catch (AuthException ex)
                {
                    _logger.LogWarning(ex, "[sponsor/auth] refresh failed on boot; keeping nothing cached");
                    await SafeDeleteAsync(ct).ConfigureAwait(false);
                    return;
                }
            }

            _cached = jwt;
            _claims = parsed;
            _logger.LogInformation("[sponsor/auth] loaded cached JWT (expires {Exp})", parsed.ExpiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SafeDeleteAsync(CancellationToken ct)
    {
        try { await _store.DeleteAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "[sponsor/auth] store delete failed"); }
    }

    /// <summary>
    /// Starts the proactive refresh timer after a successful
    /// InitializeAsync or SignInAsync. No-op in env-var mode (no refresh
    /// path) and when there is no cached token.
    /// </summary>
    public void StartRefreshTimer()
    {
        if (_envVarMode) return;
        if (_claims is null) return;
        ScheduleNextRefresh();
    }

    private void ScheduleNextRefresh()
    {
        if (_disposed) return;
        if (_claims is null) return;
        var now = _time.GetUtcNow();
        var due = _claims.ExpiresAt - RefreshLead - now.UtcDateTime;
        if (due <= TimeSpan.Zero) due = TimeSpan.Zero;

        _refreshTimer?.Dispose();
        if (_disposed) return;  // double-check after the tiny window between the Dispose call above and here
        _refreshTimer = _time.CreateTimer(
            _ => _ = OnRefreshTickAsync(),
            state: null,
            dueTime: due,
            period: Timeout.InfiniteTimeSpan);
    }

    private async Task OnRefreshTickAsync()
    {
        if (_disposed) return;
        if (!await _gate.WaitAsync(0, _lifetime.Token).ConfigureAwait(false))
        {
            return; // another refresh already running; it will reschedule
        }
        try
        {
            var current = _cached;
            if (string.IsNullOrEmpty(current)) return;

            try
            {
                var refreshed = await _auth.RefreshAsync(current, _lifetime.Token).ConfigureAwait(false);
                var parsed = JwtClaims.Parse(refreshed);
                await _store.WriteAsync(Encoding.UTF8.GetBytes(refreshed), _lifetime.Token).ConfigureAwait(false);
                _cached = refreshed;
                _claims = parsed;
                _logger.LogInformation("[sponsor/auth] proactive refresh succeeded");
                ScheduleNextRefresh();
            }
            catch (AuthException ex) when (ex.Kind == AuthErrorKind.Unauthorized)
            {
                _logger.LogInformation("[sponsor/auth] proactive refresh rejected; clearing");
                _cached = null; _claims = null;
                await SafeDeleteAsync(_lifetime.Token).ConfigureAwait(false);
                _refreshTimer?.Dispose();
                _refreshTimer = null;
                TokenInvalidated?.Invoke(this, EventArgs.Empty);
            }
            catch (AuthException ex)
            {
                _logger.LogWarning(ex, "[sponsor/auth] proactive refresh transient; retry in 10m");
                _refreshTimer?.Dispose();
                if (_disposed) return;
                _refreshTimer = _time.CreateTimer(
                    _ => _ = OnRefreshTickAsync(), null,
                    TransientRetry, Timeout.InfiniteTimeSpan);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[sponsor/auth] proactive refresh unexpected failure");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Revokes the current session best-effort against the Worker, then
    /// deletes the local blob. Fires <see cref="TokenInvalidated"/>
    /// only if there was a token to sign out of. Network/server failures
    /// on revoke are swallowed - the JWT remains valid on the Worker
    /// until <c>exp</c> but the local machine no longer has it.
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _cached;
            _cached = null;
            _claims = null;
            _refreshTimer?.Dispose();
            _refreshTimer = null;

            if (!string.IsNullOrEmpty(current))
            {
                if (!_envVarMode)
                {
                    try
                    {
                        await _auth.RevokeAsync(current, ct).ConfigureAwait(false);
                        _logger.LogInformation("[sponsor/auth] revoke acknowledged");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[sponsor/auth] revoke best-effort failed");
                    }

                    await SafeDeleteAsync(ct).ConfigureAwait(false);
                }

                TokenInvalidated?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static readonly TimeSpan LoopbackTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Runs the one-shot OAuth loopback flow: starts the listener,
    /// opens the browser, awaits the callback, validates nonce and
    /// JWT shape, persists. Returns true on success. Returns false
    /// on timeout, user cancel, error query, nonce mismatch, or
    /// pre-expired JWT. Unexpected exceptions propagate.
    /// </summary>
    public async Task<bool> SignInAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                _listener.Start();
            }
            catch (Exception ex) when (ex is System.Net.HttpListenerException
                                          or System.Net.Sockets.SocketException)
            {
                _logger.LogWarning(ex, "[sponsor/auth] loopback bind failed");
                return false;
            }

            // The Worker's /auth/github/start endpoint takes a `nonce` query param
            // and echoes it verbatim in the final /cb 302. This is an ADDITIONAL
            // CSRF defense on top of the OAuth `state` param (which the Worker
            // owns end-to-end and HS256-signs with its own secret). The client
            // never touches OAuth `state`; our `nonce` is the client-local replay
            // guard against a rogue local process racing the real callback.
            var nonce = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            var callback = $"http://127.0.0.1:{_listener.Port}/cb";

            var url = new Uri(
                $"{_apiBase.GetLeftPart(UriPartial.Authority)}/auth/github/start"
                + $"?redirect={Uri.EscapeDataString(callback)}"
                + $"&nonce={nonce}");
            _browser.Open(url);

            using var timeoutCts = new CancellationTokenSource(LoopbackTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            LoopbackResult callbackResult;
            try
            {
                callbackResult = await _listener.AwaitCallbackAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[sponsor/auth] sign-in timed out or cancelled");
                return false;
            }

            if (!string.IsNullOrEmpty(callbackResult.Error))
            {
                // Log length only - never log raw attacker-supplied query strings.
                _logger.LogInformation("[sponsor/auth] OAuth error query ({Length} chars)",
                    callbackResult.Error.Length);
                return false;
            }

            if (callbackResult.Nonce != nonce)
            {
                _logger.LogWarning("[sponsor/auth] nonce mismatch on callback");
                return false;
            }

            if (string.IsNullOrEmpty(callbackResult.Token))
            {
                _logger.LogWarning("[sponsor/auth] callback missing token");
                return false;
            }

            JwtClaims parsed;
            try
            {
                parsed = JwtClaims.Parse(callbackResult.Token);
            }
            catch (AuthException ex)
            {
                _logger.LogWarning(ex, "[sponsor/auth] callback returned malformed JWT");
                return false;
            }

            if (parsed.ExpiresAt <= _time.GetUtcNow().UtcDateTime)
            {
                _logger.LogWarning("[sponsor/auth] callback JWT is pre-expired");
                return false;
            }

            await _store.WriteAsync(
                Encoding.UTF8.GetBytes(callbackResult.Token), ct).ConfigureAwait(false);
            _cached = callbackResult.Token;
            _claims = parsed;
            _logger.LogInformation("[sponsor/auth] sign-in complete (exp {Exp})", parsed.ExpiresAt);
            RaiseTokenAcquired();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lifetime.Cancel(); } catch { /* idempotent */ }
        _refreshTimer?.Dispose();
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
