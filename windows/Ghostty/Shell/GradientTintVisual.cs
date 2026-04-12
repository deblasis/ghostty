using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Ghostty.Services;

namespace Ghostty.Shell;

/// <summary>
/// Full-window composition visual that renders a multi-point radial
/// gradient tint. Inserted between the SystemBackdrop and RootGrid
/// so the gradient is visible through transparent UI content.
/// </summary>
internal sealed class GradientTintVisual : IDisposable
{
    private readonly Compositor _compositor;
    private readonly SpriteVisual _rootVisual;
    private readonly ContainerVisual _hostVisual;

    internal GradientTintVisual(UIElement host, IReadOnlyList<GradientPoint> points)
    {
        var hostVisual = ElementCompositionPreview.GetElementVisual(host);
        _compositor = hostVisual.Compositor;
        _hostVisual = hostVisual as ContainerVisual
            ?? _compositor.CreateContainerVisual();

        _rootVisual = _compositor.CreateSpriteVisual();
        _rootVisual.RelativeSizeAdjustment = Vector2.One;

        RebuildBrush(points);

        // Insert behind all XAML content so the gradient sits
        // between the SystemBackdrop and the RootGrid.
        ElementCompositionPreview.SetElementChildVisual(host, _rootVisual);
    }

    /// <summary>
    /// Rebuild the gradient brush from a new set of points.
    /// </summary>
    internal void RebuildBrush(IReadOnlyList<GradientPoint> points)
    {
        if (points.Count == 0)
        {
            _rootVisual.Brush = null;
            return;
        }

        if (points.Count == 1)
        {
            _rootVisual.Brush = CreateSinglePointBrush(points[0]);
            return;
        }

        // For multiple points, layer radial gradient brushes using
        // additive blending via CompositionColorBrush per-point
        // sprites in a container visual.
        _rootVisual.Brush = CreateMultiPointBrush(points);
    }

    private CompositionBrush CreateSinglePointBrush(GradientPoint pt)
    {
        var brush = _compositor.CreateRadialGradientBrush();
        brush.EllipseCenter = new Vector2(pt.X, pt.Y);
        brush.EllipseRadius = new Vector2(pt.Radius);
        brush.MappingMode = CompositionMappingMode.Relative;

        var stop0 = _compositor.CreateColorGradientStop(0f, pt.Color);
        var stop1 = _compositor.CreateColorGradientStop(1f,
            Windows.UI.Color.FromArgb(0, pt.Color.R, pt.Color.G, pt.Color.B));
        brush.ColorStops.Add(stop0);
        brush.ColorStops.Add(stop1);

        return brush;
    }

    private CompositionBrush CreateMultiPointBrush(IReadOnlyList<GradientPoint> points)
    {
        // Use a container visual with one sprite per point, each
        // with its own radial gradient brush. The sprites use
        // additive blending to naturally merge the color blobs.
        //
        // Clear any previous children.
        _rootVisual.Children.RemoveAll();

        foreach (var pt in points)
        {
            var sprite = _compositor.CreateSpriteVisual();
            sprite.RelativeSizeAdjustment = Vector2.One;
            sprite.Brush = CreateSinglePointBrush(pt);
            _rootVisual.Children.InsertAtTop(sprite);
        }

        // Return null for the root brush since children handle rendering.
        return null!;
    }

    /// <summary>
    /// Update the visual opacity to track background-opacity.
    /// </summary>
    internal void SetOpacity(float opacity)
    {
        _rootVisual.Opacity = opacity;
    }

    public void Dispose()
    {
        _rootVisual.Children.RemoveAll();
        _rootVisual.Brush?.Dispose();
        _rootVisual.Dispose();
    }
}
