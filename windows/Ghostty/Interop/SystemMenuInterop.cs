using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Ghostty.Interop;

/// <summary>
/// Opens the Windows system menu (Restore / Move / Size / Minimize /
/// Maximize / Close) over a WinUI 3 custom title bar element. Matches
/// Notepad's caption-icon behavior on the WinUI 3 side where the non-
/// client area is extended and the OS does not draw its own caption icon.
///
/// Hand-written LibraryImport follows the existing project convention
/// in Interop/NativeMethods.cs and Interop/ShellInterop.cs. BOOL is
/// represented as int (0 = FALSE, non-zero = TRUE) because
/// [assembly: DisableRuntimeMarshalling] forbids [MarshalAs] on
/// LibraryImport signatures.
/// </summary>
internal static partial class SystemMenuInterop
{
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_SYSCOMMAND = 0x0112;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // hWnd, BOOL bRevert (0 = retrieve, non-zero = reset default)
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetSystemMenu(IntPtr hWnd, int bRevert);

    // Returns command id (int), 0 if cancelled, or >0 on selection with TPM_RETURNCMD.
    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenu(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        IntPtr hWnd,
        IntPtr prcRect);

    // Returns BOOL (int). Non-zero on success.
    [LibraryImport("user32.dll")]
    private static partial int ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    // Returns BOOL (int). Non-zero on success.
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    private static partial int PostMessage(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam);

    public static void ShowAt(Window window, FrameworkElement anchor)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(anchor);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var hmenu = GetSystemMenu(hwnd, 0);
        if (hmenu == IntPtr.Zero) return;

        // Anchor at the badge's bottom-left corner in screen coordinates.
        // DIP -> physical pixels via the anchor's XamlRoot RasterizationScale.
        var transform = anchor.TransformToVisual(null);
        var bottomLeftDip = transform.TransformPoint(new Point(0, anchor.ActualHeight));
        var scale = anchor.XamlRoot.RasterizationScale;

        var clientPoint = new POINT
        {
            X = (int)(bottomLeftDip.X * scale),
            Y = (int)(bottomLeftDip.Y * scale),
        };
        ClientToScreen(hwnd, ref clientPoint);

        var cmd = TrackPopupMenu(
            hmenu,
            TPM_RETURNCMD,
            clientPoint.X,
            clientPoint.Y,
            0,
            hwnd,
            IntPtr.Zero);

        if (cmd != 0)
        {
            PostMessage(hwnd, WM_SYSCOMMAND, (IntPtr)cmd, IntPtr.Zero);
        }
    }
}
