using System;
using System.Threading.Tasks;
using Ghostty.Core.Tabs;
using Ghostty.Panes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Ghostty.Tabs;

/// <summary>
/// Vertical-sidebar tab host. Sibling of <see cref="TabHost"/>.
/// Two-column Grid: <see cref="VerticalTabStrip"/> in column 0,
/// active tab's <c>PaneHost</c> in column 1. All pane hosts live
/// as siblings in <c>PaneHostContainer</c> with Visibility toggled
/// for the active one — same SwapChainPanel-safety pattern as
/// <see cref="TabHost"/>.
///
/// Collapsed-only in this commit. Animation, expanded layout,
/// drag handle, and hover-expand come in later commits.
/// </summary>
internal sealed partial class VerticalTabHost : UserControl, ITabHost
{
    private readonly TabManager _manager;
    private readonly VerticalTabStrip _strip;
    private readonly GridLengthProxy _columnProxy;
    private readonly ColumnDragHandle _dragHandle;

    // TODO(config): vertical-tabs-width (int px, default 220)
    private const double ExpandedWidth = 220;
    // TODO(config): vertical-tabs-pinned (bool, default false)
    private bool _pinnedExpanded = false;

    public FrameworkElement HostElement => this;
    public UIElement DragRegion => CustomDragRegion;

    public VerticalTabHost(TabManager manager)
    {
        InitializeComponent();
        _manager = manager;

        _columnProxy = new GridLengthProxy(StripColumn);
        _columnProxy.Width = StripColumn.Width.Value;

        _strip = new VerticalTabStrip(manager);
        _strip.CloseRequestedFromRow += async tab => await RequestCloseTabAsync(tab);
        StripHost.Content = _strip;

        // Drag handle for live resize in pinned-expanded mode.
        // Hidden by default; TogglePinned shows it when entering
        // the pinned state and hides it on collapse.
        _dragHandle = new ColumnDragHandle(
            onWidthChanged: w =>
            {
                // Live drag: set column width and proxy backing in
                // lockstep so the next tween starts from the right
                // value.
                StripColumn.Width = new GridLength(w);
                _columnProxy.Width = w;
            },
            readCurrentWidth: () => StripColumn.Width.Value)
        {
            Visibility = Visibility.Collapsed,
            Height = double.NaN, // stretch via Canvas parent sizing
        };
        HandleHost.Children.Add(_dragHandle);
        // Bind the handle's height to the HandleHost size so it
        // spans the whole strip vertically.
        HandleHost.SizeChanged += (_, e) => _dragHandle.Height = e.NewSize.Height;

        foreach (var t in _manager.Tabs) AddPane(t);
        SwapActivePane();

        _manager.TabAdded += (_, t) => { AddPane(t); SwapActivePane(); };
        _manager.TabRemoved += (_, t) => RemovePane(t);
        _manager.ActiveTabChanged += (_, _) => SwapActivePane();

        _strip.NewTabRequested += (_, _) => _manager.NewTab();
        _strip.ChevronToggled += (_, _) => TogglePinned();
    }

    /// <summary>
    /// Toggle the pinned-expanded state. Called by the chevron
    /// button click and (in a later commit) by the
    /// Ctrl+Shift+Space keyboard chord.
    /// </summary>
    internal void TogglePinnedFromKeyboard() => TogglePinned();

    private void TogglePinned()
    {
        _pinnedExpanded = !_pinnedExpanded;
        _strip.IsExpanded = _pinnedExpanded;
        AnimateColumnWidth(_pinnedExpanded ? ExpandedWidth : 40);
        _dragHandle.Visibility = _pinnedExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimateColumnWidth(double target)
    {
        var anim = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, _columnProxy);
        Storyboard.SetTargetProperty(anim, "Width");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            // Reflow the terminal once. The libghostty resize call
            // is driven by TerminalControl.OnSizeChanged which fires
            // from the panel's LayoutUpdated. Forcing a layout pass
            // here ensures that fires exactly once for the final
            // dimensions rather than 10x during the tween.
            PaneHostContainer.InvalidateMeasure();
            PaneHostContainer.UpdateLayout();
        };
        sb.Begin();
    }

    private void AddPane(TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        paneHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        paneHost.VerticalAlignment = VerticalAlignment.Stretch;
        paneHost.Visibility = Visibility.Collapsed;
        PaneHostContainer.Children.Add(paneHost);
    }

    private void RemovePane(TabModel tab)
    {
        var paneHost = (PaneHost)tab.PaneHost;
        PaneHostContainer.Children.Remove(paneHost);
    }

    private void SwapActivePane()
    {
        var active = (PaneHost)_manager.ActiveTab.PaneHost;
        foreach (UIElement child in PaneHostContainer.Children)
        {
            child.Visibility = ReferenceEquals(child, active)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <inheritdoc/>
    public async Task RequestCloseTabAsync(TabModel tab)
    {
        // TODO(config): confirm-close-multi-pane (bool, default true)
        const bool confirmCloseMultiPane = true;

        var paneCount = tab.PaneHost.PaneCount;
        if (confirmCloseMultiPane && paneCount > 1)
        {
            var dlg = new ContentDialog
            {
                Title = "Close tab?",
                Content = $"This tab has {paneCount} panes. Close all of them?",
                PrimaryButtonText = "Close all",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = XamlRoot,
            };
            var res = await dlg.ShowAsync();
            if (res != ContentDialogResult.Primary) return;
        }
        _manager.CloseTab(tab);
    }
}
