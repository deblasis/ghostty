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
    private readonly List<CompositionAnimation> _animations = [];

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

    /// <summary>
    /// Apply animation to the gradient based on the configured mode.
    /// </summary>
    internal void ApplyAnimation(string mode, float speed)
    {
        StopAnimations();

        if (mode is "static" or "") return;
        if (_rootVisual.Children.Count == 0) return;

        var duration = TimeSpan.FromSeconds(8.0 / Math.Max(speed, 0.01f));

        if (mode is "drift" or "drift-color-cycle")
            ApplyDrift(duration);

        if (mode is "color-cycle" or "drift-color-cycle")
            ApplyColorCycle(duration);
    }

    private void ApplyDrift(TimeSpan duration)
    {
        var rng = new Random(42);
        var index = 0;
        foreach (var child in _rootVisual.Children)
        {
            if (child is not SpriteVisual sprite) continue;
            if (sprite.Brush is not CompositionRadialGradientBrush brush) continue;

            var center = brush.EllipseCenter;
            // Drift radius: 5% of window in each direction.
            var dx = 0.05f * (float)(rng.NextDouble() * 2 - 1);
            var dy = 0.05f * (float)(rng.NextDouble() * 2 - 1);
            // Phase offset per point so they don't all move in sync.
            var phase = (float)index / _rootVisual.Children.Count;

            var anim = _compositor.CreateVector2KeyFrameAnimation();
            anim.InsertKeyFrame(0f, center);
            anim.InsertKeyFrame(0.25f + phase * 0.1f,
                new Vector2(center.X + dx, center.Y + dy));
            anim.InsertKeyFrame(0.5f, center);
            anim.InsertKeyFrame(0.75f - phase * 0.1f,
                new Vector2(center.X - dx, center.Y - dy));
            anim.InsertKeyFrame(1f, center);
            anim.Duration = duration;
            anim.IterationBehavior = AnimationIterationBehavior.Forever;

            brush.StartAnimation("EllipseCenter", anim);
            _animations.Add(anim);
            index++;
        }
    }

    private void ApplyColorCycle(TimeSpan duration)
    {
        foreach (var child in _rootVisual.Children)
        {
            if (child is not SpriteVisual sprite) continue;
            if (sprite.Brush is not CompositionRadialGradientBrush brush) continue;
            if (brush.ColorStops.Count < 1) continue;

            var stop = brush.ColorStops[0];
            var baseColor = stop.Color;

            // Rotate hue by cycling through HSL space.
            // We approximate with 6 keyframes at 60-degree hue intervals.
            var anim = _compositor.CreateColorKeyFrameAnimation();
            for (int i = 0; i <= 6; i++)
            {
                var hueShift = i * 60f;
                var shifted = ShiftHue(baseColor, hueShift);
                anim.InsertKeyFrame(i / 6f, shifted);
            }
            anim.Duration = duration;
            anim.IterationBehavior = AnimationIterationBehavior.Forever;

            stop.StartAnimation("Color", anim);
            _animations.Add(anim);
        }
    }

    private void StopAnimations()
    {
        foreach (var child in _rootVisual.Children)
        {
            if (child is not SpriteVisual sprite) continue;
            if (sprite.Brush is not CompositionRadialGradientBrush brush) continue;
            brush.StopAnimation("EllipseCenter");
            foreach (var stop in brush.ColorStops)
                stop.StopAnimation("Color");
        }
        _animations.Clear();
    }

    /// <summary>
    /// Shift the hue of an RGB color by the given degrees.
    /// </summary>
    private static Windows.UI.Color ShiftHue(Windows.UI.Color color, float degrees)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float l = (max + min) / 2f;

        if (max == min)
            return color;

        float d = max - min;
        float s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        float h;

        if (max == r)
            h = ((g - b) / d + (g < b ? 6f : 0f)) * 60f;
        else if (max == g)
            h = ((b - r) / d + 2f) * 60f;
        else
            h = ((r - g) / d + 4f) * 60f;

        h = (h + degrees) % 360f;
        if (h < 0) h += 360f;

        return HslToColor(color.A, h, s, l);
    }

    private static Windows.UI.Color HslToColor(byte a, float h, float s, float l)
    {
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs(h / 60f % 2f - 1f));
        float m = l - c / 2f;

        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Windows.UI.Color.FromArgb(a,
            (byte)((r + m) * 255f),
            (byte)((g + m) * 255f),
            (byte)((b + m) * 255f));
    }

    public void Dispose()
    {
        _rootVisual.Children.RemoveAll();
        _rootVisual.Brush?.Dispose();
        _rootVisual.Dispose();
    }
}
