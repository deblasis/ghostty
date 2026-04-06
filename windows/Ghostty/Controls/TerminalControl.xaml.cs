using System;
using System.Runtime.InteropServices;
using System.Text;
using Ghostty.Interop;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Ghostty.Controls;

/// <summary>
/// Single libghostty-backed terminal surface, hosted via the WinUI 3
/// SwapChainPanel composition path (null HWND). Matches how macOS's
/// Ghostty.Surface.swift owns one ghostty_surface_t per SwiftUI view.
///
/// This first pass owns the whole ghostty_app_t lifetime too; when we add
/// tabs/splits the app handle will move up to MainWindow and each control
/// will only own its surface.
/// </summary>
public sealed partial class TerminalControl : UserControl
{
    // Handles ------------------------------------------------------------

    private GhosttyConfig _config;
    private GhosttyApp _app;
    private GhosttySurface _surface;
    private IntPtr _emptyCString;
    private bool _initialized;

    // Keep delegates alive for as long as the runtime config references
    // them. P/Invoke marshals the managed delegate to a native function
    // pointer that the GC has no way of tracking, so a dropped field here
    // would crash the Zig side on the first wakeup.
    private GhosttyWakeupCb? _wakeupCb;
    private GhosttyActionCb? _actionCb;
    private GhosttyReadClipboardCb? _readClipboardCb;
    private GhosttyConfirmReadClipboardCb? _confirmReadClipboardCb;
    private GhosttyWriteClipboardCb? _writeClipboardCb;
    private GhosttyCloseSurfaceCb? _closeSurfaceCb;

    public TerminalControl()
    {
        InitializeComponent();
    }

    // Lifecycle ----------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        // 1) ghostty_init — one-time per process, but calling multiple
        //    times is documented as safe.
        _ = NativeMethods.Init(UIntPtr.Zero, IntPtr.Zero);

        // 2) Build the global config. Default files are loaded from the
        //    standard locations; we skip CLI args because WinUI 3's entry
        //    point swallows them before we get here.
        _config = NativeMethods.ConfigNew();
        NativeMethods.ConfigLoadDefaultFiles(_config);
        NativeMethods.ConfigFinalize(_config);

        // 3) Runtime config. The Zig side requires non-null callbacks even
        //    if they are no-ops; storing each delegate in a field prevents
        //    GC from freeing it while libghostty still holds the pointer.
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

        // 4) Surface config. swap_chain_panel takes the panel's IUnknown
        //    pointer; libghostty QIs for ISwapChainPanelNative internally.
        //    The string fields (working_directory, command, initial_input)
        //    must be non-null: Zig dereferences them unconditionally. The
        //    macOS side passes empty C strings via withCString; we do the
        //    same by allocating a single null-terminated byte and pointing
        //    all three at it. These live until the surface is freed.
        var surfaceConfig = NativeMethods.SurfaceConfigNew();
        surfaceConfig.PlatformTag = GhosttyPlatform.Windows;
        surfaceConfig.Platform.Windows = new GhosttyPlatformWindows
        {
            Hwnd = IntPtr.Zero,
            SwapChainPanel = SwapChainPanelInterop.QueryInterface(Panel),
            SharedTextureOut = IntPtr.Zero,
            TextureWidth = 0,
            TextureHeight = 0,
        };
        surfaceConfig.ScaleFactor = Panel.CompositionScaleX > 0 ? Panel.CompositionScaleX : 1.0;
        surfaceConfig.Context = GhosttySurfaceContext.Window;

        _emptyCString = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(_emptyCString, 0);
        surfaceConfig.WorkingDirectory = _emptyCString;
        surfaceConfig.Command = _emptyCString;
        surfaceConfig.InitialInput = _emptyCString;

        _surface = NativeMethods.SurfaceNew(_app, surfaceConfig);

        // Request focus so keyboard input starts flowing immediately.
        Panel.Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Tear down in reverse order. Each free is a no-op on a zero
        // handle so we do not need to guard individually.
        if (_surface.Handle != IntPtr.Zero) NativeMethods.SurfaceFree(_surface);
        if (_app.Handle != IntPtr.Zero) NativeMethods.AppFree(_app);
        if (_config.Handle != IntPtr.Zero) NativeMethods.ConfigFree(_config);
        if (_emptyCString != IntPtr.Zero) Marshal.FreeHGlobal(_emptyCString);

        _surface = default;
        _app = default;
        _config = default;
        _emptyCString = IntPtr.Zero;
        _initialized = false;
    }

    // Runtime callbacks --------------------------------------------------
    //
    // These fire on libghostty's thread. For this first pass we implement
    // the minimum required to avoid null-deref on the Zig side. Marshaling
    // back to the UI thread for things like clipboard and actions will be
    // added when those features are wired.

    private void OnWakeup(IntPtr userdata)
    {
        // libghostty is asking us to pump its event loop. We do it on the
        // UI thread so any resulting draws land on the right dispatcher.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_app.Handle != IntPtr.Zero) NativeMethods.AppTick(_app);
        });
    }

    private bool OnAction(GhosttyApp app, GhosttyTarget target, IntPtr actionPtr)
    {
        // Actions dispatch is not wired yet. Returning true tells ghostty
        // the action was handled so it does not fall back to a default
        // that we do not implement.
        return true;
    }

    private bool OnReadClipboard(IntPtr userdata, GhosttyClipboard kind, IntPtr state) => false;
    private void OnConfirmReadClipboard(IntPtr userdata, IntPtr str, IntPtr state, GhosttyClipboardRequest req) { }
    private void OnWriteClipboard(IntPtr userdata, GhosttyClipboard kind, IntPtr content, UIntPtr count, bool confirm) { }
    private void OnCloseSurface(IntPtr userdata, bool processAlive) { }

    // Size / scale -------------------------------------------------------

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _resizeTimer;
    private Windows.Foundation.Size _pendingSize;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_surface.Handle == IntPtr.Zero) return;

        // Debounce: WinUI 3 fires SizeChanged per pixel during a drag.
        // libghostty's DX12 renderer recreates the swap chain on every
        // set_size, which crashes under that kind of storm. Coalesce to a
        // single resize ~30ms after the last event fires.
        _pendingSize = e.NewSize;
        _resizeTimer ??= DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(30);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick -= OnResizeTick;
        _resizeTimer.Tick += OnResizeTick;
        _resizeTimer.Start();
    }

    private void OnResizeTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_surface.Handle == IntPtr.Zero) return;
        var sx = Panel.CompositionScaleX > 0 ? Panel.CompositionScaleX : 1.0;
        var sy = Panel.CompositionScaleY > 0 ? Panel.CompositionScaleY : 1.0;
        var w = (uint)Math.Max(1, _pendingSize.Width * sx);
        var h = (uint)Math.Max(1, _pendingSize.Height * sy);
        NativeMethods.SurfaceSetSize(_surface, w, h);
    }

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_surface.Handle == IntPtr.Zero) return;
        NativeMethods.SurfaceSetContentScale(_surface, sender.CompositionScaleX, sender.CompositionScaleY);
    }

    // Focus --------------------------------------------------------------

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (_surface.Handle == IntPtr.Zero) return;
        NativeMethods.SurfaceSetFocus(_surface, true);
        if (_app.Handle != IntPtr.Zero) NativeMethods.AppSetFocus(_app, true);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_surface.Handle == IntPtr.Zero) return;
        NativeMethods.SurfaceSetFocus(_surface, false);
        if (_app.Handle != IntPtr.Zero) NativeMethods.AppSetFocus(_app, false);
    }

    // Mouse --------------------------------------------------------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Panel.Focus(FocusState.Pointer);
        SendMouseButton(e, GhosttyMouseState.Press);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e) =>
        SendMouseButton(e, GhosttyMouseState.Release);

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_surface.Handle == IntPtr.Zero) return;
        var pt = e.GetCurrentPoint(Panel).Position;
        NativeMethods.SurfaceMousePos(
            _surface,
            pt.X * Panel.CompositionScaleX,
            pt.Y * Panel.CompositionScaleY,
            CurrentMods());
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_surface.Handle == IntPtr.Zero) return;
        var pt = e.GetCurrentPoint(Panel);
        var delta = pt.Properties.MouseWheelDelta / 120.0;  // 120 = one notch
        var isHorizontal = pt.Properties.IsHorizontalMouseWheel;
        NativeMethods.SurfaceMouseScroll(
            _surface,
            isHorizontal ? delta : 0.0,
            isHorizontal ? 0.0 : delta,
            0);  // scroll mods packed bitfield - wire when we need them
    }

    private void SendMouseButton(PointerRoutedEventArgs e, GhosttyMouseState state)
    {
        if (_surface.Handle == IntPtr.Zero) return;
        var props = e.GetCurrentPoint(Panel).Properties;
        GhosttyMouseButton btn = GhosttyMouseButton.Unknown;
        // Pick whichever button changed in this event. For Press/Release
        // only one bit flips, so "IsLeftButtonPressed == (state == Press)"
        // is the right test; but we can shortcut using PointerUpdateKind.
        btn = props.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed or
            PointerUpdateKind.LeftButtonReleased => GhosttyMouseButton.Left,
            PointerUpdateKind.RightButtonPressed or
            PointerUpdateKind.RightButtonReleased => GhosttyMouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed or
            PointerUpdateKind.MiddleButtonReleased => GhosttyMouseButton.Middle,
            _ => GhosttyMouseButton.Unknown,
        };
        if (btn == GhosttyMouseButton.Unknown) return;
        NativeMethods.SurfaceMouseButton(_surface, state, btn, CurrentMods());
    }

    // Keyboard -----------------------------------------------------------

    private void OnKeyDown(object sender, KeyRoutedEventArgs e) =>
        SendKey(e, GhosttyInputAction.Press);

    private void OnKeyUp(object sender, KeyRoutedEventArgs e) =>
        SendKey(e, GhosttyInputAction.Release);

    private void SendKey(KeyRoutedEventArgs e, GhosttyInputAction action)
    {
        if (_surface.Handle == IntPtr.Zero) return;

        // First-pass key routing mirrors the WM_CHAR-combining fix landed
        // in the Win32 example: we send the key event itself here, and the
        // CharacterReceived handler below sends the translated text when
        // it arrives. GhosttyKey mapping is intentionally skipped for now
        // because ghostty can accept a bare keycode (Keycode field) with
        // text filled in from CharacterReceived separately.
        var key = new GhosttyInputKey
        {
            Action = action,
            Mods = CurrentMods(),
            ConsumedMods = GhosttyMods.None,
            Keycode = (uint)e.OriginalKey,
            Text = IntPtr.Zero,
            UnshiftedCodepoint = 0,
            Composing = false,
        };
        var handled = NativeMethods.SurfaceKey(_surface, key);
        if (handled) e.Handled = true;
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (_surface.Handle == IntPtr.Zero) return;

        // Filter C0 control chars: these are already produced by the key
        // event path (Ctrl+C, Backspace, etc). Mirrors the fix in
        // 8dde86df5 for the Win32 example.
        var ch = e.Character;
        if (ch < 0x20 || ch == 0x7F) return;

        Span<byte> buf = stackalloc byte[4];
        var len = new Rune(ch).EncodeToUtf8(buf);
        unsafe
        {
            fixed (byte* p = buf)
            {
                NativeMethods.SurfaceText(_surface, (IntPtr)p, (UIntPtr)len);
            }
        }
    }

    // Mods helper --------------------------------------------------------

    private static GhosttyMods CurrentMods()
    {
        // Use Win32 GetKeyState directly. WinUI 3's InputKeyboardSource
        // surface has moved several times between releases; Win32 is
        // stable and cheap (reads a thread-local state table).
        var mods = GhosttyMods.None;
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) mods |= GhosttyMods.Shift;
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) mods |= GhosttyMods.Ctrl;
        if ((GetKeyState(VK_MENU) & 0x8000) != 0) mods |= GhosttyMods.Alt;
        if ((GetKeyState(VK_LWIN) & 0x8000) != 0) mods |= GhosttyMods.Super;
        if ((GetKeyState(VK_RWIN) & 0x8000) != 0) mods |= GhosttyMods.Super;
        return mods;
    }

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;  // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
}
