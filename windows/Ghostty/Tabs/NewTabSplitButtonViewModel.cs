using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Profiles;
using Ghostty.Core.Tabs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ghostty.Tabs;

/// <summary>
/// XAML-side wrapper around <see cref="NewTabFlyoutController"/>.
/// Holds the cross-platform row list AND lazily-resolved
/// <see cref="BitmapImage"/> per row. Disposed on the UserControl's
/// <c>Unloaded</c>; cancels any in-flight icon resolves.
/// </summary>
internal sealed class NewTabSplitButtonViewModel : IDisposable
{
    private readonly NewTabFlyoutController _controller;
    private readonly IIconResolver _iconResolver;
    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _cts = new();

    public NewTabSplitButtonViewModel(
        IProfileRegistry registry,
        IIconResolver iconResolver,
        DispatcherQueue dispatcher)
    {
        _controller = new NewTabFlyoutController(registry);
        _iconResolver = iconResolver;
        _dispatcher = dispatcher;
    }

    public IReadOnlyList<NewTabFlyoutController.Row> Rows => _controller.Rows;

    /// <summary>
    /// Fire-and-forget: resolve the icon for the given profile id and
    /// invoke <paramref name="apply"/> on the UI thread when ready.
    /// Best-effort; failures keep the placeholder.
    /// </summary>
    public void ResolveIcon(string profileId, IconSpec spec, Action<BitmapImage> apply)
        => _ = ResolveIconAsync(profileId, spec, apply, _cts.Token);

    private async Task ResolveIconAsync(
        string profileId, IconSpec spec, Action<BitmapImage> apply, CancellationToken ct)
    {
        try
        {
            var bytes = await _iconResolver.ResolveAsync(spec, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            _dispatcher.TryEnqueue(() =>
            {
                if (ct.IsCancellationRequested) return;
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                using var raStream = stream.AsRandomAccessStream();
                _ = image.SetSourceAsync(raStream).AsTask();
                apply(image);
            });
        }
        catch (OperationCanceledException) { /* expected on dispose */ }
        catch
        {
            // Best-effort: row keeps placeholder; failure already
            // logged at warning level inside IIconResolver.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _controller.Dispose();
    }
}
