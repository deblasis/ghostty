using System.Runtime.InteropServices;

namespace Ghostty.Interop;

internal static partial class NativeMethods
{
    private const string LibName = "ghostty";

    // --- Info ---

    [LibraryImport(LibName, EntryPoint = "ghostty_info")]
    public static partial GhosttyInfo Info();

    // --- Init ---

    [LibraryImport(LibName, EntryPoint = "ghostty_init")]
    public static partial int Init(nuint argc, nint argv);

    // --- Config ---

    [LibraryImport(LibName, EntryPoint = "ghostty_config_new")]
    public static partial nint ConfigNew();

    [LibraryImport(LibName, EntryPoint = "ghostty_config_free")]
    public static partial void ConfigFree(nint config);

    [LibraryImport(LibName, EntryPoint = "ghostty_config_load_default_files")]
    public static partial void ConfigLoadDefaultFiles(nint config);

    [LibraryImport(LibName, EntryPoint = "ghostty_config_load_recursive_files")]
    public static partial void ConfigLoadRecursiveFiles(nint config);

    [LibraryImport(LibName, EntryPoint = "ghostty_config_finalize")]
    public static partial void ConfigFinalize(nint config);

    [LibraryImport(LibName, EntryPoint = "ghostty_config_diagnostics_count")]
    public static partial uint ConfigDiagnosticsCount(nint config);

    // --- App ---

    [LibraryImport(LibName, EntryPoint = "ghostty_app_new")]
    public static partial nint AppNew(ref GhosttyRuntimeConfig runtimeConfig, nint config);

    [LibraryImport(LibName, EntryPoint = "ghostty_app_free")]
    public static partial void AppFree(nint app);

    [LibraryImport(LibName, EntryPoint = "ghostty_app_tick")]
    public static partial void AppTick(nint app);
}
