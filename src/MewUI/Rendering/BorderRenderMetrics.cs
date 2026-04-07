// Portions of this file are derived from dotnet/wpf (MIT License).
// https://github.com/dotnet/wpf
// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT License. See https://github.com/dotnet/wpf/blob/main/LICENSE.TXT

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Pre-computed, DPI-snapped border metrics for rendering.
/// All values are snapped to device pixels before construction.
/// Uses fill-based rendering (not stroke-based), so inner radii use full border
/// thickness (not half) to keep all coordinates pixel-aligned.
/// </summary>
public ref struct BorderRenderMetrics
{
    public BorderRenderMetrics(Rect bounds, double dpiScale, Thickness borderThickness, CornerRadius cornerRadius)
    {
        Bounds = bounds;
        DpiScale = dpiScale;
        BorderThickness = borderThickness;
        CornerRadius = cornerRadius;

        InnerBounds = bounds.Deflate(borderThickness);

        // Per-corner per-axis inner radii: radius - fullThickness (per adjacent side).
        // All inputs are snapped, so results remain pixel-aligned.
        InnerTopLeftX = Math.Max(0, cornerRadius.TopLeft - borderThickness.Left);
        InnerTopLeftY = Math.Max(0, cornerRadius.TopLeft - borderThickness.Top);
        InnerTopRightX = Math.Max(0, cornerRadius.TopRight - borderThickness.Right);
        InnerTopRightY = Math.Max(0, cornerRadius.TopRight - borderThickness.Top);
        InnerBottomRightX = Math.Max(0, cornerRadius.BottomRight - borderThickness.Right);
        InnerBottomRightY = Math.Max(0, cornerRadius.BottomRight - borderThickness.Bottom);
        InnerBottomLeftX = Math.Max(0, cornerRadius.BottomLeft - borderThickness.Left);
        InnerBottomLeftY = Math.Max(0, cornerRadius.BottomLeft - borderThickness.Bottom);

        IsUniformThickness = borderThickness.IsUniform;
        IsUniformRadius = cornerRadius.IsUniform;
    }

    public Rect Bounds { get; }
    public double DpiScale { get; }
    public Thickness BorderThickness { get; }
    public CornerRadius CornerRadius { get; }

    public Rect InnerBounds { get; }
    public double InnerTopLeftX { get; }
    public double InnerTopLeftY { get; }
    public double InnerTopRightX { get; }
    public double InnerTopRightY { get; }
    public double InnerBottomRightX { get; }
    public double InnerBottomRightY { get; }
    public double InnerBottomLeftX { get; }
    public double InnerBottomLeftY { get; }

    public bool IsUniformThickness { get; }
    public bool IsUniformRadius { get; }
    public bool IsSimple => IsUniformThickness && IsUniformRadius;

    // Uniform accessors (meaningful only when IsSimple is true)
    public double UniformThickness => BorderThickness.Left;
    public double UniformRadius => CornerRadius.TopLeft;
    public double UniformInnerRadius => InnerTopLeftX;
}
