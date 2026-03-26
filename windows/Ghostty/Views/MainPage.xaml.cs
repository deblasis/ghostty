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

        // Get ISwapChainPanelNative COM pointer
        var panelNative = Marshal.GetComInterfaceForObject<SwapChainPanel, ISwapChainPanelNative>(TerminalPanel);

        try
        {
            var width = (uint)Math.Max(1, TerminalPanel.ActualWidth);
            var height = (uint)Math.Max(1, TerminalPanel.ActualHeight);
            var scale = (float)TerminalPanel.CompositionScaleX;

            var ok = LibGhostty.SpikeInit(panelNative, width, height, scale);
            if (!ok)
            {
                System.Diagnostics.Debug.WriteLine("ghostty_spike_init failed");
                return;
            }

            _initialized = true;
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
        LibGhostty.SpikeKeyPress();
    }
}
