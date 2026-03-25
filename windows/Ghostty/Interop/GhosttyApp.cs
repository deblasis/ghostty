using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ghostty.Interop;

/// <summary>
/// Managed wrapper around the libghostty app lifecycle.
/// Mirrors the pattern used by the macOS Swift app:
/// ghostty_init → config_new → load → finalize → app_new → tick loop → free.
/// </summary>
public sealed class GhosttyApp : IDisposable
{
    private nint _app;
    private nint _config;
    private bool _disposed;

    // Prevent GC from collecting delegates while native code holds function pointers.
    private readonly WakeupDelegate _wakeupDelegate;
    private readonly ActionDelegate _actionDelegate;
    private readonly ReadClipboardDelegate _readClipboardDelegate;
    private readonly ConfirmReadClipboardDelegate _confirmReadClipboardDelegate;
    private readonly WriteClipboardDelegate _writeClipboardDelegate;
    private readonly CloseSurfaceDelegate _closeSurfaceDelegate;

    // Callback signatures matching ghostty.h typedefs
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WakeupDelegate(nint userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool ActionDelegate(
        nint app,
        GhosttyTarget target,
        GhosttyAction action);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool ReadClipboardDelegate(
        nint userdata,
        GhosttyClipboard clipboard,
        nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ConfirmReadClipboardDelegate(
        nint userdata,
        nint message,
        nint state,
        GhosttyClipboardRequest request);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WriteClipboardDelegate(
        nint userdata,
        GhosttyClipboard clipboard,
        nint content,
        nuint contentLen,
        bool confirm);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseSurfaceDelegate(
        nint userdata,
        bool processAlive);

    public bool IsInitialized => _app != 0;

    public GhosttyApp()
    {
        // Create and pin all delegates up front so they survive GC.
        _wakeupDelegate = OnWakeup;
        _actionDelegate = OnAction;
        _readClipboardDelegate = OnReadClipboard;
        _confirmReadClipboardDelegate = OnConfirmReadClipboard;
        _writeClipboardDelegate = OnWriteClipboard;
        _closeSurfaceDelegate = OnCloseSurface;
    }

    /// <summary>
    /// Initialize libghostty, load config, and create the app instance.
    /// Returns true on success.
    /// </summary>
    public unsafe bool Initialize()
    {
        // ghostty_init requires a valid argv pointer (Zig slices it as argv[0..argc]).
        // Pass the exe path as argv[0], matching the macOS Swift pattern.
        var exePath = Environment.ProcessPath ?? "ghostty";
        var exePathBytes = System.Text.Encoding.UTF8.GetBytes(exePath + '\0');

        fixed (byte* exePathPtr = exePathBytes)
        {
            var argvEntry = (nint)exePathPtr;
            var result = NativeMethods.Init(1, (nint)(&argvEntry));
            if (result != 0)
            {
                Debug.WriteLine("ghostty_init failed");
                return false;
            }
        }

        Debug.WriteLine("ghostty_init succeeded");

        // Create and load config
        _config = NativeMethods.ConfigNew();
        if (_config == 0)
        {
            Debug.WriteLine("ghostty_config_new returned null");
            return false;
        }

        NativeMethods.ConfigLoadDefaultFiles(_config);
        NativeMethods.ConfigLoadRecursiveFiles(_config);
        NativeMethods.ConfigFinalize(_config);

        var diagCount = NativeMethods.ConfigDiagnosticsCount(_config);
        Debug.WriteLine($"Config loaded with {diagCount} diagnostic(s)");

        // Wire up the runtime config with callbacks
        var runtimeConfig = new GhosttyRuntimeConfig
        {
            Userdata = 0,
            SupportsSelectionClipboard = false,
            WakeupCb = Marshal.GetFunctionPointerForDelegate(_wakeupDelegate),
            ActionCb = Marshal.GetFunctionPointerForDelegate(_actionDelegate),
            ReadClipboardCb = Marshal.GetFunctionPointerForDelegate(_readClipboardDelegate),
            ConfirmReadClipboardCb = Marshal.GetFunctionPointerForDelegate(_confirmReadClipboardDelegate),
            WriteClipboardCb = Marshal.GetFunctionPointerForDelegate(_writeClipboardDelegate),
            CloseSurfaceCb = Marshal.GetFunctionPointerForDelegate(_closeSurfaceDelegate),
        };

        _app = NativeMethods.AppNew(ref runtimeConfig, _config);
        if (_app == 0)
        {
            Debug.WriteLine("ghostty_app_new returned null");
            NativeMethods.ConfigFree(_config);
            _config = 0;
            return false;
        }

        Debug.WriteLine("ghostty_app_new succeeded");
        return true;
    }

    public void Tick()
    {
        if (_app != 0)
            NativeMethods.AppTick(_app);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_app != 0)
        {
            NativeMethods.AppFree(_app);
            _app = 0;
        }

        if (_config != 0)
        {
            NativeMethods.ConfigFree(_config);
            _config = 0;
        }
    }

    // --- Stub callbacks ---
    // These will be fleshed out as we add surface management, clipboard,
    // and action handling in later PRs.

    private static void OnWakeup(nint userdata)
    {
        Debug.WriteLine("ghostty: wakeup");
    }

    private static bool OnAction(nint app, GhosttyTarget target, GhosttyAction action)
    {
        Debug.WriteLine($"ghostty: action {action.Tag}");
        return false;
    }

    private static bool OnReadClipboard(nint userdata, GhosttyClipboard clipboard, nint state)
    {
        Debug.WriteLine($"ghostty: read_clipboard {clipboard}");
        return false;
    }

    private static void OnConfirmReadClipboard(
        nint userdata, nint message, nint state, GhosttyClipboardRequest request)
    {
        Debug.WriteLine($"ghostty: confirm_read_clipboard {request}");
    }

    private static void OnWriteClipboard(
        nint userdata, GhosttyClipboard clipboard, nint content, nuint contentLen, bool confirm)
    {
        Debug.WriteLine($"ghostty: write_clipboard {clipboard}");
    }

    private static void OnCloseSurface(nint userdata, bool processAlive)
    {
        Debug.WriteLine($"ghostty: close_surface processAlive={processAlive}");
    }
}
