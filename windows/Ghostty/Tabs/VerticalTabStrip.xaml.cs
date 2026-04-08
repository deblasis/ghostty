using System;
using System.Collections.Generic;
using Ghostty.Core.Tabs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Ghostty.Tabs;

/// <summary>
/// The icon rail / row list visual for vertical tabs. Subscribes
/// to a <see cref="TabManager"/> and renders per-tab rows (40x40
/// centered icon in collapsed mode; an expanded row template is
/// added in a later commit). Owns the active-tab accent bar
/// overlay and the chevron + new-tab buttons.
///
/// Collapsed mode only in this task.
/// </summary>
internal sealed partial class VerticalTabStrip : UserControl
{
    private readonly TabManager _manager;
    private readonly Dictionary<TabModel, ListViewItem> _itemByModel = new();

    /// <summary>Raised when the user clicks the chevron toggle.</summary>
    public event EventHandler? ChevronToggled;

    /// <summary>Raised when the user clicks the new-tab "+" button.</summary>
    public event EventHandler? NewTabRequested;

    public VerticalTabStrip(TabManager manager)
    {
        InitializeComponent();
        _manager = manager;

        foreach (var t in _manager.Tabs) AddRow(t);
        UpdateAccentBar();

        _manager.TabAdded += (_, t) => { AddRow(t); UpdateAccentBar(); };
        _manager.TabRemoved += (_, t) => { RemoveRow(t); UpdateAccentBar(); };
        _manager.TabMoved += (_, e) => { MoveRow(e.tab, e.to); UpdateAccentBar(); };
        _manager.ActiveTabChanged += (_, _) => UpdateAccentBar();

        // Reposition the accent bar whenever layout settles.
        TabList.LayoutUpdated += (_, _) => UpdateAccentBar();
    }

    private void AddRow(TabModel tab)
    {
        // Collapsed-mode row: 40x40, centered icon. Per-shell icons
        // come once profiles exist (plan 3); for now a generic glyph.
        var icon = new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = "\uE756", // CommandPrompt
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new ListViewItem
        {
            Content = icon,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            DataContext = tab,
        };
        _itemByModel[tab] = row;
        TabList.Items.Add(row);
    }

    private void RemoveRow(TabModel tab)
    {
        if (!_itemByModel.TryGetValue(tab, out var row)) return;
        TabList.Items.Remove(row);
        _itemByModel.Remove(tab);
    }

    private void MoveRow(TabModel tab, int to)
    {
        if (!_itemByModel.TryGetValue(tab, out var row)) return;
        TabList.Items.Remove(row);
        TabList.Items.Insert(to, row);
    }

    private void UpdateAccentBar()
    {
        if (!_itemByModel.TryGetValue(_manager.ActiveTab, out var row)) return;
        if (row.ActualHeight <= 0) return; // not laid out yet
        var transform = row.TransformToVisual(AccentOverlay);
        var origin = transform.TransformPoint(new Point(0, 0));
        AccentBar.Height = row.ActualHeight;
        Canvas.SetLeft(AccentBar, 0);
        Canvas.SetTop(AccentBar, origin.Y);
    }

    private void OnChevronClick(object sender, RoutedEventArgs e) =>
        ChevronToggled?.Invoke(this, EventArgs.Empty);

    private void OnNewTabClick(object sender, RoutedEventArgs e) =>
        NewTabRequested?.Invoke(this, EventArgs.Empty);

    private void OnTabRowClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListViewItem row && row.DataContext is TabModel tab)
            _manager.Activate(tab);
    }
}
