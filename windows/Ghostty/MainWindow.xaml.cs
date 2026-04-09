using System;
using System.Runtime.InteropServices;
using Ghostty.Controls;
using Ghostty.Core.Panes;
using Ghostty.Core.Tabs;
using Ghostty.Core.Taskbar;
using Ghostty.Taskbar;
using Ghostty.Hosting;
using Ghostty.Input;
using Ghostty.Panes;
using Ghostty.Settings;
using Ghostty.Tabs;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using WinRT.Interop;

namespace Ghostty;

public sealed partial class MainWindow : Window
{
    private readonly GhosttyHost _host;
    private readonly PaneHostFactory _factory;
    private readonly TabManager _tabManager;
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
    private readonly Grid _paneHostContainer;
    // Title bar strip shown when vertical tabs are active. Hosts the
    // switch-layout button on the left, the active-tab title centered,
    // and a 146 DIP right inset for the OS caption buttons. Lives in
    // RootGrid row 0 at col span 2 so it spans the full window.
    private Grid _verticalTitleBar = null!;
    private TextBlock _verticalTitleText = null!;
    private Grid _verticalTitleDragRegion = null!;
    private TabModel? _verticalTitleBoundTab;
    private readonly UiSettings _uiSettings;
    private bool _switchingLayout;
    private LeafPane? _activeLeaf;

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
        _uiSettings = UiSettings.Load();

        // Build the dual-chrome layout: both tab hosts coexist in
        // the same RootGrid, only one is visible at a time. The
        // shared PaneHostContainer lives in the same grid slot
        // forever so no SwapChainPanel is ever reparented.
        BuildLayout(out _paneHostContainer);

        _horizontalTabHost = new TabHost(_tabManager);
        _verticalTabHost = new VerticalTabHost(_tabManager, _host);

        // Chevron (and keyboard pinned toggle) forwards a new strip
        // width. MainWindow tweens both the RootGrid outer strip
        // column and the VerticalTabHost's own internal column in
        // lockstep so the sidebar actually grows past 40 px.
        _verticalTabHost.StripWidthChangeRequested += (_, width) =>
        {
            var col = RootGrid.ColumnDefinitions[0];
            TweenColumnWidth(col, col.Width.Value, width, SwitchDurationMs,
                onTick: v => _verticalTabHost.SetInternalStripWidth(v));
        };

        RebindVerticalTitle();
        _tabManager.ActiveTabChanged += (_, _) => RebindVerticalTitle();

        // Place both tab hosts in their RootGrid slots.
        Grid.SetRow((FrameworkElement)_horizontalTabHost.HostElement, 0);
        Grid.SetColumn((FrameworkElement)_horizontalTabHost.HostElement, 0);
        Grid.SetColumnSpan((FrameworkElement)_horizontalTabHost.HostElement, 2);
        RootGrid.Children.Add(_horizontalTabHost.HostElement);

        Grid.SetRow(_verticalTabHost, 0);
        Grid.SetColumn(_verticalTabHost, 0);
        Grid.SetRowSpan(_verticalTabHost, 2);
        RootGrid.Children.Add(_verticalTabHost);

        // Parent every existing and future PaneHost into the shared
        // container. This is the single owner for PaneHost lifetime
        // in the visual tree — both tab hosts read from it without
        // ever reparenting.
        foreach (var t in _tabManager.Tabs) AddPaneHost(t);
        SwapActivePane();
        _tabManager.TabAdded += (_, t) => { AddPaneHost(t); SwapActivePane(); };
        _tabManager.TabRemoved += (_, t) => RemovePaneHost(t);
        _tabManager.ActiveTabChanged += (_, _) => SwapActivePane();

        _tabHost = _uiSettings.VerticalTabs ? _verticalTabHost : _horizontalTabHost;
        ApplyLayoutMode(_uiSettings.VerticalTabs, animate: false);

        SetTitleBarForCurrentMode();

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
            };
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Taskbar progress wiring failed: {ex.Message}");
        }

        Closed += (_, _) =>
        {
            // Surface lifetime is decoupled from Loaded/Unloaded
            // (see TerminalControl.DisposeSurface), so we have to
            // free every leaf in every tab explicitly before tearing
            // down the libghostty host.
            foreach (var t in _tabManager.Tabs) t.PaneHost.DisposeAllLeaves();
            _host.Dispose();
        };
    }

    /// <summary>
    /// Rewrite RootGrid into a 2x2 layout that both tab hosts share:
    ///
    ///   +------------------+----------------+
    ///   | v-strip col 0    | title / h-strip|  row 0 (Auto)
    ///   |  (0 px in        |    row 0 col 1 |
    ///   |   horizontal)    +----------------+
    ///   |                  | PaneHost *     |  row 1 (*)
    ///   +------------------+----------------+
    ///
    /// PaneHostContainer is a direct child of RootGrid at row 1,
    /// col 1 and never moves. This is the safe slot for every
    /// PaneHost's SwapChainPanel.
    /// </summary>
    private void BuildLayout(out Grid paneHostContainer)
    {
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Vertical-mode title bar (row 0, full width). Hosts the
        // switch-layout button, the active-tab title, and the caption
        // button inset. Visibility toggles with the active layout.
        BuildVerticalTitleBar();
        Grid.SetRow(_verticalTitleBar, 0);
        Grid.SetColumn(_verticalTitleBar, 0);
        Grid.SetColumnSpan(_verticalTitleBar, 2);
        RootGrid.Children.Add(_verticalTitleBar);

        paneHostContainer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(paneHostContainer, 1);
        Grid.SetColumn(paneHostContainer, 1);
        RootGrid.Children.Add(paneHostContainer);
    }

    private void BuildVerticalTitleBar()
    {
        _verticalTitleBar = new Grid
        {
            Height = 32,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Visibility = Visibility.Collapsed,
        };
        _verticalTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _verticalTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) });

        _verticalTitleDragRegion = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Grid.SetColumn(_verticalTitleDragRegion, 0);

        var switchButton = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            // Sit just to the right of the sidebar column (40 px
            // wide) so the button does not overlap the chevron that
            // lives at the top of VerticalTabStrip.
            Margin = new Thickness(44, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)
                    Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = "\uE8A9",
                FontSize = 14,
            },
        };
        ToolTipService.SetToolTip(switchButton, "Switch to horizontal tabs (Ctrl+Shift+Alt+V)");
        switchButton.Click += (_, _) => PaneActionRouter.RequestToggleTabLayout(_tabManager);

        _verticalTitleText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["TextFillColorPrimaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };

        _verticalTitleDragRegion.Children.Add(switchButton);
        _verticalTitleDragRegion.Children.Add(_verticalTitleText);
        _verticalTitleBar.Children.Add(_verticalTitleDragRegion);
    }

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
        _verticalTitleText.Text = _verticalTitleBoundTab?.EffectiveTitle ?? "Ghostty";
    }

    private void AddPaneHost(Core.Tabs.TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        paneHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        paneHost.VerticalAlignment = VerticalAlignment.Stretch;
        paneHost.Visibility = Visibility.Collapsed;
        _paneHostContainer.Children.Add(paneHost);
    }

    private void RemovePaneHost(Core.Tabs.TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        _paneHostContainer.Children.Remove(paneHost);
    }

    private void SwapActivePane()
    {
        var active = (PaneHost)_tabManager.ActiveTab.PaneHost;
        foreach (UIElement child in _paneHostContainer.Children)
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
        ApplyLayoutMode(toVertical, animate: true);
        SetTitleBarForCurrentMode();
    }

    private void SetTitleBarForCurrentMode()
    {
        // In horizontal mode the drag region is the TabStripFooter
        // inside TabHost. In vertical mode it is the MainWindow-owned
        // title bar grid (row 0 full width).
        if (_tabHost is VerticalTabHost)
            SetTitleBar(_verticalTitleDragRegion);
        else
            SetTitleBar(_horizontalTabHost.DragRegion as FrameworkElement);
    }

    /// <summary>
    /// Apply the given layout mode to the RootGrid column widths and
    /// both tab hosts' visibility. On first call (animate=false) the
    /// end state is snapped immediately; on toggle (animate=true) the
    /// outgoing chrome slides away while the incoming chrome slides
    /// in, and the vertical strip column width tweens in parallel.
    /// </summary>
    private const double VerticalStripCollapsedWidth = 40;
    private const int SwitchDurationMs = 220;

    private void ApplyLayoutMode(bool verticalTabs, bool animate)
    {
        var verticalHost = (FrameworkElement)_verticalTabHost;
        var horizontalHost = (FrameworkElement)_horizontalTabHost.HostElement;

        var stripCol = RootGrid.ColumnDefinitions[0];
        var targetColWidth = verticalTabs ? VerticalStripCollapsedWidth : 0;

        if (!animate)
        {
            stripCol.Width = new GridLength(targetColWidth);
            _verticalTabHost.SetInternalStripWidth(VerticalStripCollapsedWidth);
            verticalHost.Opacity = verticalTabs ? 1 : 0;
            verticalHost.Visibility = verticalTabs ? Visibility.Visible : Visibility.Collapsed;
            verticalHost.IsHitTestVisible = verticalTabs;
            horizontalHost.Opacity = verticalTabs ? 0 : 1;
            horizontalHost.Visibility = verticalTabs ? Visibility.Collapsed : Visibility.Visible;
            horizontalHost.IsHitTestVisible = !verticalTabs;
            _verticalTitleBar.Visibility = verticalTabs ? Visibility.Visible : Visibility.Collapsed;
            _verticalTitleBar.Opacity = verticalTabs ? 1 : 0;
            return;
        }

        _switchingLayout = true;
        _verticalTitleBar.Visibility = Visibility.Visible;
        verticalHost.Visibility = Visibility.Visible;
        horizontalHost.Visibility = Visibility.Visible;

        // Incoming chrome is prepped at Opacity 0 with a translate
        // offset, then animates to Opacity 1 / offset 0.
        var incoming = verticalTabs ? verticalHost : horizontalHost;
        var outgoing = verticalTabs ? horizontalHost : verticalHost;
        var incomingOffset = verticalTabs ? new Windows.Foundation.Point(-VerticalStripCollapsedWidth, 0) : new Windows.Foundation.Point(0, -32);
        var outgoingOffset = verticalTabs ? new Windows.Foundation.Point(0, -32) : new Windows.Foundation.Point(-VerticalStripCollapsedWidth, 0);

        incoming.IsHitTestVisible = true;
        var incomingTx = GetOrCreateTranslate(incoming);
        incomingTx.X = incomingOffset.X;
        incomingTx.Y = incomingOffset.Y;
        incoming.Opacity = 0;
        incoming.Visibility = Visibility.Visible;

        var sb = new Storyboard();

        // Cross-fade.
        sb.Children.Add(MakeDoubleAnim(incoming, "Opacity", 0, 1));
        sb.Children.Add(MakeDoubleAnim(outgoing, "Opacity", outgoing.Opacity, 0));
        // The vertical title bar fades with the vertical chrome.
        sb.Children.Add(MakeDoubleAnim(_verticalTitleBar, "Opacity",
            verticalTabs ? 0 : 1, verticalTabs ? 1 : 0));

        // Slide.
        sb.Children.Add(MakeTransformAnim(incoming, "X", incomingTx.X, 0));
        sb.Children.Add(MakeTransformAnim(incoming, "Y", incomingTx.Y, 0));
        var outgoingTx = GetOrCreateTranslate(outgoing);
        sb.Children.Add(MakeTransformAnim(outgoing, "X", outgoingTx.X, outgoingOffset.X));
        sb.Children.Add(MakeTransformAnim(outgoing, "Y", outgoingTx.Y, outgoingOffset.Y));

        sb.Completed += (_, _) =>
        {
            outgoing.IsHitTestVisible = false;
            outgoing.Opacity = 0;
            outgoing.Visibility = Visibility.Collapsed;
            // Reset outgoing back to origin so a subsequent switch
            // animates from a known position instead of stacking.
            outgoingTx.X = 0;
            outgoingTx.Y = 0;
            _verticalTitleBar.Visibility = verticalTabs ? Visibility.Visible : Visibility.Collapsed;
            _switchingLayout = false;
        };
        sb.Begin();

        // Column width tween runs in parallel via DispatcherQueueTimer
        // because animating GridLength via Storyboard is clunky. Drive
        // the VerticalTabHost's own internal column in lockstep so its
        // visual sidebar fills the outer column.
        TweenColumnWidth(stripCol, stripCol.Width.Value, targetColWidth, SwitchDurationMs,
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

    private void TweenColumnWidth(ColumnDefinition col, double from, double to, int durationMs, Action<double>? onTick = null)
    {
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
            if (progress >= 1.0) t.Stop();
        };
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
    /// Bindings live in <see cref="KeyBindings.Default"/>; a future PR
    /// will replace that with a config-driven loader and nothing here
    /// has to change.
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
                PaneActionRouter.Invoke(captured.Action, _tabManager);
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
        PaneActionRouter.TabCloseRequestedFromKeyboard += async (_, mgr) =>
        {
            if (!ReferenceEquals(mgr, _tabManager)) return;
            await _tabHost.RequestCloseTabAsync(_tabManager.ActiveTab);
        };

        // Vertical-tabs pinned toggle via Ctrl+Shift+Space. No-op
        // when the layout is horizontal (TabHost) — the chord is
        // registered globally but only VerticalTabHost responds.
        PaneActionRouter.ToggleVerticalTabsPinnedFromKeyboard += (_, mgr) =>
        {
            if (!ReferenceEquals(mgr, _tabManager)) return;
            if (_tabHost is VerticalTabHost vth)
                vth.TogglePinnedFromKeyboard();
        };

        // Runtime tab-layout switch via Ctrl+Shift+Alt+V.
        PaneActionRouter.ToggleTabLayoutFromKeyboard += (_, mgr) =>
        {
            if (!ReferenceEquals(mgr, _tabManager)) return;
            ToggleTabLayout();
        };
    }
}
