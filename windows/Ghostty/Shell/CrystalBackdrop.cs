using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Ghostty.Interop;

namespace Ghostty.Shell;

/// <summary>
/// Zero-blur transparent backdrop using the DWM blur-behind trick.
/// Creates a degenerate blur region so DWM composites the window
/// as transparent without applying any gaussian blur. The terminal
/// swap chain's premultiplied alpha shows through to the raw desktop.
/// </summary>
internal sealed partial class CrystalBackdrop : SystemBackdrop
{
    private readonly IntPtr _hwnd;
    private IntPtr _blurRegion;

    internal CrystalBackdrop(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);

        if (_hwnd == IntPtr.Zero) return;

        // Extend the DWM frame into the entire client area.
        var margins = new Win32Interop.MARGINS();
        Win32Interop.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        // Enable blur-behind with a degenerate (off-screen) region.
        // This tricks DWM into compositing the window as transparent
        // without applying any blur filter.
        _blurRegion = Win32Interop.CreateRectRgn(-2, -2, -1, -1);
        var bb = new Win32Interop.DWM_BLURBEHIND
        {
            DwFlags = Win32Interop.DWM_BLURBEHIND.DWM_BB_ENABLE
                    | Win32Interop.DWM_BLURBEHIND.DWM_BB_BLURREGION,
            FEnable = 1,
            HRgnBlur = _blurRegion,
        };
        Win32Interop.DwmEnableBlurBehindWindow(_hwnd, ref bb);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);

        if (_blurRegion != IntPtr.Zero)
        {
            Win32Interop.DeleteObject(_blurRegion);
            _blurRegion = IntPtr.Zero;
        }
    }
}
