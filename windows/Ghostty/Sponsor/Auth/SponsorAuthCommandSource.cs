#if SPONSOR_BUILD
using System;
using System.Collections.Generic;
using System.Threading;
using Ghostty.Commands;
using Ghostty.Core.Sponsor.Auth;
using Microsoft.Extensions.Logging;

namespace Ghostty.Sponsor.Auth;

/// <summary>
/// Palette entries for OAuth sign-in / sign-out. Visible in release
/// sponsor builds (not DEBUG-gated like <c>SponsorUpdateCommandSource</c>).
/// Re-registers its entries on <c>TokenAcquired</c> / <c>TokenInvalidated</c>
/// via the palette's <see cref="ICommandSource.Refresh"/> contract -
/// the host calls <see cref="GetCommands"/> every time the palette
/// opens, so simply triggering a Refresh is enough to flip the visible
/// entry between "Sign in" and "Sign out".
/// </summary>
internal sealed class SponsorAuthCommandSource : ICommandSource, IDisposable
{
    private readonly OAuthTokenProvider _provider;
    private readonly ILogger<SponsorAuthCommandSource> _logger;
    private readonly List<CommandItem> _items = new();
    private bool _disposed;

    public SponsorAuthCommandSource(OAuthTokenProvider provider, ILogger<SponsorAuthCommandSource> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _logger = logger;

        _provider.TokenAcquired    += OnStateChanged;
        _provider.TokenInvalidated += OnStateChanged;

        Refresh();
    }

    public IReadOnlyList<CommandItem> GetCommands() => _items;

    public void Refresh()
    {
        _items.Clear();

        // Snapshot current state. GetTokenAsync is a hot-path synchronous
        // Task.FromResult under the hood, so GetAwaiter().GetResult() is
        // safe and non-blocking. We need it synchronously because
        // GetCommands is called on the UI thread on each palette open.
        var hasToken = _provider.GetTokenAsync(CancellationToken.None)
            .GetAwaiter().GetResult() is not null;

        if (!hasToken)
        {
            _items.Add(new CommandItem
            {
                Id = "wintty.signIn",
                Title = "Sign in to wintty",
                Description = "Activate sponsor updates via GitHub",
                Category = CommandCategory.Custom,
                Execute = _ => SignInFireAndForget(),
            });
        }
        else
        {
            _items.Add(new CommandItem
            {
                Id = "wintty.signOut",
                Title = "Sign out of wintty",
                Description = "Stops sponsor updates on this machine",
                Category = CommandCategory.Custom,
                Execute = _ => SignOutFireAndForget(),
            });
        }
    }

    private void SignInFireAndForget()
    {
        // Fire-and-forget: the palette close happens on the caller's
        // UI thread. The provider's sign-in flow runs async; if it fails
        // we log but show nothing.
        _ = SignInAsync();
    }

    private async System.Threading.Tasks.Task SignInAsync()
    {
        try { await _provider.SignInAsync(CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning(ex, "[sponsor/auth] palette SignIn failed"); }
    }

    private void SignOutFireAndForget()
    {
        _ = SignOutAsync();
    }

    private async System.Threading.Tasks.Task SignOutAsync()
    {
        try { await _provider.SignOutAsync(CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning(ex, "[sponsor/auth] palette SignOut failed"); }
    }

    private void OnStateChanged(object? sender, EventArgs e) => Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _provider.TokenAcquired    -= OnStateChanged;
        _provider.TokenInvalidated -= OnStateChanged;
    }
}
#endif
