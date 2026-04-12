using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ghostty;

/// <summary>
/// Custom entry point that wraps the WinUI 3 startup with diagnostic
/// error capture. The XAML-generated Main is suppressed via
/// DISABLE_XAML_GENERATED_MAIN. This is temporary for debugging
/// NativeAOT startup crashes that produce no output.
/// </summary>
public static partial class Program
{
    // WinExe subsystem has no console. Attach to the parent's console
    // so CLI actions can write to stdout/stderr.
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [STAThread]
    static int Main(string[] args)
    {
        // Handle CLI actions before WinUI startup.
        if (args.Length > 0 && args[0].StartsWith('+'))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            return args[0] switch
            {
                "+list-themes" => ListThemes(args),
                "+version" => ShowVersion(),
                _ => UnknownAction(args[0]),
            };
        }

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

            // Also write to a file next to the exe in case stderr is lost
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "ghostty-crash.log");
                File.WriteAllText(logPath, msg);
            }
            catch { /* best effort */ }

            return 1;
        }
    }

    /// <summary>
    /// List available themes from all known theme directories.
    /// Matches the upstream <c>ghostty +list-themes --plain</c> output.
    /// </summary>
    private static int ListThemes(string[] args)
    {
        var showPath = args.Contains("--path");
        var themes = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        // User themes: %APPDATA%/ghostty/themes/
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        var userThemesDir = Path.Combine(appData, "ghostty", "themes");

        // Config-relative themes: <config_dir>/themes/
        var configDir = Path.Combine(appData, "ghostty");
        var configThemesDir = Path.Combine(configDir, "themes");

        // Collect from both locations. User themes override bundled
        // ones with the same name (user dir is checked first).
        foreach (var dir in new[] { userThemesDir, configThemesDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (name is null) continue;
                themes.TryAdd(name, file);
            }
        }

        if (themes.Count == 0)
        {
            Console.Error.WriteLine("No themes found.");
            Console.Error.WriteLine($"Place theme files in: {userThemesDir}");
            return 1;
        }

        foreach (var (name, path) in themes)
        {
            Console.WriteLine(showPath ? path : name);
        }

        return 0;
    }

    private static int ShowVersion()
    {
        Console.WriteLine("Ghostty (Windows)");
        return 0;
    }

    private static int UnknownAction(string action)
    {
        Console.Error.WriteLine($"Unknown action: {action}");
        Console.Error.WriteLine("Available actions: +list-themes, +version");
        return 1;
    }
}
