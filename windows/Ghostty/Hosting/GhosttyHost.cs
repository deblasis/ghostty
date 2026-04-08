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

    private void OnWakeup(IntPtr userdata)
    {
        // Fires on libghostty's thread. Hop to the UI dispatcher so the
        // tick (and any resulting draws) lands on the right queue.
        _dispatcher.TryEnqueue(() =>
        {
            if (_app.Handle != IntPtr.Zero) NativeMethods.AppTick(_app);
        });
    }

    // ghostty_target_s tag values, mirroring ghostty.h ghostty_target_tag_e.
    private const int GhosttyTargetApp = 0;
    private const int GhosttyTargetSurface = 1;

    private bool OnAction(GhosttyApp _, IntPtr targetPtr, IntPtr actionPtr)
    {
        // ABI note: ghostty_runtime_action_cb is declared as
        //
        //   bool action_cb(ghostty_app_t, ghostty_target_s, ghostty_action_s);
        //
        // Both target and action are passed BY VALUE in C, but on the Windows
        // x64 calling convention any struct larger than 8 bytes is passed via
        // a hidden pointer to a caller-allocated copy. ghostty_target_s is
        // 16 bytes:
        //
        //   struct ghostty_target_s {
        //     ghostty_target_tag_e tag;   // int32 at offset 0
        //     // 4 bytes padding
        //     union {                     // 8-byte aligned
        //       ghostty_surface_t surface; // pointer at offset 8
        //     } target;
        //   };
        //
        // ghostty_action_s is similarly oversized. The C# delegate therefore
        // declares both as IntPtr - the actual pointers we receive point at
        // ephemeral stack copies of the structs and must be DEREFERENCED to
        // get at their contents. Treating targetPtr as if it were the surface
        // handle silently misses every dictionary lookup.
        if (actionPtr == IntPtr.Zero || targetPtr == IntPtr.Zero) return false;

        var targetTag = Marshal.ReadInt32(targetPtr);
        if (targetTag != GhosttyTargetSurface) return false;
        var surfaceHandle = Marshal.ReadIntPtr(targetPtr, 8);
        if (!_surfaces.TryGetValue(surfaceHandle, out var control)) return false;

        // ghostty_action_s layout: { int32 tag; <union> action; }
        // Union starts at offset 8 (8-byte aligned on x64).
        var tag = (GhosttyActionTag)Marshal.ReadInt32(actionPtr);
        switch (tag)
        {
            case GhosttyActionTag.SetTitle:
            {
                var titlePtr = Marshal.ReadIntPtr(actionPtr, 8);
                var title = Marshal.PtrToStringUTF8(titlePtr) ?? string.Empty;
                _dispatcher.TryEnqueue(() => control.RaiseTitleChanged(title));
                return true;
            }

            case GhosttyActionTag.RingBell:
            {
                NativeMethods.MessageBeep(NativeMethods.MB_OK);
                return true;
            }

            case GhosttyActionTag.CloseWindow:
            {
                _dispatcher.TryEnqueue(() => control.RaiseCloseRequested());
                return true;
            }

            default:
                return false;
        }
    }

    private bool OnReadClipboard(IntPtr userdata, GhosttyClipboard kind, IntPtr state) => false;
    private void OnConfirmReadClipboard(IntPtr userdata, IntPtr str, IntPtr state, GhosttyClipboardRequest request) { }
    private void OnWriteClipboard(IntPtr userdata, GhosttyClipboard kind, IntPtr content, UIntPtr count, bool confirm) { }

    private void OnCloseSurface(IntPtr userdata, bool processAlive)
    {
        // userdata is the GCHandle.ToIntPtr value the owning TerminalControl
        // pinned for itself before SurfaceNew. Decode it back to the managed
        // control and raise CloseRequested on the UI thread; MainWindow's
        // CloseRequested handler is what actually closes the window.
        //
        // This callback fires from libghostty's thread on two paths today:
        // (1) the user typed `exit` in the shell and then pressed any key,
        //     which makes Surface.zig encode the keystroke, notice
        //     child_exited, and call self.close().
        // (2) a binding action ran .close_surface or .close_window.
        //
        // We deliberately ignore processAlive here. Confirm-on-close lives
        // at the libghostty layer and only invokes us once the user has
        // already agreed (or wait_after_command was off).
        if (userdata == IntPtr.Zero) return;
        var control = GCHandle.FromIntPtr(userdata).Target as TerminalControl;
        if (control is null) return;
        _dispatcher.TryEnqueue(() => control.RaiseCloseRequested());
    }
}
