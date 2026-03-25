using Ghostty.Interop;

namespace Ghostty;

/// <summary>
/// Custom entry point to handle CLI actions before launching the GUI.
/// Mirrors the macOS pattern: ghostty_init → ghostty_cli_try_action → GUI.
/// Since ghostty_init currently crashes in Windows DLLs (Zig global state bug),
/// we handle --version directly in C# using ghostty_info().
/// </summary>
public static class Program
{
    [global::System.STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            var info = NativeMethods.Info();
            var version = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(
                info.Version, (int)info.VersionLen);
            Console.WriteLine(version);
            return;
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
