using System;
using System.Runtime.InteropServices;

namespace Ghostty.Core.Interop;

// Mirrors the nested `struct { bool enabled; uint32_t width; uint32_t height; }`
// inside ghostty_platform_windows_s. Used when constructing the platform
// config for shared-texture surface mode.
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySharedTextureConfig
{
    [MarshalAs(UnmanagedType.I1)]
    public bool Enabled;
    public uint Width;
    public uint Height;
}

// Mirrors ghostty_surface_shared_texture_s -- the atomic snapshot returned
// by ghostty_surface_shared_texture(). Field order is ABI-critical.
[StructLayout(LayoutKind.Sequential)]
public struct GhosttySharedTextureSnapshot
{
    public IntPtr ResourceHandle;  // NT HANDLE -- do NOT CloseHandle; ghostty retains ownership
    public IntPtr FenceHandle;     // NT HANDLE -- do NOT CloseHandle; stable for surface lifetime
    public ulong FenceValue;
    public uint Width;
    public uint Height;
    public ulong Version;
}
