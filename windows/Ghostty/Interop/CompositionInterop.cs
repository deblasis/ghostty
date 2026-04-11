using System;
using System.Runtime.InteropServices;

namespace Ghostty.Interop;

/// <summary>
/// COM interop for Microsoft.UI.Composition (WinUI 3). Binds a DXGI
/// swap chain to a composition visual so the DX12 terminal content
/// participates in the WinUI 3 visual tree with per-pixel alpha.
///
/// This is the WinUI 3 / Windows App SDK version of the interface.
/// The UWP version (Windows.UI.Composition) has a different GUID
/// (25297D5C-...) and does not work with Microsoft.UI.Composition.
/// See: github.com/microsoft/microsoft-ui-xaml/issues/3752
/// </summary>
[ComImport]
[Guid("FAB19398-6D19-4D8A-B752-8F096C396069")]
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
