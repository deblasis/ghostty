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
    /// Reactive 401 handler. Task 8 skeleton clears the cache and fires
    /// <see cref="TokenInvalidated"/>. Task 11 replaces this with a
    /// background refresh attempt that only fires on refresh failure.
    /// </summary>
    public void Invalidate()
    {
        _cached = null;
        _claims = null;
        TokenInvalidated?.Invoke(this, EventArgs.Empty);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lifetime.Cancel(); } catch { /* idempotent */ }
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
