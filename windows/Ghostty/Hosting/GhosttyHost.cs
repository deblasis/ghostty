using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ghostty.Controls;
using Ghostty.Interop;
using Microsoft.UI.Dispatching;

namespace Ghostty.Hosting;

/// <summary>
/// Per-window owner of the libghostty config + app handles and the
/// runtime callback surface. Holds a dictionary mapping
/// <see cref="GhosttySurface"/> handles to the <see cref="TerminalControl"/>
/// that owns them so action-callback <c>target</c> arguments can be routed
/// to the correct leaf.
///
/// Lifetime: created once by <see cref="MainWindow"/> before any terminal
/// surface is constructed, disposed when the window closes. The app handle
/// is passed to each <see cref="TerminalControl"/> via its
/// <see cref="TerminalControl.Host"/> property before it is loaded.
/// </summary>
internal sealed class GhosttyHost : IDisposable
{
    private GhosttyConfig _config;
    private GhosttyApp _app;

    // Delegates must be retained as fields; P/Invoke hands out native
    // function pointers the GC cannot track.
    private GhosttyWakeupCb? _wakeupCb;
    private GhosttyActionCb? _actionCb;
    private GhosttyReadClipboardCb? _readClipboardCb;
    private GhosttyConfirmReadClipboardCb? _confirmReadClipboardCb;
    private GhosttyWriteClipboardCb? _writeClipboardCb;
    private GhosttyCloseSurfaceCb? _closeSurfaceCb;

    private readonly Dictionary<IntPtr, TerminalControl> _surfaces = new();
    private readonly DispatcherQueue _dispatcher;

    public GhosttyApp App => _app;

    public GhosttyHost(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        NativeMethods.Init(UIntPtr.Zero, IntPtr.Zero);

        _config = NativeMethods.ConfigNew();
        NativeMethods.ConfigLoadDefaultFiles(_config);
        NativeMethods.ConfigFinalize(_config);

        _wakeupCb = OnWakeup;
        _actionCb = OnAction;
        _readClipboardCb = OnReadClipboard;
        _confirmReadClipboardCb = OnConfirmReadClipboard;
        _writeClipboardCb = OnWriteClipboard;
        _closeSurfaceCb = OnCloseSurface;

        var runtime = new GhosttyRuntimeConfig
        {
            Userdata = IntPtr.Zero,
            SupportsSelectionClipboard = false,
            WakeupCb = Marshal.GetFunctionPointerForDelegate(_wakeupCb),
            ActionCb = Marshal.GetFunctionPointerForDelegate(_actionCb),
            ReadClipboardCb = Marshal.GetFunctionPointerForDelegate(_readClipboardCb),
            ConfirmReadClipboardCb = Marshal.GetFunctionPointerForDelegate(_confirmReadClipboardCb),
            WriteClipboardCb = Marshal.GetFunctionPointerForDelegate(_writeClipboardCb),
            CloseSurfaceCb = Marshal.GetFunctionPointerForDelegate(_closeSurfaceCb),
        };

        _app = NativeMethods.AppNew(runtime, _config);
    }

    public void Register(GhosttySurface surface, TerminalControl control)
    {
        if (surface.Handle == IntPtr.Zero) return;
        _surfaces[surface.Handle] = control;
    }

    public void Unregister(GhosttySurface surface)
    {
        if (surface.Handle == IntPtr.Zero) return;
        _surfaces.Remove(surface.Handle);
    }

    public void Dispose()
    {
        _surfaces.Clear();
        if (_app.Handle != IntPtr.Zero) NativeMethods.AppFree(_app);
        if (_config.Handle != IntPtr.Zero) NativeMethods.ConfigFree(_config);
        _app = default;
        _config = default;
        _wakeupCb = null;
        _actionCb = null;
        _readClipboardCb = null;
        _confirmReadClipboardCb = null;
        _writeClipboardCb = null;
        _closeSurfaceCb = null;
    }

    // Runtime callbacks - implemented in Task 2.
    private void OnWakeup(IntPtr userdata) { }
    private bool OnAction(GhosttyApp _, IntPtr targetPtr, IntPtr actionPtr) => false;
    private bool OnReadClipboard(IntPtr userdata, GhosttyClipboard kind, IntPtr state) => false;
    private void OnConfirmReadClipboard(IntPtr userdata, IntPtr str, IntPtr state, GhosttyClipboardRequest req) { }
    private void OnWriteClipboard(IntPtr userdata, GhosttyClipboard kind, IntPtr content, UIntPtr count, bool confirm) { }
    private void OnCloseSurface(IntPtr userdata, bool processAlive) { }
}
