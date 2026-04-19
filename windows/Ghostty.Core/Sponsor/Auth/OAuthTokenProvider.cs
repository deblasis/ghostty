using System;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lifetime.Cancel(); } catch { /* idempotent */ }
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
