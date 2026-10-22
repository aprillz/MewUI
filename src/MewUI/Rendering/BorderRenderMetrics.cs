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
    /// <summary>
    /// Computes the metrics for <paramref name="bounds"/> at <paramref name="dpiScale"/>; every input must
    /// already be snapped to device pixels.
    /// </summary>
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

    /// <summary>Gets the outer bounds of the border.</summary>
    public Rect Bounds { get; }

    /// <summary>Gets the DPI scale the metrics were snapped at.</summary>
    public double DpiScale { get; }

    /// <summary>Gets the per-side border thickness.</summary>
    public Thickness BorderThickness { get; }

    /// <summary>Gets the outer corner radii.</summary>
    public CornerRadius CornerRadius { get; }

    /// <summary>Gets the bounds inside the border.</summary>
    public Rect InnerBounds { get; }

    /// <summary>Gets the horizontal inner radius of the top-left corner.</summary>
    public double InnerTopLeftX { get; }

    /// <summary>Gets the vertical inner radius of the top-left corner.</summary>
    public double InnerTopLeftY { get; }

    /// <summary>Gets the horizontal inner radius of the top-right corner.</summary>
    public double InnerTopRightX { get; }

    /// <summary>Gets the vertical inner radius of the top-right corner.</summary>
    public double InnerTopRightY { get; }

    /// <summary>Gets the horizontal inner radius of the bottom-right corner.</summary>
    public double InnerBottomRightX { get; }

    /// <summary>Gets the vertical inner radius of the bottom-right corner.</summary>
    public double InnerBottomRightY { get; }

    /// <summary>Gets the horizontal inner radius of the bottom-left corner.</summary>
    public double InnerBottomLeftX { get; }

    /// <summary>Gets the vertical inner radius of the bottom-left corner.</summary>
    public double InnerBottomLeftY { get; }

    /// <summary>Gets whether all four sides have the same thickness.</summary>
    public bool IsUniformThickness { get; }

    /// <summary>Gets whether all four corners have the same radius.</summary>
    public bool IsUniformRadius { get; }

    /// <summary>Gets whether thickness and radius are both uniform, so the uniform accessors describe every side and corner.</summary>
    public bool IsSimple => IsUniformThickness && IsUniformRadius;

    // Uniform accessors (meaningful only when IsSimple is true)
    /// <summary>Gets the thickness of every side when <see cref="IsSimple"/>; otherwise only the left side.</summary>
    public double UniformThickness => BorderThickness.Left;

    /// <summary>Gets the radius of every corner when <see cref="IsSimple"/>; otherwise only the top-left corner.</summary>
    public double UniformRadius => CornerRadius.TopLeft;

    /// <summary>Gets the inner radius of every corner when <see cref="IsSimple"/>; otherwise only the top-left horizontal inner radius.</summary>
    public double UniformInnerRadius => InnerTopLeftX;

    /// <summary>True when any side has a positive thickness. Use this (not <see cref="UniformThickness"/>, which is
    /// only the Left side) to gate border drawing on the non-uniform path, where Left may be 0 while others are not.</summary>
    public readonly bool HasAnyBorder =>
        BorderThickness.Left > 0 || BorderThickness.Top > 0 || BorderThickness.Right > 0 || BorderThickness.Bottom > 0;
}
