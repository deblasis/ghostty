using System;
using System.Runtime.InteropServices;
using Ghostty.Controls;
using Ghostty.Core.Panes;
using Ghostty.Core.Tabs;
using Ghostty.Core.Taskbar;
using Ghostty.Dialogs;
using Ghostty.Hosting;
using Ghostty.Input;
using Ghostty.Panes;
using Ghostty.Settings;
using Ghostty.Tabs;
using Ghostty.Taskbar;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace Ghostty;

public sealed partial class MainWindow : Window
{
    private readonly GhosttyHost _host;
    private readonly PaneHostFactory _factory;
    private readonly TabManager _tabManager;
    private readonly PaneActionRouter _router;
    private readonly DialogTracker _dialogs = new();
    private TaskbarList3Facade? _taskbarFacade;
    private TaskbarProgressCoordinator? _taskbarCoordinator;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _taskbarTickTimer;

    // Both tab hosts are instantiated at startup so the user can
    // runtime-switch without ever tearing down any PaneHost or its
    // SwapChainPanel. _tabHost tracks the currently-active one for
    // accelerator/title-bar routing; the inactive one sits in the
    // visual tree with Opacity=0 and IsHitTestVisible=false.
    private readonly TabHost _horizontalTabHost;
    private readonly VerticalTabHost _verticalTabHost;
    private ITabHost _tabHost;

    private readonly UiSettings _uiSettings;
    private TabModel? _verticalTitleBoundTab;
    private bool _switchingLayout;
    private LeafPane? _activeLeaf;

    // Concurrent-tween guard. Both ApplyLayoutMode (on runtime
    // layout switch) and the chevron toggle via
    // VerticalTabHost.StripWidthChangeRequested can start a width
    // tween targeting StripColumn. Keep a handle to the most recent
    // timer so a second tween cancels the first instead of racing.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _activeColumnTween;

    // Dedup guard for KeyboardAccelerator double-dispatch. WinUI 3
    // fires accelerator Invoked twice for a single key event when the
    // accelerator is registered on a parent of the focused element,
    // even with args.Handled = true and ScopeOwner explicitly set.
    //
    // Workaround: remember which action just fired and swallow any
    // subsequent Invoked for the same action until the next KeyUp
    // resets the flag. Framework dupes arrive inside the same physical
    // keypress (no KeyUp between them) so they are filtered, while
    // legitimate user repeats (KeyDown -> KeyUp -> KeyDown) come
    // through unmodified. This replaces an earlier 150 ms wall-clock
    // window that ate muscle-memory double-splits.
    //
    // Tracked in https://github.com/deblasis/ghostty/issues/165
    private PaneAction? _acceleratorFiredThisKeyDown;

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

    private const double VerticalStripCollapsedWidth = 40;
    private const int SwitchDurationMs = 220;

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
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        // Extend content into the title bar: remove the system-drawn
        // title bar chrome and let TabHost's TabView strip render
        // where the title bar used to be. The caption buttons
        // (min / max / close) are still drawn by the system on the
        // right; TabView's TabStripFooter reserves room for them.
        // Must be set before the TabHost is parented so the content
        // area is sized without the default title bar row.
        ExtendsContentIntoTitleBar = true;

        _factory = new PaneHostFactory(_host);
        _tabManager = new TabManager(() => _factory.Create());
        _router = new PaneActionRouter(_tabManager);
        _uiSettings = UiSettings.Load();

        _horizontalTabHost = new TabHost(_tabManager, _router, _dialogs);
        _verticalTabHost = new VerticalTabHost(_tabManager, _router, _dialogs, _host);

        // Chevron (and keyboard pinned toggle) forwards a new strip
        // width. MainWindow tweens both the RootGrid outer strip
        // column and the VerticalTabHost's own internal column in
        // lockstep so the sidebar actually grows past 40 px.
        _verticalTabHost.StripWidthChangeRequested += (_, width) =>
        {
            TweenColumnWidth(StripColumn, StripColumn.Width.Value, width, SwitchDurationMs,
                onTick: v => _verticalTabHost.SetInternalStripWidth(v));
        };

        // Place both tab hosts in their RootGrid slots. The
        // horizontal host spans both columns in row 0 so its TabView
        // strip can grow under the title bar area; the vertical host
        // anchors at col 0 and spans both rows.
        Grid.SetRow((FrameworkElement)_horizontalTabHost.HostElement, 0);
        Grid.SetColumn((FrameworkElement)_horizontalTabHost.HostElement, 0);
        Grid.SetColumnSpan((FrameworkElement)_horizontalTabHost.HostElement, 2);
        RootGrid.Children.Add(_horizontalTabHost.HostElement);

        Grid.SetRow(_verticalTabHost, 0);
        Grid.SetColumn(_verticalTabHost, 0);
        Grid.SetRowSpan(_verticalTabHost, 2);
        RootGrid.Children.Add(_verticalTabHost);

        // Parent every existing and future PaneHost into the shared
        // container (declared in MainWindow.xaml as PaneHostContainer).
        // This is the single owner for PaneHost lifetime in the visual
        // tree — both tab hosts read from it without ever reparenting.
        foreach (var t in _tabManager.Tabs) AddPaneHost(t);
        SwapActivePane();
        _tabManager.TabAdded += (_, t) => { AddPaneHost(t); SwapActivePane(); };
        _tabManager.TabRemoved += (_, t) => RemovePaneHost(t);
        _tabManager.ActiveTabChanged += (_, _) => SwapActivePane();

        RebindVerticalTitle();
        _tabManager.ActiveTabChanged += (_, _) => RebindVerticalTitle();

        // Tooltip chord label is sourced from KeyBindings.Default so
        // the button description cannot drift from the accelerator.
        var chord = KeyBindings.Default.Label(PaneAction.ToggleTabLayout);
        ToolTipService.SetToolTip(
            VerticalSwitchButton,
            chord is null
                ? "Switch to horizontal tabs"
                : $"Switch to horizontal tabs ({chord})");

        _tabHost = _uiSettings.VerticalTabs ? _verticalTabHost : _horizontalTabHost;
        SnapLayoutState(_uiSettings.VerticalTabs);

        SetTitleBarForCurrentMode();
        SyncCaptionInset();

        InstallPaneAccelerators();

        _tabManager.ActiveTabChanged += (_, _) => HookActiveTabTitle();
        _tabManager.WindowTitleChanged += (_, _) => Title = _tabManager.ActiveTab.EffectiveTitle;
        HookActiveTabTitle();

        _tabManager.LastTabClosed += (_, _) => Close();

        // Taskbar progress: wire a TaskbarProgressCoordinator through
        // a real ITaskbarList3 facade, driven by a 2s DispatcherQueueTimer.
        // Wrapped in try/catch because a COM failure here must not
        // block window construction — the taskbar indicator is a
        // nice-to-have.
        try
        {
            var taskbarHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _taskbarFacade = new TaskbarList3Facade(taskbarHwnd);
            _taskbarCoordinator = new TaskbarProgressCoordinator(
                _tabManager,
                _taskbarFacade,
                () => System.DateTime.UtcNow);
            _taskbarTickTimer = DispatcherQueue.CreateTimer();
            _taskbarTickTimer.Interval = System.TimeSpan.FromSeconds(2);
            _taskbarTickTimer.IsRepeating = true;
            _taskbarTickTimer.Tick += (_, _) => _taskbarCoordinator!.Tick();
            _taskbarTickTimer.Start();

            // Pause cycling when minimized so the taskbar does not
            // churn while the window is hidden. AppWindow.Changed
            // fires on presenter state transitions.
            this.AppWindow.Changed += (_, _) =>
            {
                if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op)
                {
                    if (op.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                        _taskbarCoordinator?.Pause();
                    else
                        _taskbarCoordinator?.Resume();
                }
                SyncCaptionInset();
            };
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Taskbar progress wiring failed: {ex.Message}");
        }

        Closed += OnClosedAsync;
    }

    private async void OnClosedAsync(object sender, WindowEventArgs args)
    {
        // Let any in-flight ContentDialog complete before we tear
        // down the libghostty host. Files #17363 reproducer:
        // disposing MainWindow while a dialog is still animating its
        // data-binding teardown throws a COMException out of the
        // XAML runtime. The tracker awaits the tasks that the tab
        // hosts push in via RequestCloseTabAsync / RenameTabDialog.
        try
        {
            if (_dialogs.PendingCount > 0)
                await _dialogs.WhenAllClosedAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DialogTracker drain failed: {ex.Message}");
        }

        // Surface lifetime is decoupled from Loaded/Unloaded
        // (see TerminalControl.DisposeSurface), so we have to
        // free every leaf in every tab explicitly before tearing
        // down the libghostty host.
        foreach (var t in _tabManager.Tabs) t.PaneHost.DisposeAllLeaves();
        _host.Dispose();
    }

    private void OnVerticalSwitchButtonClick(object sender, RoutedEventArgs e)
        => _router.RequestToggleTabLayout();

    private void RebindVerticalTitle()
    {
        if (_verticalTitleBoundTab is not null)
            _verticalTitleBoundTab.PropertyChanged -= OnVerticalTitlePropertyChanged;
        _verticalTitleBoundTab = _tabManager.ActiveTab;
        if (_verticalTitleBoundTab is not null)
            _verticalTitleBoundTab.PropertyChanged += OnVerticalTitlePropertyChanged;
        UpdateVerticalTitleText();
    }

    private void OnVerticalTitlePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabModel.EffectiveTitle) ||
            e.PropertyName == nameof(TabModel.ShellReportedTitle) ||
            e.PropertyName == nameof(TabModel.UserOverrideTitle))
        {
            UpdateVerticalTitleText();
        }
    }

    private void UpdateVerticalTitleText()
    {
        VerticalTitleText.Text = _verticalTitleBoundTab?.EffectiveTitle ?? "Ghostty";
    }

    /// <summary>
    /// Keep the right-hand caption inset in sync with the real
    /// OS-reserved width for min/max/close buttons. AppWindow's
    /// TitleBar.RightInset is DPI- and theme-aware; the previous
    /// hard-coded 146 DIP was a best-guess for 1x DPI.
    /// </summary>
    private void SyncCaptionInset()
    {
        try
        {
            var inset = AppWindow.TitleBar.RightInset;
            // RightInset is in physical pixels; scale by the current
            // rasterization scale so we land on the right DIP.
            var scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
            var dip = scale > 0 ? inset / scale : inset;
            if (dip > 0)
                VerticalCaptionInset.Width = new GridLength(dip);
        }
        catch
        {
            // RightInset can throw early during construction; leave
            // the XAML default (146) in place.
        }
    }

    private void AddPaneHost(Core.Tabs.TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        paneHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        paneHost.VerticalAlignment = VerticalAlignment.Stretch;
        paneHost.Visibility = Visibility.Collapsed;
        PaneHostContainer.Children.Add(paneHost);
    }

    private void RemovePaneHost(Core.Tabs.TabModel tab)
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

    /// <summary>
    /// Toggle between horizontal and vertical tab layouts at runtime.
    /// Triggered by Ctrl+Shift+Alt+V, the title-bar icon button, and
    /// the strip context menu. Persists the choice via
    /// <see cref="UiSettings"/> so it survives the next launch.
    /// </summary>
    internal void ToggleTabLayout()
    {
        if (_switchingLayout) return;
        var toVertical = !_uiSettings.VerticalTabs;
        _uiSettings.VerticalTabs = toVertical;
        _uiSettings.Save();
        _tabHost = toVertical ? _verticalTabHost : _horizontalTabHost;
        AnimateLayoutSwitch(toVertical);
        SetTitleBarForCurrentMode();
    }

    private void SetTitleBarForCurrentMode()
    {
        // In horizontal mode the drag region is the TabStripFooter
        // inside TabHost. In vertical mode it is the MainWindow-owned
        // title bar grid (row 0 full width).
        if (_tabHost is VerticalTabHost)
            SetTitleBar(VerticalTitleDragRegion);
        else
            SetTitleBar(_horizontalTabHost.DragRegion as FrameworkElement);
    }

    /// <summary>
    /// Snap both hosts and the vertical title bar to the end state
    /// for <paramref name="verticalTabs"/>. Used at construction
    /// (animate=false) and from the Storyboard Completed handler to
    /// guarantee a consistent end state regardless of mid-flight
    /// cancellation.
    /// </summary>
    private void SnapLayoutState(bool verticalTabs)
    {
        var verticalHost = (FrameworkElement)_verticalTabHost;
        var horizontalHost = (FrameworkElement)_horizontalTabHost.HostElement;

        StripColumn.Width = new GridLength(verticalTabs ? VerticalStripCollapsedWidth : 0);
        _verticalTabHost.SetInternalStripWidth(VerticalStripCollapsedWidth);

        verticalHost.Opacity = verticalTabs ? 1 : 0;
        verticalHost.Visibility = verticalTabs ? Visibility.Visible : Visibility.Collapsed;
        verticalHost.IsHitTestVisible = verticalTabs;

        horizontalHost.Opacity = verticalTabs ? 0 : 1;
        horizontalHost.Visibility = verticalTabs ? Visibility.Collapsed : Visibility.Visible;
        horizontalHost.IsHitTestVisible = !verticalTabs;

        VerticalTitleBar.Visibility = verticalTabs ? Visibility.Visible : Visibility.Collapsed;
        VerticalTitleBar.Opacity = verticalTabs ? 1 : 0;

        // Reset any dangling translate offsets so future switches
        // start from origin. Safe to overwrite: SnapLayoutState is
        // only called when no transform animation is in flight.
        GetOrCreateTranslate(verticalHost).X = 0;
        GetOrCreateTranslate(verticalHost).Y = 0;
        GetOrCreateTranslate(horizontalHost).X = 0;
        GetOrCreateTranslate(horizontalHost).Y = 0;
    }

    /// <summary>
    /// Cross-fade + slide animation between horizontal and vertical
    /// layouts. Drives the chrome transforms via a Storyboard
    /// (compositor-backed) and the strip column width via
    /// <see cref="TweenColumnWidth"/> because GridLength cannot be
    /// animated by the Storyboard system.
    /// </summary>
    private void AnimateLayoutSwitch(bool verticalTabs)
    {
        var verticalHost = (FrameworkElement)_verticalTabHost;
        var horizontalHost = (FrameworkElement)_horizontalTabHost.HostElement;
        var targetColWidth = verticalTabs ? VerticalStripCollapsedWidth : 0;

        _switchingLayout = true;
        VerticalTitleBar.Visibility = Visibility.Visible;
        verticalHost.Visibility = Visibility.Visible;
        horizontalHost.Visibility = Visibility.Visible;

        // Incoming chrome is prepped at Opacity 0 with a translate
        // offset, then animates to Opacity 1 / offset 0.
        var incoming = verticalTabs ? verticalHost : horizontalHost;
        var outgoing = verticalTabs ? horizontalHost : verticalHost;
        var incomingOffset = verticalTabs
            ? new Windows.Foundation.Point(-VerticalStripCollapsedWidth, 0)
            : new Windows.Foundation.Point(0, -32);
        var outgoingOffset = verticalTabs
            ? new Windows.Foundation.Point(0, -32)
            : new Windows.Foundation.Point(-VerticalStripCollapsedWidth, 0);

        incoming.IsHitTestVisible = true;
        var incomingTx = GetOrCreateTranslate(incoming);
        incomingTx.X = incomingOffset.X;
        incomingTx.Y = incomingOffset.Y;
        incoming.Opacity = 0;

        var sb = new Storyboard();
        sb.Children.Add(MakeDoubleAnim(incoming, "Opacity", 0, 1));
        sb.Children.Add(MakeDoubleAnim(outgoing, "Opacity", outgoing.Opacity, 0));
        sb.Children.Add(MakeDoubleAnim(VerticalTitleBar, "Opacity",
            verticalTabs ? 0 : 1, verticalTabs ? 1 : 0));

        sb.Children.Add(MakeTransformAnim(incoming, "X", incomingTx.X, 0));
        sb.Children.Add(MakeTransformAnim(incoming, "Y", incomingTx.Y, 0));
        var outgoingTx = GetOrCreateTranslate(outgoing);
        sb.Children.Add(MakeTransformAnim(outgoing, "X", outgoingTx.X, outgoingOffset.X));
        sb.Children.Add(MakeTransformAnim(outgoing, "Y", outgoingTx.Y, outgoingOffset.Y));

        sb.Completed += (_, _) =>
        {
            SnapLayoutState(verticalTabs);
            _switchingLayout = false;
        };
        sb.Begin();

        // Column width tween runs in parallel (see AnimateLayoutSwitch
        // docstring for why it's code-driven, not Storyboard-driven).
        TweenColumnWidth(StripColumn, StripColumn.Width.Value, targetColWidth, SwitchDurationMs,
            onTick: _ => _verticalTabHost.SetInternalStripWidth(VerticalStripCollapsedWidth));
    }

    private static TranslateTransform GetOrCreateTranslate(FrameworkElement fe)
    {
        if (fe.RenderTransform is TranslateTransform t) return t;
        var nt = new TranslateTransform();
        fe.RenderTransform = nt;
        return nt;
    }

    private static DoubleAnimation MakeDoubleAnim(DependencyObject target, string path, double from, double to)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(SwitchDurationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, path);
        return anim;
    }

    private static DoubleAnimation MakeTransformAnim(FrameworkElement target, string axis, double from, double to)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(SwitchDurationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, target.RenderTransform);
        Storyboard.SetTargetProperty(anim, axis);
        return anim;
    }

    /// <summary>
    /// Tween a <see cref="ColumnDefinition.Width"/> from its current
    /// value to <paramref name="to"/> over <paramref name="durationMs"/>.
    /// Only one tween is active per column at a time: if a previous
    /// tween is still running when this is called it is stopped and
    /// replaced. GridLength cannot be animated by Storyboard so this
    /// is unavoidable.
    /// </summary>
    private void TweenColumnWidth(ColumnDefinition col, double from, double to, int durationMs, Action<double>? onTick = null)
    {
        // Cancel any previous in-flight tween so chevron + layout
        // switch can't race on the same column.
        _activeColumnTween?.Stop();

        var startTime = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.IsRepeating = true;
        timer.Tick += (t, _) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var progress = Math.Min(1.0, elapsed / duration.TotalMilliseconds);
            var eased = 1 - (1 - progress) * (1 - progress);
            var value = from + (to - from) * eased;
            col.Width = new GridLength(value);
            onTick?.Invoke(value);
            if (progress >= 1.0)
            {
                t.Stop();
                if (ReferenceEquals(_activeColumnTween, t))
                    _activeColumnTween = null;
            }
        };
        _activeColumnTween = timer;
        timer.Start();
    }

    /// <summary>
    /// Subscribe the active tab's active leaf to live title-change
    /// updates and write the title into <see cref="TabModel.ShellReportedTitle"/>.
    /// Re-runs every time the active tab changes or the active leaf
    /// within the active tab changes. The leaf-title hook lives here
    /// (not in <see cref="TabManager"/>) because it touches WinUI
    /// types that Ghostty.Core cannot reach.
    /// </summary>
    private void HookActiveTabTitle()
    {
        if (_activeLeaf is { } previous)
            previous.Terminal().TitleChanged -= OnLiveTitleChanged;

        var tab = _tabManager.ActiveTab;
        var leaf = tab.PaneHost.ActiveLeaf;
        _activeLeaf = leaf;
        leaf.Terminal().TitleChanged += OnLiveTitleChanged;
        tab.ShellReportedTitle = leaf.Terminal().CurrentTitle;
        Title = tab.EffectiveTitle;

        tab.PaneHost.LeafFocused -= OnActiveTabLeafFocused;
        tab.PaneHost.LeafFocused += OnActiveTabLeafFocused;
    }

    private void OnActiveTabLeafFocused(object? sender, LeafPane leaf)
    {
        if (_activeLeaf is { } previous)
            previous.Terminal().TitleChanged -= OnLiveTitleChanged;
        _activeLeaf = leaf;
        leaf.Terminal().TitleChanged += OnLiveTitleChanged;
        _tabManager.ActiveTab.ShellReportedTitle = leaf.Terminal().CurrentTitle;
    }

    private void OnLiveTitleChanged(object? sender, string title)
    {
        // TabManager raises WindowTitleChanged in response, which
        // sets Title via the constructor's subscription.
        _tabManager.ActiveTab.ShellReportedTitle = title;
    }

    /// <summary>
    /// Install one <see cref="KeyboardAccelerator"/> per binding from
    /// <see cref="KeyBindings.Default"/>. Each accelerator dispatches
    /// through <see cref="PaneActionRouter.Invoke"/>, so adding a new
    /// pane chord is one line in <see cref="KeyBindings.Default"/> and
    /// one case in <see cref="PaneActionRouter.Invoke"/>.
    ///
    /// Why KeyboardAccelerators rather than KeyDown / PreviewKeyDown:
    /// WinUI 3 routed key events ALL bubble (despite the "Preview"
    /// naming inherited from WPF, PreviewKeyDown does not tunnel). The
    /// focused TerminalControl receives KeyDown first, and would
    /// forward the chord to libghostty if we did nothing. Accelerators
    /// fire AFTER routed key events but BEFORE the framework gives up,
    /// AND only when the focused element has not marked the event
    /// handled - so TerminalControl actively short-circuits known
    /// chords (it asks the same KeyBindings registry) to let the
    /// accelerator fire.
    ///
    /// Router events are instance-scoped (no static subscriptions),
    /// so MainWindow can be closed and garbage-collected cleanly once
    /// the last tab closes.
    /// </summary>
    private void InstallPaneAccelerators()
    {
        foreach (var binding in KeyBindings.Default.All)
        {
            var captured = binding;
            var accel = new KeyboardAccelerator
            {
                Modifiers = captured.Modifiers,
                Key = captured.Key,
                // Pin the accelerator scope to _tabHost. Without this,
                // WinUI 3 dispatches the same accelerator twice for a
                // single key event (once from the focused element's
                // search up the tree, once from the host's search
                // down), and Split runs twice per Ctrl+Shift+D.
                ScopeOwner = _tabHost.HostElement,
            };
            accel.Invoked += (_, args) =>
            {
                args.Handled = true;
                // WinUI 3 fires Invoked twice per key event for an
                // accelerator on a parent of the focused element even
                // with Handled set and ScopeOwner pinned. Swallow the
                // second dispatch inside the same physical keypress
                // by remembering which action just fired; KeyUp on
                // the host clears the flag so the next KeyDown
                // dispatches normally.
                // See https://github.com/deblasis/ghostty/issues/165.
                if (_acceleratorFiredThisKeyDown == captured.Action) return;
                _acceleratorFiredThisKeyDown = captured.Action;
                _router.Invoke(captured.Action);
            };
            _tabHost.HostElement.KeyboardAccelerators.Add(accel);
        }

        _tabHost.HostElement.KeyUp += (_, _) => _acceleratorFiredThisKeyDown = null;
        _tabHost.HostElement.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        // Listen for keyboard-driven full-tab close. Route through
        // TabHost.RequestCloseTabAsync so the confirmation dialog
        // is the same code path as the per-tab X button and the
        // context-menu Close item — single source of truth for
        // close confirmation lives in TabHost, which has XamlRoot.
        _router.TabCloseRequestedFromKeyboard += async (_, _) =>
        {
            await _tabHost.RequestCloseTabAsync(_tabManager.ActiveTab);
        };

        // Vertical-tabs pinned toggle via Ctrl+Shift+Space. No-op
        // when the layout is horizontal (TabHost) — the chord is
        // registered globally but only VerticalTabHost responds.
        _router.ToggleVerticalTabsPinnedRequested += (_, _) =>
        {
            if (_tabHost is VerticalTabHost vth)
                vth.TogglePinnedFromKeyboard();
        };

        // Runtime tab-layout switch via Ctrl+Shift+Alt+V (and the
        // title-bar icon + context menu, which share the event path).
        _router.ToggleTabLayoutRequested += (_, _) => ToggleTabLayout();
    }
}
