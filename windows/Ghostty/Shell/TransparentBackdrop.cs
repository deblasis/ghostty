using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Ghostty.Shell;

/// <summary>
/// A fully transparent window backdrop for use when background-opacity
/// is less than 1.0. Uses <see cref="DesktopAcrylicController"/> with
/// zero tint and zero luminosity so the DX12 swap chain's premultiplied
/// alpha shows through to the desktop.
///
/// Requires Windows 11 Build 22000+. Call
/// <see cref="DesktopAcrylicController.IsSupported"/> before assigning.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _config;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);

        _controller = new DesktopAcrylicController
        {
            // Zero tint + zero luminosity = fully transparent backdrop.
            // The swap chain's premultiplied alpha blends directly with
            // whatever is behind the window (desktop, other apps).
            TintColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
            TintOpacity = 0f,
            LuminosityOpacity = 0f,
            FallbackColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
        };

        // Force the backdrop to stay active even when the window loses
        // focus. The default config switches to FallbackColor on
        // deactivation, which makes the window flash opaque.
        _config = new SystemBackdropConfiguration
        {
            IsInputActive = true,
        };

        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_config);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);

        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
        _config = null;
    }
}
