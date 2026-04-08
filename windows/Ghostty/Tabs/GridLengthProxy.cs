using Microsoft.UI.Xaml;

namespace Ghostty.Tabs;

/// <summary>
/// Bridges a <see cref="Microsoft.UI.Xaml.Media.Animation.DoubleAnimation"/>
/// onto a <see cref="ColumnDefinition.Width"/>. WinUI 3's
/// <see cref="GridLength"/> is a struct without a DependencyProperty
/// path, so animations cannot target it directly. The standard
/// workaround is to animate a <see cref="double"/> on a small
/// <see cref="DependencyObject"/> and forward each tick to the
/// real column width via the property-changed callback.
/// </summary>
internal sealed class GridLengthProxy : DependencyObject
{
    private readonly ColumnDefinition _column;

    public GridLengthProxy(ColumnDefinition column) { _column = column; }

    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(
            nameof(Width),
            typeof(double),
            typeof(GridLengthProxy),
            new PropertyMetadata(0.0, OnWidthChanged));

    public double Width
    {
        get => (double)GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    private static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (GridLengthProxy)d;
        self._column.Width = new GridLength((double)e.NewValue);
    }
}
