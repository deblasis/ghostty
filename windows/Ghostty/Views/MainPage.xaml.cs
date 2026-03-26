using System.Runtime.InteropServices;
using Ghostty.Interop;
using Microsoft.UI.Xaml.Input;

namespace Ghostty.Views;

public partial class MainPage : Page
{
    private bool _initialized;

    public MainPage()
    {
        this.InitializeComponent();
        TerminalPanel.Loaded += OnPanelLoaded;
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;

        Console.WriteLine($"[Ghostty] Panel loaded: {TerminalPanel.ActualWidth}x{TerminalPanel.ActualHeight}, scale={TerminalPanel.CompositionScaleX}");

        IntPtr panelNative;
        try
        {
            panelNative = Marshal.GetComInterfaceForObject<SwapChainPanel, ISwapChainPanelNative>(TerminalPanel);
            Console.WriteLine($"[Ghostty] Got ISwapChainPanelNative: 0x{panelNative:X}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ghostty] COM QI failed: {ex.GetType().Name}: {ex.Message}");
            // Fallback: try via IUnknown + QueryInterface
            Console.WriteLine("[Ghostty] Trying IUnknown fallback...");
            var unknown = Marshal.GetIUnknownForObject(TerminalPanel);
            var iid = typeof(ISwapChainPanelNative).GUID;
            var hr = Marshal.QueryInterface(unknown, ref iid, out panelNative);
            Marshal.Release(unknown);
            if (hr != 0)
            {
                Console.WriteLine($"[Ghostty] QI fallback also failed: HRESULT=0x{hr:X8}");
                return;
            }
            Console.WriteLine($"[Ghostty] QI fallback worked: 0x{panelNative:X}");
        }

        try
        {
            var width = (uint)Math.Max(1, TerminalPanel.ActualWidth);
            var height = (uint)Math.Max(1, TerminalPanel.ActualHeight);
            var scale = (float)TerminalPanel.CompositionScaleX;

            Console.WriteLine($"[Ghostty] Calling ghostty_spike_init({panelNative:X}, {width}, {height}, {scale})");
            var ok = LibGhostty.SpikeInit(panelNative, width, height, scale);
            Console.WriteLine($"[Ghostty] ghostty_spike_init returned: {ok}");
            if (!ok)
            {
                return;
            }

            _initialized = true;
            Console.WriteLine("[Ghostty] Initialization complete!");
        }
        finally
        {
            Marshal.Release(panelNative);
        }
    }

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized) return;
        var w = (uint)Math.Max(1, e.NewSize.Width);
        var h = (uint)Math.Max(1, e.NewSize.Height);
        LibGhostty.SpikeResize(w, h);
    }

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (!_initialized) return;
        LibGhostty.SpikeDpiChanged((float)sender.CompositionScaleX);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_initialized) return;
        LibGhostty.SpikeKeyPress((uint)e.Key);
        e.Handled = true;
    }
}
