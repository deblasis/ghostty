using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Ghostty.Controls.Settings;

/// <summary>
/// Visual 2D editor for background-gradient-point values. Position
/// editing via drag on the canvas; radius via numeric input below.
/// The editor is stateless from the config perspective: consumers
/// call SetPoints to load, then listen for PointsChanged to persist.
/// </summary>
public sealed partial class GradientPointsEditor : UserControl
{
    // Maximum gradient points supported by libghostty's config.
    public const int MaxPoints = 5;

    // Handle dot is 14 px diameter (normalized coords computed per-frame
    // based on actual canvas pixel size).
    private const double HandleDiameter = 14.0;

    private readonly List<GradientPointModel> _points = new();

    public event EventHandler<IReadOnlyList<GradientPointModel>>? PointsChanged;

    public IReadOnlyList<GradientPointModel> Points => _points;

    public GradientPointsEditor()
    {
        InitializeComponent();
        PointsCanvas.SizeChanged += (_, _) => RenderCanvas();
    }

    /// <summary>
    /// Replaces the editor state. Does NOT raise PointsChanged --
    /// this is the load path, not a user edit.
    /// </summary>
    public void SetPoints(IReadOnlyList<GradientPointModel> points)
    {
        _points.Clear();
        _points.AddRange(points.Take(MaxPoints));
        RenderCanvas();
    }

    private void RenderCanvas()
    {
        PointsCanvas.Children.Clear();
        var w = PointsCanvas.ActualWidth;
        var h = PointsCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            // Falloff: an Ellipse sized to 2*Radius in normalized space,
            // filled with a RadialGradientBrush from opaque center to
            // transparent edge.
            var falloffSize = 2.0 * p.Radius * Math.Min(w, h);
            var falloff = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = falloffSize,
                Height = falloffSize,
                IsHitTestVisible = false,
                Fill = BuildFalloffBrush(p.Color),
            };
            Canvas.SetLeft(falloff, p.X * w - falloffSize / 2);
            Canvas.SetTop(falloff, p.Y * h - falloffSize / 2);
            PointsCanvas.Children.Add(falloff);

            // Handle: a small white-bordered solid dot above the falloff.
            var handle = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = HandleDiameter,
                Height = HandleDiameter,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.Color),
                Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.White),
                StrokeThickness = 2,
                Tag = i,
            };
            Canvas.SetLeft(handle, p.X * w - HandleDiameter / 2);
            Canvas.SetTop(handle, p.Y * h - HandleDiameter / 2);
            PointsCanvas.Children.Add(handle);
        }
    }

    private static Microsoft.UI.Xaml.Media.Brush BuildFalloffBrush(Color c)
    {
        // RadialGradientBrush: opaque at center, transparent at edge.
        // This is a visual editor aid ONLY -- it does not reflect blend
        // mode, opacity, or animation of the real gradient.
        var brush = new Microsoft.UI.Xaml.Media.RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
        {
            Color = Color.FromArgb(0xCC, c.R, c.G, c.B),
            Offset = 0.0,
        });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
        {
            Color = Color.FromArgb(0x00, c.R, c.G, c.B),
            Offset = 1.0,
        });
        return brush;
    }
}

/// <summary>
/// Editor-local DTO for a gradient point. Maps 1:1 to
/// <c>Ghostty.Services.GradientPoint</c>; the control does not
/// reference the service layer.
/// </summary>
public readonly record struct GradientPointModel(
    float X, float Y, Color Color, float Radius);
