using System;
using System.ComponentModel;
using Ghostty.Commands;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI;

namespace Ghostty.Controls.CommandPalette;

/// <summary>
/// Floating command-palette control.
///
/// The control is intentionally dumb: it owns only the visual tree and
/// user-interaction handling. All state lives in <see cref="CommandPaletteViewModel"/>.
/// The two are connected by <see cref="Bind"/>, which subscribes to
/// PropertyChanged and pushes ViewModel state into the named XAML elements.
///
/// We deliberately avoid x:Bind / DataContext assignment because
/// <see cref="CommandPaletteViewModel"/> is an <c>internal</c> type and
/// AOT-safe x:Bind requires public types. Code-behind binding lets the type
/// stay internal.
/// </summary>
internal sealed partial class CommandPaletteControl : UserControl
{
    private CommandPaletteViewModel? _vm;

    // Suppress "Event never fired" — fired by WinUI runtime via ContainerContentChanging.
    public CommandPaletteControl()
    {
        InitializeComponent();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Connects the control to a <see cref="CommandPaletteViewModel"/>.
    /// May be called multiple times; each call replaces the previous subscription.
    /// </summary>
    public void Bind(CommandPaletteViewModel viewModel)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = viewModel;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Sync initial state
        SyncAll();
    }

    // ── ViewModel → UI ────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // All UI updates must happen on the UI thread.
        if (DispatcherQueue is null) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_vm is null) return;

            switch (e.PropertyName)
            {
                case nameof(CommandPaletteViewModel.ModeLabel):
                    ModeLabel.Text = _vm.ModeLabel;
                    UpdateFooterHints();
                    break;

                case nameof(CommandPaletteViewModel.IsPinned):
                    PinButton.IsChecked = _vm.IsPinned;
                    break;

                case nameof(CommandPaletteViewModel.SearchText):
                    // Only sync when ViewModel drives the change (not when
                    // the TextBox itself fired TextChanged — avoids cursor jump).
                    if (SearchBox.Text != _vm.SearchText)
                        SearchBox.Text = _vm.SearchText;
                    break;

                case nameof(CommandPaletteViewModel.FilteredCommands):
                    SyncFilteredCommands();
                    break;

                case nameof(CommandPaletteViewModel.SelectedCommand):
                    SyncSelectedItem();
                    break;

                case nameof(CommandPaletteViewModel.StatusText):
                    StatusLabel.Text = _vm.StatusText;
                    LiveAnnouncer.Text = _vm.StatusText;
                    break;
            }
        });
    }

    private void SyncAll()
    {
        if (_vm is null) return;

        ModeLabel.Text = _vm.ModeLabel;
        PinButton.IsChecked = _vm.IsPinned;
        SearchBox.Text = _vm.SearchText;
        StatusLabel.Text = _vm.StatusText;
        SyncFilteredCommands();
        UpdateFooterHints();
    }

    private void SyncFilteredCommands()
    {
        if (_vm is null) return;

        // Replace ItemsSource with a new list snapshot so WinUI re-creates
        // the containers and ContainerContentChanging fires for all items.
        ResultsList.ItemsSource = _vm.FilteredCommands.ToArray();

        SyncSelectedItem();
    }

    private void SyncSelectedItem()
    {
        if (_vm is null) return;

        // Map ViewModel.SelectedCommand back to the ListView's SelectedItem.
        // The ListView's ItemsSource is a CommandItem[], so we match by index.
        if (_vm.SelectedCommand is null)
        {
            ResultsList.SelectedItem = null;
            return;
        }

        var idx = _vm.FilteredCommands.IndexOf(_vm.SelectedCommand);
        if (idx >= 0 && ResultsList.Items.Count > idx)
        {
            ResultsList.SelectedIndex = idx;
            ResultsList.ScrollIntoView(ResultsList.Items[idx]);
        }
    }

    private void UpdateFooterHints()
    {
        if (_vm is null) return;

        ShortcutHints.Text = _vm.Mode == PaletteMode.CommandLine
            ? "Tab autocomplete   ↑↓ navigate   ↵ run   Esc close"
            : "↑↓ navigate   ↵ run   Esc close";
    }

    // ── ContainerContentChanging: populate DataTemplate elements ─────────────

    /// <summary>
    /// Populates the named elements inside each <see cref="CommandItemTemplate"/>
    /// with data from the corresponding <see cref="CommandItem"/>.
    ///
    /// This replaces XAML data-binding: because <see cref="CommandItem"/> is
    /// <c>internal</c>, x:Bind would require public types, and regular
    /// {Binding} on a record is fragile with AOT. Code-behind element lookup
    /// is straightforward and AOT-safe.
    /// </summary>
    private void OnContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is not CommandItem item) return;

        // Phase 0 fires synchronously during measure; grab the template root.
        if (args.ItemContainer.ContentTemplateRoot is not Grid root) return;

        // Color dot
        if (root.FindName("LeadingColorDot") is Ellipse dot)
        {
            if (item.LeadingColor is { } color)
            {
                dot.Fill = new SolidColorBrush(
                    Color.FromArgb(color.A, color.R, color.G, color.B));
                dot.Visibility = Visibility.Visible;
            }
            else
            {
                dot.Visibility = Visibility.Collapsed;
            }
        }

        // Leading icon glyph
        if (root.FindName("LeadingIcon") is FontIcon icon)
        {
            if (!string.IsNullOrEmpty(item.LeadingIcon))
            {
                icon.Glyph = item.LeadingIcon;
                icon.Visibility = Visibility.Visible;
            }
            else
            {
                icon.Visibility = Visibility.Collapsed;
            }
        }

        // Title
        if (root.FindName("ItemTitle") is TextBlock title)
            title.Text = item.Title;

        // Description
        if (root.FindName("ItemDescription") is TextBlock desc)
        {
            if (!string.IsNullOrEmpty(item.Description))
            {
                desc.Text = item.Description;
                desc.Visibility = Visibility.Visible;
            }
            else
            {
                desc.Visibility = Visibility.Collapsed;
            }
        }

        // Shortcut
        if (root.FindName("ShortcutBorder") is Border shortcutBorder &&
            root.FindName("ShortcutText") is TextBlock shortcutText)
        {
            if (item.Shortcut is { } kb)
            {
                shortcutText.Text = FormatKeyBinding(kb);
                shortcutBorder.Visibility = Visibility.Visible;
            }
            else
            {
                shortcutBorder.Visibility = Visibility.Collapsed;
            }
        }
    }

    // ── UI → ViewModel ────────────────────────────────────────────────────────

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm is null) return;

        // Push the raw text into the ViewModel; OnSearchTextChanged partial
        // method there will update Mode, FilteredCommands, etc.
        _vm.SearchText = SearchBox.Text;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_vm is null) return;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var isCtrl = ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.Escape:
                _vm.Close();
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                _vm.ExecuteSelectedCommand();
                e.Handled = true;
                break;

            case VirtualKey.Tab:
                if (_vm.Mode == PaletteMode.CommandLine)
                {
                    _vm.AcceptAutocomplete();
                    // Sync the TextBox immediately so the cursor lands at end.
                    SearchBox.Text = _vm.SearchText;
                    SearchBox.SelectionStart = SearchBox.Text.Length;
                    e.Handled = true;
                }
                break;

            case VirtualKey.Up:
                _vm.MoveSelectionUp();
                e.Handled = true;
                break;

            case VirtualKey.Down:
                _vm.MoveSelectionDown();
                e.Handled = true;
                break;

            case VirtualKey.P when isCtrl:
                _vm.MoveSelectionUp();
                e.Handled = true;
                break;

            case VirtualKey.N when isCtrl:
                _vm.MoveSelectionDown();
                e.Handled = true;
                break;
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (_vm is null) return;
        if (e.ClickedItem is CommandItem item)
        {
            _vm.SelectedCommand = item;
            _vm.ExecuteSelectedCommand();
        }
    }

    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // When the user clicks a different item without executing, keep the
        // ViewModel's SelectedCommand in sync so keyboard Enter works on the
        // visually-selected item.
        if (_vm is null) return;
        if (ResultsList.SelectedItem is CommandItem item)
            _vm.SelectedCommand = item;
    }

    private void OnPinChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.IsPinned = true;
    }

    private void OnPinUnchecked(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.IsPinned = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats a <see cref="Input.KeyBinding"/> into a compact display string
    /// suitable for the shortcut key-cap, e.g. "Ctrl+Shift+P".
    /// </summary>
    private static string FormatKeyBinding(Input.KeyBinding kb)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (kb.Modifiers.HasFlag(VirtualKeyModifiers.Control)) parts.Add("Ctrl");
        if (kb.Modifiers.HasFlag(VirtualKeyModifiers.Menu))    parts.Add("Alt");
        if (kb.Modifiers.HasFlag(VirtualKeyModifiers.Shift))   parts.Add("Shift");
        if (kb.Modifiers.HasFlag(VirtualKeyModifiers.Windows)) parts.Add("Win");

        // Convert VirtualKey to a display name.
        var key = kb.Key.ToString();

        // Trim "Number" prefix from digit keys (VirtualKey.Number0 → "0")
        if (key.StartsWith("Number", StringComparison.Ordinal))
            key = key["Number".Length..];

        parts.Add(key);
        return string.Join("+", parts);
    }
}
