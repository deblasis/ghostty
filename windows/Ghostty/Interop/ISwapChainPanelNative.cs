// COM interop for Microsoft.UI.Xaml.Controls.SwapChainPanel. The managed
// SwapChainPanel type does not expose a way to attach a native swap chain
// directly, so we QueryInterface for ISwapChainPanelNative and call
// SetSwapChain(IDXGISwapChain*) manually.
//
// libghostty's Windows renderer lives inside ghostty.dll and uses the panel
// handle we pass via ghostty_surface_config_s.platform.windows.swap_chain_panel.
// We hand it the IUnknown* of the panel (which is QI-able to
// ISwapChainPanelNative), so nothing on the C# side actually calls
// SetSwapChain - but we keep the interface here for any case where the
// shell needs to present its own swap chain (overlays, inspector, etc).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WinRT;

namespace Ghostty.Interop;

[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(IntPtr swapChain); // IDXGISwapChain*
}

internal static class SwapChainPanelInterop
{
    private static readonly Guid IID_ISwapChainPanelNative =
        new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    /// <summary>
    /// QueryInterfaces a WinUI 3 SwapChainPanel for ISwapChainPanelNative
    /// and returns the raw interface pointer. libghostty's DX12 renderer
    /// calls SetSwapChain (v-table slot 3) directly on the pointer we
    /// pass, so it must already be the ISwapChainPanelNative v-table, not
    /// the IUnknown of the WinRT projection.
    /// </summary>
    /// <remarks>
    /// This returns an AddRef'd pointer. libghostty is expected to Release
    /// it when the surface is destroyed. The managed SwapChainPanel still
    /// holds its own reference so nothing leaks if lifetimes line up.
    /// </remarks>
    public static IntPtr QueryInterface(Microsoft.UI.Xaml.Controls.SwapChainPanel panel)
    {
        var objRef = ((IWinRTObject)panel).NativeObject;
        var iid = IID_ISwapChainPanelNative;
        var hr = Marshal.QueryInterface(objRef.ThisPtr, in iid, out var ppv);
        if (hr < 0 || ppv == IntPtr.Zero)
            throw new InvalidOperationException(
                $"QueryInterface for ISwapChainPanelNative failed: 0x{hr:X8}");
        return ppv;
    }
}
