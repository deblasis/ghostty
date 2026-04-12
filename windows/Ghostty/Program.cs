using System;
using System.IO;
using System.Runtime.InteropServices;
using Ghostty.Interop;

namespace Ghostty;

/// <summary>
/// Custom entry point that wraps the WinUI 3 startup with diagnostic
/// error capture. The XAML-generated Main is suppressed via
/// DISABLE_XAML_GENERATED_MAIN. This is temporary for debugging
/// NativeAOT startup crashes that produce no output.
/// </summary>
public static partial class Program
{
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [STAThread]
    static int Main(string[] args)
    {
        // CLI actions are delegated to libghostty, matching the macOS
        // architecture: ghostty_init parses argv, ghostty_cli_try_action
        // runs the action (if any) and calls ExitProcess. If no action,
        // it returns and we start the WinUI app.
        //
        // For CLI mode we need a console. WinExe processes don't inherit
        // one, so AttachConsole gives us the parent's console (or the
        // ConPTY pseudoconsole when launched from a terminal). This makes
        // isTty() return true in the Zig code, enabling the interactive
        // Vaxis TUI for +list-themes.
        if (args.Length > 0 && args[0].StartsWith('+'))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            RegisterNativeResolver();
            InitGhostty(args);
            // Run the CLI action if one was parsed from argv. Returns
            // the exit code, or -1 if no action matched. We use
            // CliRunAction (not CliTryAction) because the latter calls
            // posix.exit/ExitProcess which triggers DLL_PROCESS_DETACH
            // cleanup that crashes on Windows.
            var exitCode = NativeMethods.CliRunAction();
            if (exitCode >= 0)
                Environment.Exit(exitCode);
        }

        return StartGui();
    }

    /// <summary>
    /// Marshal the managed args array into a C-style argv (null-terminated
    /// UTF-8 strings) and call ghostty_init. The program name "ghostty" is
    /// prepended as argv[0] since .NET's args array omits it.
    ///
    /// The allocated argv is intentionally not freed: ghostty_init stores
    /// the pointers in std.os.argv, and ghostty_cli_try_action reads them
    /// later. The OS reclaims everything on process exit.
    /// </summary>
    private static void InitGhostty(string[] args)
    {
        // Build argv: ["ghostty", args[0], args[1], ...]
        var argc = args.Length + 1;
        var argv = new IntPtr[argc];
        argv[0] = Marshal.StringToCoTaskMemUTF8("ghostty");
        for (int i = 0; i < args.Length; i++)
            argv[i + 1] = Marshal.StringToCoTaskMemUTF8(args[i]);

        var argvPtr = Marshal.AllocCoTaskMem(IntPtr.Size * argc);
        Marshal.Copy(argv, 0, argvPtr, argc);

        var result = NativeMethods.Init((UIntPtr)argc, argvPtr);
        if (result != 0)
        {
            // ghostty_init failed (e.g. invalid action). The Zig
            // code logs to stderr. Exit with failure.
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Register the native DLL resolver so LibraryImport("ghostty") finds
    /// native/ghostty.dll. Mirrors the resolver in App.xaml.cs but runs
    /// before WinUI is initialized, enabling CLI-path P/Invoke calls.
    /// </summary>
    private static void RegisterNativeResolver()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(Interop.NativeMethods).Assembly,
            (name, assembly, path) =>
            {
                if (!string.Equals(name, "ghostty", StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;
                var candidate = Path.Combine(AppContext.BaseDirectory, "native", "ghostty.dll");
                return NativeLibrary.Load(candidate);
            });
    }

    private static int StartGui()
    {
        try
        {
            Console.Error.WriteLine("[Ghostty] Program.Main entered");
            Console.Error.Flush();

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Console.Error.WriteLine("[Ghostty] ComWrappers initialized");
            Console.Error.Flush();

            Microsoft.UI.Xaml.Application.Start(p =>
            {
                Console.Error.WriteLine("[Ghostty] Application.Start callback entered");
                Console.Error.Flush();

                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);

                Console.Error.WriteLine("[Ghostty] Creating App instance");
                Console.Error.Flush();

                new App();

                Console.Error.WriteLine("[Ghostty] App instance created");
                Console.Error.Flush();
            });

            return 0;
        }
        catch (Exception ex)
        {
            var msg = $"[Ghostty] FATAL: {ex}";
            Console.Error.WriteLine(msg);
            Console.Error.Flush();

            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "ghostty-crash.log");
                File.WriteAllText(logPath, msg);
            }
            catch { /* best effort */ }

            return 1;
        }
    }
}
