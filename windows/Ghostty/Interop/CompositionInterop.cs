using System;
using System.Runtime.InteropServices;

namespace Ghostty.Interop;

/// <summary>
/// COM interop for Windows.UI.Composition. Used to bind a DXGI swap
/// chain to a composition visual so the DX12 terminal content
/// participates in the WinUI 3 visual tree with per-pixel alpha.
/// </summary>
[ComImport]
[Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICompositorInterop
{
    [PreserveSig]
    int CreateCompositionSurfaceForSwapChain(
        IntPtr swapChain,
        out IntPtr compositionSurface);

    [PreserveSig]
    int CreateCompositionSurfaceForHandle(
        IntPtr swapChainHandle,
        out IntPtr compositionSurface);

    [PreserveSig]
    int CreateGraphicsDevice(
        IntPtr renderingDevice,
        out IntPtr graphicsDevice);
}
