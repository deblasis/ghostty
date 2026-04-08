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

    /// <summary>Raised when a row's hover-only close button is clicked.
    /// The consuming <see cref="VerticalTabHost"/> routes this through
    /// its shared <c>RequestCloseTabAsync</c>.</summary>
    public event Func<TabModel, System.Threading.Tasks.Task>? CloseRequestedFromRow;

    private bool _isExpanded;

    /// <summary>
    /// Toggle between collapsed (40x40 icon-only) and expanded
    /// (36px-tall, icon + title + hover X) row templates. The
    /// animation of the column width itself lives in
    /// <see cref="VerticalTabHost"/>; this property only affects
    /// per-row rendering.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            foreach (var (tab, row) in _itemByModel)
                ApplyRowTemplate(row, tab, _isExpanded);
            UpdateAccentBar();
        }
    }

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
        var row = new ListViewItem
        {
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            DataContext = tab,
        };
        ApplyRowTemplate(row, tab, _isExpanded);
        _itemByModel[tab] = row;
        TabList.Items.Add(row);

        // Refresh title text on tab title changes (only matters in expanded mode).
        tab.PropertyChanged += (_, _) =>
        {
            if (_isExpanded) ApplyRowTemplate(row, tab, _isExpanded);
        };
    }

    private void ApplyRowTemplate(ListViewItem row, TabModel tab, bool expanded)
    {
        if (!expanded)
        {
            // Collapsed 40x40: centered icon, no title, no close button.
            row.Width = 40;
            row.Height = 40;
            row.Content = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = "\uE756", // CommandPrompt
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        // Expanded 36px tall: [icon] [title stretching] [hover X]
        row.Width = double.NaN;
        row.Height = 36;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var icon = new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = "\uE756",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var title = new TextBlock
        {
            Text = tab.EffectiveTitle,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 4, 0),
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var closeBtn = new Button
        {
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = "\uE894", // ChromeClose
                FontSize = 10,
            },
            Opacity = 0,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeBtn.Click += async (_, _) =>
        {
            if (CloseRequestedFromRow is { } handler)
                await handler(tab);
        };
        Grid.SetColumn(closeBtn, 2);
        grid.Children.Add(closeBtn);

        // Show close button on row hover.
        row.PointerEntered += (_, _) =>
        {
            if (!_isExpanded) return;
            closeBtn.Opacity = 1;
            closeBtn.IsHitTestVisible = true;
        };
        row.PointerExited += (_, _) =>
        {
            closeBtn.Opacity = 0;
            closeBtn.IsHitTestVisible = false;
        };

        row.Content = grid;
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
