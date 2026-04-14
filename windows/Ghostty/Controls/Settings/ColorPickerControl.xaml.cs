using System;
using Ghostty.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindowsColor = Windows.UI.Color;
using WindowsColors = Microsoft.UI.Colors;

namespace Ghostty.Controls.Settings;

/// <summary>
/// Inline color picker: swatch + hex TextBox + flyout with the WinUI
/// ColorPicker (HSV square, hue slider, RGB/hex inputs). Consumers see
/// a single <see cref="Color"/> dependency property (a "#RRGGBB"
/// string) and a <see cref="ColorChanged"/> event that fires only when
/// the user commits -- flyout close or hex box LostFocus -- so config
/// writes are not spammed on every pointer move inside the picker.
///
/// Windows.UI.Color is aliased to WindowsColor because the control's
/// own Color dependency property (a string) would otherwise shadow
/// the type name everywhere in this file.
/// </summary>
public sealed partial class ColorPickerControl : UserControl
{
    public ColorPickerControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(string),
            typeof(ColorPickerControl),
            new PropertyMetadata(string.Empty, OnColorPropertyChanged));

    /// <summary>
    /// Hex color string in "#RRGGBB" form. Setting this externally
    /// updates the swatch, hex TextBox, and built-in ColorPicker
    /// without firing <see cref="ColorChanged"/> (reserved for user
    /// edits only).
    /// </summary>
    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Raised when the user commits a new color via the flyout closing
    /// or the hex box losing focus with a valid value. The argument is
    /// the new hex string in "#RRGGBB" form.
    /// </summary>
    public event EventHandler<string>? ColorChanged;

    // Guards against re-entrancy when the picker or hex box drives the
    // Color DP: skip pushing the value back into the control that just
    // raised it (would reset caret position in HexBox etc.).
    private bool _suppressPush;

    // Null until the user moves something inside the flyout. On first
    // Picker_ColorChanged we capture the pre-edit Color (which can be
    // empty -- "no override set"); on flyout close we fire only if the
    // user actually interacted, regardless of whether the starting
    // value was empty.
    private string? _valueAtFlyoutOpen;

    private static void OnColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ColorPickerControl)d;
        var hex = (string?)e.NewValue ?? string.Empty;
        control.ApplyHex(hex, pushToPicker: !control._suppressPush, pushToHexBox: !control._suppressPush);
    }

    private void ApplyHex(string hex, bool pushToPicker, bool pushToHexBox)
    {
        if (!Rgb.TryParseHex(hex, out var rgb))
        {
            // Unparseable input. Clear the swatch in every case; only
            // clear the hex TextBox when we were asked to push (i.e.
            // the consumer set Color = "" to mean "no override"). When
            // pushToHexBox is false we're reacting to the user mid-edit
            // and overwriting their text would steal the caret.
            SwatchBorder.Background = new SolidColorBrush(WindowsColors.Transparent);
            if (pushToHexBox) HexBox.Text = string.Empty;
            return;
        }

        var normalized = rgb.ToHex();
        var color = WindowsColor.FromArgb(0xFF, rgb.R, rgb.G, rgb.B);
        SwatchBorder.Background = new SolidColorBrush(color);

        if (pushToHexBox && HexBox.Text != normalized)
        {
            HexBox.Text = normalized;
        }

        if (pushToPicker)
        {
            Picker.Color = color;
        }
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var input = HexBox.Text;
        if (!Rgb.TryParseHex(input, out var rgb))
        {
            // Revert to last valid value.
            ApplyHex(Color, pushToPicker: false, pushToHexBox: true);
            return;
        }

        var normalized = rgb.ToHex();
        if (string.Equals(normalized, Color, StringComparison.OrdinalIgnoreCase))
        {
            // Value unchanged; still normalize casing in the box.
            if (HexBox.Text != normalized) HexBox.Text = normalized;
            return;
        }

        _suppressPush = true;
        try
        {
            Color = normalized;
            HexBox.Text = normalized;
            Picker.Color = WindowsColor.FromArgb(0xFF, rgb.R, rgb.G, rgb.B);
        }
        finally
        {
            _suppressPush = false;
        }

        ColorChanged?.Invoke(this, normalized);
    }

    private void Picker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        // Live preview: swatch and hex box reflect the in-progress
        // pick so the user sees both representations while dragging.
        // The ColorChanged event itself is deferred to flyout close.
        // Capture the pre-edit value the first time the picker fires
        // during this flyout session, even if Color is currently "".
        _valueAtFlyoutOpen ??= Color;

        var c = args.NewColor;
        var rgb = new Rgb(c.R, c.G, c.B);
        var hex = rgb.ToHex();

        SwatchBorder.Background = new SolidColorBrush(c);
        if (HexBox.Text != hex) HexBox.Text = hex;

        _suppressPush = true;
        try { Color = hex; }
        finally { _suppressPush = false; }
    }

    private void PickerFlyout_Closed(object sender, object e)
    {
        if (_valueAtFlyoutOpen is null) return; // never interacted

        var before = _valueAtFlyoutOpen;
        _valueAtFlyoutOpen = null;
        if (!string.Equals(Color, before, StringComparison.OrdinalIgnoreCase))
        {
            ColorChanged?.Invoke(this, Color);
        }
    }
}
