using System;
using System.Runtime.InteropServices;
using Ghostty.Controls;
using Ghostty.Hosting;
using Ghostty.Panes;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Ghostty;

public sealed partial class MainWindow : Window
{
    private readonly GhosttyHost _host;
    private readonly PaneHost _paneHost;
    private LeafPane? _activeLeaf;

    // Win32 interop for the window class background brush. WinUI 3 hosts
    // the XAML island inside a Win32 HWND whose WNDCLASS hbrBackground
    // defaults to white. During an interactive drag-resize, DWM paints
    // any uncovered window pixels with that brush BEFORE WinUI 3 gets a
    // chance to extend its XAML content into the new area, producing a
    // visible white flash at the leading edge of the drag. Replacing the
    // class brush with a dark solid brush makes the flash invisible
    // against any dark color scheme.
    private const int GCLP_HBRBACKGROUND = -10;

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    public MainWindow()
    {
        InitializeComponent();

        _host = new GhosttyHost(DispatcherQueue);

        // Match the RootGrid background (#0C0C0C). Win32 COLORREF is 0x00BBGGRR.
        var hwnd = WindowNative.GetWindowHandle(this);
        var brush = CreateSolidBrush(0x000C0C0Cu);
        SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, brush);

        // Mica is only available on Windows 11 22H1+ with a supported GPU.
        // Our TargetPlatformMinVersion is still 19041 (Windows 10), so a
        // bare `new MicaBackdrop()` would silently fail on older systems
        // and leave the window with a transparent black backdrop. Probe
        // MicaController.IsSupported() and skip the backdrop on unsupported
        // hosts; XAML's default Window background takes over.
        //
        // Custom title bar with ExtendsContentIntoTitleBar comes in a
        // follow-up PR so we can focus on the terminal plumbing here.
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        _paneHost = new PaneHost(_host, terminalFactory: () => new TerminalControl());
        RootGrid.Children.Add(_paneHost);

        _paneHost.LeafFocused += OnLeafFocused;
        _paneHost.LastLeafClosed += (_, _) => Close();

        Closed += (_, _) => _host.Dispose();
    }

    /// <summary>
    /// Re-route the window title when the active pane changes. We
    /// unsubscribe the previous active leaf's TitleChanged so a
    /// background pane setting its title via OSC does not affect the
    /// window chrome.
    /// </summary>
    private void OnLeafFocused(object? sender, LeafPane leaf)
    {
        if (_activeLeaf is { } previous)
            previous.Terminal.TitleChanged -= OnActiveLeafTitleChanged;
        _activeLeaf = leaf;
        leaf.Terminal.TitleChanged += OnActiveLeafTitleChanged;
        Title = leaf.Terminal.CurrentTitle ?? "Ghostty";
    }

    private void OnActiveLeafTitleChanged(object? sender, string title)
    {
        Title = title;
    }
}
