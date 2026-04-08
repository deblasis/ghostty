using System.Collections.Generic;
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
internal sealed partial class TabHost : UserControl
{
    private readonly TabManager _manager;
    private readonly Dictionary<TabModel, TabViewItem> _itemByModel = new();
    private bool _suppressSelectionEvent;

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
        var item = new TabViewItem
        {
            Header = tab.EffectiveTitle,
            Content = (PaneHost)tab.PaneHost,
            ContextFlyout = TabContextMenuBuilder.Build(_manager, tab),
            DataContext = tab,
        };
        item.HeaderTemplate = BuildHeaderTemplate();
        tab.PropertyChanged += (_, _) => RefreshHeader(item, tab);
        _itemByModel[tab] = item;
        TabViewControl.TabItems.Add(item);
    }

    private void RemoveItem(TabModel tab)
    {
        if (!_itemByModel.TryGetValue(tab, out var item)) return;
        TabViewControl.TabItems.Remove(item);
        _itemByModel.Remove(tab);
    }

    private void MoveItem(TabModel tab, int to)
    {
        if (!_itemByModel.TryGetValue(tab, out var item)) return;
        TabViewControl.TabItems.Remove(item);
        TabViewControl.TabItems.Insert(to, item);
    }

    private void SelectActive()
    {
        if (!_itemByModel.TryGetValue(_manager.ActiveTab, out var item)) return;
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

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabViewItem item)
        {
            foreach (var (model, vi) in _itemByModel)
            {
                if (vi == item) { _manager.CloseTab(model); return; }
            }
        }
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

    private static DataTemplate BuildHeaderTemplate()
    {
        // Inline template: title text over a 2px progress bar bound
        // to TabModel.Progress. The bar is invisible when Kind == None.
        const string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
            "              xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "    <StackPanel Orientation=\"Vertical\" Spacing=\"2\">" +
            "        <TextBlock Text=\"{Binding EffectiveTitle}\" />" +
            "        <ProgressBar" +
            "            Height=\"2\"" +
            "            Minimum=\"0\" Maximum=\"100\"" +
            "            Value=\"{Binding Progress.Percent}\"" +
            "            Visibility=\"{Binding Progress.State," +
            "                         Converter={StaticResource ProgressVisibilityConverter}}\"/>" +
            "    </StackPanel>" +
            "</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }
}
