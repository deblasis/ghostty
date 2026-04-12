using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Ghostty.Commands;
using Ghostty.Controls;
using Ghostty.Core.Hosting;
using Ghostty.Core.Tabs;
using Ghostty.Dialogs;
using Ghostty.Hosting;
using Ghostty.Interop;
using Ghostty.Input;
using Ghostty.Core.Panes;
using Ghostty.Services;
using Ghostty.Panes;
using Ghostty.Settings;
using Ghostty.Shell;
using Ghostty.Tabs;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Ghostty;

/// <summary>
/// Composition root for the WinUI 3 shell. MainWindow holds the
/// XAML structural elements (declared in MainWindow.xaml), wires
/// the libghostty host, the tab manager, the two tab hosts, the
/// pane action router, and three coordinators that own the rest
/// of the cross-cutting plumbing:
///
///   - <see cref="LayoutCoordinator"/> handles the runtime switch
///     between horizontal and vertical layouts (cross-fade
///     Storyboard + strip-column tween + concurrent-tween guard).
///   - <see cref="TitleBarCoordinator"/> owns the title bar
///     drag-region selection, the caption-inset DPI sync, the
///     active-leaf TitleChanged hook, and the vertical-mode title
///     TextBlock binding.
///   - <see cref="TaskbarHost"/> wires the Ghostty.Core taskbar
///     progress coordinator into ITaskbarList3.
///
/// MainWindow itself only owns construction order, the dialog
/// tracker, the keyboard accelerator install, and the Win32 class
/// brush hack for resize flicker.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly GhosttyHost _host;
    private readonly ConfigService _configService;
    private readonly ConfigFileEditor _configEditor;
    private readonly PaneHostFactory _factory;
    private readonly TabManager _tabManager;
    private readonly PaneActionRouter _router;
    private readonly DialogTracker _dialogs = new();
    private readonly UiSettings _uiSettings;
    // Kept as a field so the ColorValuesChanged subscription is not GC'd.
    private readonly Windows.UI.ViewManagement.UISettings _systemUiSettings;

    private readonly TabHost _horizontalTabHost;
    private readonly VerticalTabHost _verticalTabHost;
    private ITabHost _tabHost;

    private readonly LayoutCoordinator _layout;
    private readonly TitleBarCoordinator _titleBar;
    private readonly TaskbarHost _taskbar;
    private readonly WindowThemeManager _themeManager;

    private CommandPaletteViewModel? _commandPaletteVm;
    private FrecencyStore? _frecencyStore;
    private Controls.TerminalControl? _previousFocusSurface;

    /// <summary>
    /// Palette close state: prevents re-entrant close handling between
    /// ViewModel.PropertyChanged and Popup.Closed callbacks.
    /// </summary>
    private enum PaletteCloseState { Idle, ClosingFromCommand, ClosingFromToggle }
    private PaletteCloseState _paletteCloseState;

    // Dedup guard for KeyboardAccelerator double-dispatch. WinUI 3
    // fires accelerator Invoked twice for a single key event when the
    // accelerator is registered on a parent of the focused element,
    // even with args.Handled = true and ScopeOwner explicitly set.
    private PaneAction? _acceleratorFiredThisKeyDown;

    // Win32 interop for the window class background brush.

    // Captured from Content.XamlRoot at registration time (in the
    // one-shot Content.Loaded handler). Read by App.OnAnyWindowClosedInternal
    // on Window.Closed to remove the WindowsByRoot entry; reading
    // Content.XamlRoot directly at Closed time can return null because
    // WinUI 3 tears Content down before Closed fires.
    internal XamlRoot? RegisteredRoot { get; private set; }

    internal MainWindow(ConfigService configService, GhosttyHost bootstrapHost, HostLifetimeSupervisor supervisor)
        : this(configService, bootstrapHost, supervisor, seedTab: null)
    {
    }

    /// <summary>
    /// Full ctor. <paramref name="seedTab"/>, when non-null, is
    /// adopted as the sole initial tab (used by Move Tab to New
    /// Window); when null, the normal "create a fresh tab via the
    /// factory" path runs. <paramref name="bootstrapHost"/> is the
    /// app-owning GhosttyHost built once in App.xaml.cs; this window
    /// constructs its OWN per-window GhosttyHost from it using the
    /// shared-app ctor.
    /// </summary>
    private MainWindow(
        ConfigService configService,
        GhosttyHost bootstrapHost,
        HostLifetimeSupervisor supervisor,
        TabModel? seedTab)
    {
        InitializeComponent();

        _configService = configService;
        _configEditor = new ConfigFileEditor(configService.ConfigFilePath);

        // Build this window's per-window GhosttyHost around the shared
        // app. Each per-window host has its OWN per-window surface
        // dictionary; routing to this host from the bootstrap host's
        // libghostty callbacks happens via App._hostBySurface (populated
        // by the per-window host's Register / Adopt paths).
        _host = new GhosttyHost(
            DispatcherQueue,
            bootstrapHost.App.Handle,
            supervisor);
        // NOTE: configService.SetApp is already done by App.xaml.cs on
        // the bootstrap host. We do NOT call it again here.

        // Register with App.WindowsByRoot once the XamlRoot is live.
        // We capture the XamlRoot into RegisteredRoot so the
        // App.OnAnyWindowClosedInternal handler can remove the entry on
        // Window.Closed even if Content.XamlRoot has gone null by then
        // (which WinUI 3 does during window teardown).
        if (Content is FrameworkElement fe)
        {
            fe.Loaded += OnContentLoadedOnce;
            void OnContentLoadedOnce(object s, RoutedEventArgs e)
            {
                fe.Loaded -= OnContentLoadedOnce;
                RegisteredRoot = fe.XamlRoot;
                if (RegisteredRoot != null)
                {
                    App.WindowsByRoot[RegisteredRoot] = this;
                }
            }
        }

        // Detect initial system theme and notify libghostty so conditional
        // config blocks (e.g. palette dark/light) take effect immediately.
        _systemUiSettings = new Windows.UI.ViewManagement.UISettings();
        var initialFg = _systemUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
        var initialDark = initialFg.R > 128;
        Ghostty.Interop.NativeMethods.AppSetColorScheme(
            _host.App,
            initialDark ? Ghostty.Interop.GhosttyColorScheme.Dark : Ghostty.Interop.GhosttyColorScheme.Light);

        // Subscribe to runtime theme changes.
        _systemUiSettings.ColorValuesChanged += (s, _) =>
        {
            var fg = s.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
            var dark = fg.R > 128;
            DispatcherQueue.TryEnqueue(() =>
                Ghostty.Interop.NativeMethods.AppSetColorScheme(
                    _host.App,
                    dark ? Ghostty.Interop.GhosttyColorScheme.Dark : Ghostty.Interop.GhosttyColorScheme.Light));
        };

        // Match the RootGrid background (#0C0C0C). Win32 COLORREF is 0x00BBGGRR.
        var hwnd = new HWND(WindowNative.GetWindowHandle(this));
        var brush = PInvoke.CreateSolidBrush(new COLORREF(0x000C0C0Cu));
        PInvoke.SetClassLongPtr(hwnd, GET_CLASS_LONG_INDEX.GCLP_HBRBACKGROUND, brush);

        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        ExtendsContentIntoTitleBar = true;

        _themeManager = new WindowThemeManager(configService, DispatcherQueue);
        ApplyTheme();
        _themeManager.ThemeChanged += _ => ApplyTheme();

        _factory = new PaneHostFactory(_host);
        _tabManager = new TabManager(
            () => _factory.Create(),
            seed: seedTab);
        _router = new PaneActionRouter(_tabManager);
        _uiSettings = UiSettings.Load();

        _horizontalTabHost = new TabHost(_tabManager, _router, _dialogs);
        _verticalTabHost = new VerticalTabHost(_tabManager, _router, _dialogs, _host);

        var hostElement = (FrameworkElement)_horizontalTabHost.HostElement;
        Grid.SetRow(hostElement, 0);
        Grid.SetColumn(hostElement, 0);
        Grid.SetColumnSpan(hostElement, 2);
        Canvas.SetZIndex(hostElement, -1);
        RootGrid.Children.Add(hostElement);

        Grid.SetRow(_verticalTabHost, 0);
        Grid.SetColumn(_verticalTabHost, 0);
        Grid.SetRowSpan(_verticalTabHost, 2);
        Canvas.SetZIndex(_verticalTabHost, -1);
        RootGrid.Children.Add(_verticalTabHost);

        foreach (var t in _tabManager.Tabs) AddPaneHost(t);
        SwapActivePane();
        _tabManager.TabAdded += (_, t) => { AddPaneHost(t); SwapActivePane(); };
        _tabManager.TabRemoved += (_, t) => RemovePaneHost(t);
        _tabManager.ActiveTabChanged += (_, _) => SwapActivePane();

        var chord = KeyBindings.Default.Label(PaneAction.ToggleTabLayout);
        ToolTipService.SetToolTip(
            VerticalSwitchButton,
            chord is null
                ? "Switch to horizontal tabs"
                : $"Switch to horizontal tabs ({chord})");

        _tabHost = _uiSettings.VerticalTabs ? _verticalTabHost : _horizontalTabHost;

        _layout = new LayoutCoordinator(
            StripColumn,
            TitleBarStripMirror,
            (FrameworkElement)_horizontalTabHost.HostElement,
            _verticalTabHost,
            VerticalTitleBar);
        _layout.Snap(_uiSettings.VerticalTabs);

        _titleBar = new TitleBarCoordinator(
            this,
            _tabManager,
            _horizontalTabHost,
            _verticalTabHost,
            VerticalTitleDragRegion,
            VerticalTitleText,
            VerticalCaptionInset,
            isVerticalMode: () => _tabHost is VerticalTabHost);
        _titleBar.ApplyForCurrentMode();
        _titleBar.SyncCaptionInset();

        _taskbar = new TaskbarHost(this, _tabManager);

        AppWindow.Changed += (_, _) =>
        {
            _taskbar.OnAppWindowChanged(AppWindow);
            _titleBar.SyncCaptionInset();
        };

        InstallPaneAccelerators();

        _commandPaletteVm = CreateCommandPaletteViewModel();
        CommandPaletteUI.Bind(_commandPaletteVm);
        CommandPaletteUI.ApplySettings(_uiSettings.CommandPaletteBackground);

        _commandPaletteVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen)) return;
            if (_commandPaletteVm.IsOpen) return;
            if (_paletteCloseState != PaletteCloseState.Idle) return;

            _paletteCloseState = PaletteCloseState.ClosingFromCommand;
            try
            {
                CommandPalettePopup.IsOpen = false;
                SetCommandPaletteOpenOnAllTerminals(false);
                _frecencyStore?.Save();
            }
            finally
            {
                _paletteCloseState = PaletteCloseState.Idle;
            }
        };

        CommandPalettePopup.Closed += (_, _) =>
        {
            if (_paletteCloseState != PaletteCloseState.Idle) return;

            _paletteCloseState = PaletteCloseState.ClosingFromCommand;
            try
            {
                var wasOpen = _commandPaletteVm.IsOpen;
                _commandPaletteVm.Close();
                SetCommandPaletteOpenOnAllTerminals(false);
                if (wasOpen)
                    _previousFocusSurface?.Focus(FocusState.Programmatic);
            }
            finally
            {
                _paletteCloseState = PaletteCloseState.Idle;
            }
        };

        _host.CommandPaletteToggleRequested += (_, _) =>
            DispatcherQueue.TryEnqueue(ToggleCommandPalette);

        _host.OpenConfigRequested += (_, _) =>
        {
            if (configService.SettingsUiEnabled)
            {
                var editor = new ConfigFileEditor(configService.ConfigFilePath);
                var keybindings = new KeyBindingsProvider(configService);
                var themeProvider = new ThemeProvider(configService);
                var settingsWin = new Ghostty.Settings.SettingsWindow(
                    configService, editor, keybindings, themeProvider);
                settingsWin.Activate();
                return;
            }

            var path = configService.ConfigFilePath;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                    });
                }
                catch
                {
                    System.Diagnostics.Process.Start("notepad.exe", path);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to open config file: {ex.Message}");
            }
        };

        _host.ReloadConfigRequested += (_, _) => configService.Reload();

        _host.OpacityAdjustRequested += (_, direction) => AdjustOpacity(direction);

        _tabManager.LastTabClosed += (_, _) => Close();

        Closed += OnClosedAsync;
    }

    /// <summary>
    /// Build a <see cref="MainWindow"/> that adopts an existing
    /// <see cref="TabModel"/> as its sole initial tab, WITHOUT
    /// activating. Caller is responsible for positioning the window
    /// (via <see cref="Microsoft.UI.Windowing.AppWindow.MoveAndResize"/>
    /// or the like) and then calling <see cref="Window.Activate"/>.
    ///
    /// Used today by <see cref="DetachTabToNewWindow"/> for cursor-
    /// anchored placement. PR 201 Snap Layouts will call this same
    /// factory to install a snap rect before first activation so there
    /// is no visible placement flicker.
    /// </summary>
    internal static MainWindow CreateForAdoption(
        ConfigService configService,
        GhosttyHost bootstrapHost,
        HostLifetimeSupervisor supervisor,
        TabModel adoptedTab)
    {
        return new MainWindow(configService, bootstrapHost, supervisor, seedTab: adoptedTab);
    }

    /// <summary>
    /// Move <paramref name="tab"/> out of this window into a brand
    /// new <see cref="MainWindow"/>. The new window is positioned
    /// near the current mouse cursor on the monitor the cursor is
    /// currently on. Disabled (via the menu <c>IsEnabled</c> guard)
    /// when this window has only one tab, because moving the sole
    /// tab into a new window would be a no-op.
    /// </summary>
    internal void DetachTabToNewWindow(TabModel tab)
    {
        if (_tabManager.Tabs.Count <= 1)
            throw new InvalidOperationException(
                "DetachTabToNewWindow: guarded menu fired on single-tab window.");

        // Source-side: detach the model. The manager's TabRemoved
        // subscribers already drain visual state (RemovePaneHost in
        // this MainWindow, RemoveItem in each tab host).
        var detached = _tabManager.DetachTab(tab);

        var bootstrap = App.BootstrapHost
            ?? throw new InvalidOperationException(
                "DetachTabToNewWindow: no bootstrap host; App.OnLaunched did not run.");
        var supervisor = App.LifetimeSupervisor
            ?? throw new InvalidOperationException(
                "DetachTabToNewWindow: no lifetime supervisor; App.OnLaunched did not run.");

        // Rehost the pane tree's terminals to a fresh per-window host
        // built inside the new window. RehostTo is what actually moves
        // the surface entries out of this window's _surfaces into the
        // new window's _surfaces AND rewrites App._hostBySurface.
        var newWindow = MainWindow.CreateForAdoption(_configService, bootstrap, supervisor, detached);
        var newHost = newWindow._host;
        ((Panes.PaneHost)detached.PaneHost).RehostTo(newHost);

        // Cursor-anchored placement. Size = this window's current size
        // so there is no jarring resize.
        var placement = ComputeCursorAnchoredPlacement(newWindow);
        var rect = new Windows.Graphics.RectInt32(
            placement.X, placement.Y, placement.Width, placement.Height);
        newWindow.AppWindow.MoveAndResize(rect);

        // Subscribe the new window to the process-wide last-window-exit
        // handler. WindowsByRoot insertion happens inside the new
        // window's own Content.Loaded handler.
        newWindow.Closed += ((App)Application.Current).OnAnyWindowClosedInternal;

        newWindow.Activate();
    }

    /// <summary>
    /// Compute the cursor-anchored target rect for a newly built
    /// <see cref="MainWindow"/>. Queries <c>GetCursorPos</c> (via
    /// CsWin32), resolves the monitor the cursor is on via
    /// <see cref="Microsoft.UI.Windowing.DisplayArea.GetFromPoint"/>,
    /// and delegates the clamping math to
    /// <see cref="Ghostty.Core.Windows.CursorWindowPlacement.Compute"/>.
    ///
    /// DPI contract: <c>GetCursorPos</c> returns physical pixel
    /// coordinates in virtual desktop space. <c>DisplayArea.GetFromPoint</c>
    /// consumes physical pixels. The two line up without scaling.
    /// </summary>
    private Ghostty.Core.Windows.PlacementRect ComputeCursorAnchoredPlacement(MainWindow target)
    {
        PInvoke.GetCursorPos(out var pt);

        var cursorPoint = new Windows.Graphics.PointInt32(pt.X, pt.Y);
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            cursorPoint,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);

        var work = display?.WorkArea
            ?? new Windows.Graphics.RectInt32(0, 0, 1920, 1080);

        // Inherit the source window's current size.
        var size = AppWindow.Size;

        return Ghostty.Core.Windows.CursorWindowPlacement.Compute(
            cursorX: pt.X,
            cursorY: pt.Y,
            windowWidth: size.Width,
            windowHeight: size.Height,
            workArea: new Ghostty.Core.Windows.WorkAreaRect(
                work.X, work.Y, work.Width, work.Height));
    }

    private async void OnClosedAsync(object sender, WindowEventArgs args)
    {
        // Let any in-flight ContentDialog complete before tearing
        // down the libghostty host.
        try
        {
            if (_dialogs.PendingCount > 0)
                await _dialogs.WhenAllClosedAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DialogTracker drain failed: {ex.Message}");
        }

        _taskbar.Dispose();
        _themeManager.Dispose();

        // Surface lifetime is decoupled from Loaded/Unloaded
        // (see TerminalControl.DisposeSurface), so we have to
        // free every leaf in every tab explicitly before tearing
        // down the libghostty host.
        foreach (var t in _tabManager.Tabs) t.PaneHost.DisposeAllLeaves();
        _host.Dispose();
    }

    private void OnVerticalSwitchButtonClick(object sender, RoutedEventArgs e)
        => _router.RequestToggleTabLayout();

    private void AddPaneHost(TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        paneHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        paneHost.VerticalAlignment = VerticalAlignment.Stretch;
        paneHost.Visibility = Visibility.Collapsed;
        PaneHostContainer.Children.Add(paneHost);
    }

    private void RemovePaneHost(TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        PaneHostContainer.Children.Remove(paneHost);
    }

    private void SwapActivePane()
    {
        var active = (PaneHost)_tabManager.ActiveTab.PaneHost;
        foreach (UIElement child in PaneHostContainer.Children)
        {
            child.Visibility = ReferenceEquals(child, active)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    internal void ToggleTabLayout()
    {
        if (_layout.IsSwitching) return;
        var toVertical = !_uiSettings.VerticalTabs;
        _uiSettings.VerticalTabs = toVertical;
        _uiSettings.Save();
        _tabHost = toVertical ? _verticalTabHost : _horizontalTabHost;

        _layout.Animate(toVertical, onCompleted: () =>
        {
            _titleBar.ApplyForCurrentMode();
            var leaf = _tabManager.ActiveTab?.PaneHost?.ActiveLeaf;
            if (leaf is not null)
                leaf.Terminal().Focus(FocusState.Programmatic);
        });
    }

    private void InstallPaneAccelerators()
    {
        foreach (var binding in KeyBindings.Default.All)
        {
            var captured = binding;
            var accel = new KeyboardAccelerator
            {
                Modifiers = captured.Modifiers,
                Key = captured.Key,
            };
            accel.Invoked += (_, args) =>
            {
                args.Handled = true;
                if (_acceleratorFiredThisKeyDown == captured.Action) return;
                _acceleratorFiredThisKeyDown = captured.Action;
                _router.Invoke(captured.Action);
            };
            RootGrid.KeyboardAccelerators.Add(accel);
        }

        RootGrid.KeyUp += (_, _) => _acceleratorFiredThisKeyDown = null;
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        _router.TabCloseRequestedFromKeyboard += async (_, _) =>
        {
            await _tabHost.RequestCloseTabAsync(_tabManager.ActiveTab);
        };

        _router.ToggleVerticalTabsPinnedRequested += (_, _) =>
        {
            if (_tabHost is VerticalTabHost vth)
                vth.TogglePinnedFromKeyboard();
        };

        _router.ToggleTabLayoutRequested += (_, _) => ToggleTabLayout();

        _router.CommandPaletteToggleRequested += (_, _) => ToggleCommandPalette();

        _router.ToggleFullscreenRequested += (_, _) => ToggleFullscreen();
    }

    private void ApplyTheme()
    {
        if (Content is FrameworkElement root)
            root.RequestedTheme = _themeManager.ElementTheme;
        _themeManager.ApplyToWindow(this);
    }

    private void ToggleFullscreen()
    {
        var kind = AppWindow.Presenter.Kind;
        AppWindow.SetPresenter(
            kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
                ? Microsoft.UI.Windowing.AppWindowPresenterKind.Default
                : Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
    }

    private void AdjustOpacity(int direction)
    {
        const double step = 0.05;
        var current = _configService.BackgroundOpacity;
        var next = direction switch
        {
            0 => 1.0,
            _ => Math.Clamp(current + direction * step, 0.0, 1.0),
        };

        if (Math.Abs(next - current) < 0.001) return;

        _configService.SuppressWatcher(true);
        _configEditor.SetValue("background-opacity", next.ToString("F2"));
        _configService.SuppressWatcher(false);
        _configService.Reload();
    }

    private void ToggleCommandPalette()
    {
        if (_commandPaletteVm is not { } vm) return;

        if (vm.IsOpen)
        {
            _paletteCloseState = PaletteCloseState.ClosingFromToggle;
            try
            {
                vm.Close();
                CommandPalettePopup.IsOpen = false;
                SetCommandPaletteOpenOnAllTerminals(false);
                _previousFocusSurface?.Focus(FocusState.Programmatic);
            }
            finally { _paletteCloseState = PaletteCloseState.Idle; }
        }
        else
        {
            _previousFocusSurface = FocusManager.GetFocusedElement(Content.XamlRoot) as Controls.TerminalControl;

            var windowWidth = AppWindow.Size.Width;
            var paletteWidth = Math.Min(600, windowWidth * 0.9);
            CommandPalettePopup.HorizontalOffset = (windowWidth - paletteWidth) / 2;
            CommandPalettePopup.VerticalOffset = 48;
            CommandPaletteUI.Width = paletteWidth;

            vm.Open();
            CommandPalettePopup.IsOpen = true;
            SetCommandPaletteOpenOnAllTerminals(true);

            DispatcherQueue.TryEnqueue(() => CommandPaletteUI.FocusSearchBox());
        }
    }

    private void SetCommandPaletteOpenOnAllTerminals(bool isOpen)
    {
        foreach (var tab in _tabManager.Tabs)
        {
            var paneHost = (Panes.PaneHost)tab.PaneHost;
            foreach (var leaf in PaneTree.Leaves(paneHost.RootNode))
                leaf.Terminal().CommandPaletteIsOpen = isOpen;
        }
    }

    private CommandPaletteViewModel CreateCommandPaletteViewModel()
    {
        _frecencyStore = FrecencyStore.Load();
        var frecency = _frecencyStore;

        var builtIn = new BuiltInCommandSource(
            paneActionFactory: action => _ => DispatcherQueue.TryEnqueue(() => _router.Invoke(action)),
            bindingActionFactory: actionKey => _ => DispatcherQueue.TryEnqueue(() => ExecuteBindingAction(actionKey)),
            opacityAction: direction => DispatcherQueue.TryEnqueue(() => AdjustOpacity(direction)));

        var jump = new JumpCommandSource(
            _tabManager,
            jumpAction: (tabIdx, _) => DispatcherQueue.TryEnqueue(() => _tabManager.JumpTo(tabIdx)));

        var config = new ConfigCommandSource();

        var sources = new List<ICommandSource> { builtIn, jump, config };

        var schemas = new Dictionary<string, ActionSchema>
        {
            ["reset"] = new() { Name = "reset", Description = "Reset the terminal", RequiresParameter = false },
            ["copy_to_clipboard"] = new() { Name = "copy_to_clipboard", Description = "Copy selection to clipboard", RequiresParameter = false },
            ["paste_from_clipboard"] = new() { Name = "paste_from_clipboard", Description = "Paste from clipboard", RequiresParameter = false },
            ["select_all"] = new() { Name = "select_all", Description = "Select all terminal content", RequiresParameter = false },
            ["increase_font_size"] = new() { Name = "increase_font_size", Description = "Increase font size", RequiresParameter = true, Parameters = ["1", "2"] },
            ["decrease_font_size"] = new() { Name = "decrease_font_size", Description = "Decrease font size", RequiresParameter = true, Parameters = ["1", "2"] },
            ["reset_font_size"] = new() { Name = "reset_font_size", Description = "Reset font size to default", RequiresParameter = false },
            ["clear_screen"] = new() { Name = "clear_screen", Description = "Clear screen and scrollback", RequiresParameter = false },
            ["scroll_to_top"] = new() { Name = "scroll_to_top", Description = "Scroll to top of scrollback", RequiresParameter = false },
            ["scroll_to_bottom"] = new() { Name = "scroll_to_bottom", Description = "Scroll to bottom", RequiresParameter = false },
            ["open_config"] = new() { Name = "open_config", Description = "Open configuration file", RequiresParameter = false },
            ["reload_config"] = new() { Name = "reload_config", Description = "Reload configuration", RequiresParameter = false },
            ["toggle_fullscreen"] = new() { Name = "toggle_fullscreen", Description = "Toggle fullscreen mode", RequiresParameter = false },
            ["equalize_splits"] = new() { Name = "equalize_splits", Description = "Equalize split panes", RequiresParameter = false },
            ["toggle_split_zoom"] = new() { Name = "toggle_split_zoom", Description = "Zoom current split", RequiresParameter = false },
        };

        var autoCompleter = new ActionAutoCompleter(schemas);

        return new CommandPaletteViewModel(
            sources,
            frecency,
            autoCompleter,
            groupByCategory: _uiSettings.CommandPaletteGroupCommands);
    }

    private void ExecuteBindingAction(string actionKey)
    {
        var leaf = _tabManager.ActiveTab?.PaneHost?.ActiveLeaf;
        if (leaf is null) return;

        var terminal = leaf.Terminal();
        var surfaceHandle = terminal.SurfaceHandle;
        if (surfaceHandle == IntPtr.Zero) return;

        var surface = new GhosttySurface(surfaceHandle);
        var actionBytes = Encoding.UTF8.GetBytes(actionKey);
        unsafe
        {
            fixed (byte* p = actionBytes)
            {
                NativeMethods.SurfaceBindingAction(surface, p, (UIntPtr)actionBytes.Length);
            }
        }
    }
}
