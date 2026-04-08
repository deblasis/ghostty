using System.Collections.Generic;
using System.Threading.Tasks;
using Ghostty.Core.Tabs;
using Ghostty.Panes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ghostty.Tabs;

/// <summary>
/// WinUI host that visualises a <see cref="TabManager"/> as a
/// <see cref="TabView"/>. Owns the bidirectional mapping between
/// <see cref="TabModel"/>s and <see cref="TabViewItem"/>s. The
/// per-tab progress indicator is rendered here as a 2px ProgressBar
/// in the tab header template.
///
/// Vertical tabs are out of scope for this PR — they come in plan 2
/// as a sibling user control sharing this same TabManager.
/// </summary>
internal sealed partial class TabHost : UserControl, ITabHost
{
    private readonly TabManager _manager;
    private readonly Dictionary<TabModel, TabViewItem> _itemByModel = new();
    private bool _suppressSelectionEvent;

    public FrameworkElement HostElement => this;

    /// <summary>
    /// The Grid that sits in the TabView's TabStripFooter and
    /// reserves room for the OS caption buttons. <see cref="MainWindow"/>
    /// passes this to <c>Window.SetTitleBar</c> so clicks on the
    /// empty strip area drag the window.
    /// </summary>
    public UIElement DragRegion => CustomDragRegion;

    public TabHost(TabManager manager)
    {
        InitializeComponent();
        _manager = manager;

        foreach (var t in _manager.Tabs) AddItem(t);
        SelectActive();

        _manager.TabAdded += (_, t) => { AddItem(t); SelectActive(); };
        _manager.TabRemoved += (_, t) => RemoveItem(t);
        _manager.TabMoved += (_, e) => MoveItem(e.tab, e.to);
        _manager.ActiveTabChanged += (_, _) => SelectActive();
    }

    private void AddItem(TabModel tab)
    {
        // The PaneHost is parented into PaneHostContainer here, ONCE,
        // for the lifetime of the tab. It is never reparented when the
        // active tab changes — instead Visibility toggles. This keeps
        // the SwapChainPanel's DCOMP visual stable; reparenting it
        // (which is what TabViewItem.Content does) tears down the
        // underlying composition surface and breaks rendering.
        var paneHost = (PaneHost)tab.PaneHost;
        paneHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        paneHost.VerticalAlignment = VerticalAlignment.Stretch;
        paneHost.Visibility = Visibility.Collapsed;
        PaneHostContainer.Children.Add(paneHost);

        // The TabView item is a header-only placeholder. Content is
        // null on purpose; the actual terminal lives in
        // PaneHostContainer above.
        //
        // Header is a plain string rendered by TabView's default
        // header template. A custom HeaderTemplate with a progress
        // bar was considered but required a binding shape that does
        // not work cleanly with TabModel.EffectiveTitle (computed
        // property, no INPC). The progress indicator is stub
        // infrastructure until plan 4 wires OSC 9;4 anyway, so
        // dropping the template costs nothing here and plan 4 can
        // reintroduce it with a binding-safe shape.
        var item = new TabViewItem
        {
            Header = tab.EffectiveTitle,
            Content = null,
            ContextFlyout = TabContextMenuBuilder.Build(_manager, tab, RequestCloseTabAsync),
            DataContext = tab,
        };
        tab.PropertyChanged += (_, _) => RefreshHeader(item, tab);
        _itemByModel[tab] = item;
        TabViewControl.TabItems.Add(item);
    }

    private void RemoveItem(TabModel tab)
    {
        if (!_itemByModel.TryGetValue(tab, out var item)) return;
        TabViewControl.TabItems.Remove(item);
        _itemByModel.Remove(tab);

        // Dispose-time pane host removal. The PaneHost has already
        // had DisposeAllLeaves called by TabManager.CloseTab; here we
        // just detach the now-dead control from the visual tree.
        var paneHost = (PaneHost)tab.PaneHost;
        PaneHostContainer.Children.Remove(paneHost);
    }

    private void MoveItem(TabModel tab, int to)
    {
        if (!_itemByModel.TryGetValue(tab, out var item)) return;
        TabViewControl.TabItems.Remove(item);
        TabViewControl.TabItems.Insert(to, item);
        // PaneHostContainer order does not matter — Visibility picks
        // the active one. No reorder needed there.
    }

    private void SelectActive()
    {
        if (!_itemByModel.TryGetValue(_manager.ActiveTab, out var item)) return;

        // Toggle Visibility across all pane hosts: only the active
        // tab's PaneHost is visible. This does NOT fire Unloaded on
        // the inactive ones, so their SwapChainPanels keep rendering
        // (even invisibly — libghostty does not stop the renderer
        // thread on Visibility, which is fine; the cost is small).
        var activePaneHost = (PaneHost)_manager.ActiveTab.PaneHost;
        foreach (UIElement child in PaneHostContainer.Children)
        {
            child.Visibility = ReferenceEquals(child, activePaneHost)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (ReferenceEquals(TabViewControl.SelectedItem, item)) return;
        _suppressSelectionEvent = true;
        TabViewControl.SelectedItem = item;
        _suppressSelectionEvent = false;
    }

    private void RefreshHeader(TabViewItem item, TabModel tab)
    {
        // EffectiveTitle is a computed property; INPC events fire for
        // ShellReportedTitle / UserOverrideTitle. Re-set Header to
        // force the simple binding to re-read.
        item.Header = tab.EffectiveTitle;
    }

    private void OnAddTabButtonClick(TabView sender, object args) => _manager.NewTab();

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabViewItem item)
        {
            foreach (var (model, vi) in _itemByModel)
            {
                if (vi == item) { await RequestCloseTabAsync(model); return; }
            }
        }
    }

    /// <summary>
    /// Single entry point for every "close this tab" path: per-tab
    /// X button, middle-click, context-menu Close, and the keyboard
    /// chord (via <see cref="MainWindow"/>'s accelerator handler).
    /// Shows the multi-pane confirmation dialog when needed and only
    /// then calls <see cref="TabManager.CloseTab"/>. Centralising
    /// here keeps every close path consistent.
    /// </summary>
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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvent) return;
        if (TabViewControl.SelectedItem is TabViewItem item)
        {
            foreach (var (model, vi) in _itemByModel)
            {
                if (vi == item) { _manager.Activate(model); return; }
            }
        }
    }

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        if (args.Item is TabViewItem item)
        {
            var newIndex = TabViewControl.TabItems.IndexOf(item);
            foreach (var (model, vi) in _itemByModel)
            {
                if (vi == item)
                {
                    var oldIndex = _manager.IndexOf(model);
                    if (oldIndex != newIndex && oldIndex >= 0)
                        _manager.Move(oldIndex, newIndex);
                    return;
                }
            }
        }
    }

}
