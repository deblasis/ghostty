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

    private readonly List<GradientPointModel> _points = new();

    public event EventHandler<IReadOnlyList<GradientPointModel>>? PointsChanged;

    public GradientPointsEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Replaces the editor state. Does NOT raise PointsChanged --
    /// this is the load path, not a user edit.
    /// </summary>
    public void SetPoints(IReadOnlyList<GradientPointModel> points)
    {
        _points.Clear();
        _points.AddRange(points.Take(MaxPoints));
        // Rendering + rows wired in a later task.
    }
}

/// <summary>
/// Editor-local DTO for a gradient point. Maps 1:1 to
/// <c>Ghostty.Services.GradientPoint</c>; the control does not
/// reference the service layer.
/// </summary>
public readonly record struct GradientPointModel(
    float X, float Y, Color Color, float Radius);
