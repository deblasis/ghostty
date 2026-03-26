using System.Runtime.InteropServices;

namespace Ghostty.Interop;

public static partial class LibGhostty
{
    private const string DllName = "ghostty";

    [LibraryImport(DllName, EntryPoint = "ghostty_spike_init")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SpikeInit(IntPtr panelNative, uint width, uint height, float scale);

    [LibraryImport(DllName, EntryPoint = "ghostty_spike_shutdown")]
    public static partial void SpikeShutdown();

    [LibraryImport(DllName, EntryPoint = "ghostty_spike_resize")]
    public static partial void SpikeResize(uint width, uint height);

    [LibraryImport(DllName, EntryPoint = "ghostty_spike_key_press")]
    public static partial void SpikeKeyPress(uint virtualKey);

    [LibraryImport(DllName, EntryPoint = "ghostty_spike_dpi_changed")]
    public static partial void SpikeDpiChanged(float scale);
}
