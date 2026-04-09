using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract base class for <see cref="IGraphicsContext"/> implementations.
/// Provides viewport-based early culling, pixel-snap and geometric-transform logic
/// so that all backends share the same canonical behaviour.
/// </summary>
public abstract class GraphicsContextBase : IGraphicsContext
{
    #region Viewport Culling

    private static readonly Rect InfiniteCullRect = new(-1_000_000, -1_000_000, 2_000_000, 2_000_000);

    private Rect _cullRect = InfiniteCullRect;
    private readonly Stack<Rect> _cullStack = new();

    private int _drawCalls;
    private int _cullCount;

    /// <summary>Total draw/fill calls attempted this frame.</summary>
    public int DrawCallCount => _drawCalls;

    /// <summary>Draw/fill calls skipped by viewport culling this frame.</summary>
    public int CullCount => _cullCount;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="bounds"/> is entirely outside the visible area.
    /// </summary>
    protected bool IsCulled(Rect bounds)
    {
        _drawCalls++;
        if (!bounds.IntersectsWith(_cullRect))
        {
            _cullCount++;
            return true;
        }
        return false;
    }

    #endregion

    #region State Management (template methods — cull rect tracking)

    public void Save()
    {
        _cullStack.Push(_cullRect);
        SaveCore();
    }

    public void Restore()
    {
        if (_cullStack.Count > 0)
            _cullRect = _cullStack.Pop();
        RestoreCore();
    }

    public void SetClip(Rect rect)
    {
        _cullRect = _cullRect.Intersect(rect);
        SetClipCore(rect);
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        _cullRect = _cullRect.Intersect(rect);
        SetClipRoundedRectCore(rect, radiusX, radiusY);
    }

    public void Translate(double dx, double dy)
    {
        _cullRect = new Rect(_cullRect.X - dx, _cullRect.Y - dy, _cullRect.Width, _cullRect.Height);
        TranslateCore(dx, dy);
    }

    public void Rotate(double angleRadians)
    {
        if (angleRadians != 0)
            _cullRect = InfiniteCullRect;
        RotateCore(angleRadians);
    }

    public void Scale(double sx, double sy)
    {
        if (sx > 0 && sy > 0)
        {
            _cullRect = new Rect(
                _cullRect.X / sx, _cullRect.Y / sy,
                _cullRect.Width / sx, _cullRect.Height / sy);
        }
        else
        {
            _cullRect = InfiniteCullRect;
        }

        ScaleCore(sx, sy);
    }

    public void SetTransform(Matrix3x2 matrix)
    {
        _cullRect = InfiniteCullRect;
        SetTransformCore(matrix);
    }

    public Matrix3x2 GetTransform() => GetTransformCore();

    public void ResetTransform()
    {
        _cullRect = InfiniteCullRect;
        ResetTransformCore();
    }

    public void ResetClip()
    {
        _cullRect = InfiniteCullRect;
        ResetClipCore();
    }

    public void IntersectClip(Rect rect)
    {
        _cullRect = _cullRect.Intersect(rect);
        IntersectClipCore(rect);
    }

    protected abstract void SaveCore();

    protected abstract void RestoreCore();

    protected abstract void SetClipCore(Rect rect);

    protected abstract void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY);

    protected abstract void TranslateCore(double dx, double dy);

    protected virtual void RotateCore(double angleRadians) { }

    protected virtual void ScaleCore(double sx, double sy) { }

    protected virtual void SetTransformCore(Matrix3x2 matrix) { }

    protected virtual Matrix3x2 GetTransformCore() => Matrix3x2.Identity;

    protected virtual void ResetTransformCore() { }

    protected virtual void ResetClipCore() { }

    protected virtual void IntersectClipCore(Rect rect) => SetClipCore(rect);

    #endregion

    #region Drawing Primitives (template methods — cull check)

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        double halfT = thickness * 0.5;
        var lineBounds = new Rect(
            Math.Min(start.X, end.X) - halfT,
            Math.Min(start.Y, end.Y) - halfT,
            Math.Abs(end.X - start.X) + thickness,
            Math.Abs(end.Y - start.Y) + thickness);
        if (IsCulled(lineBounds)) return;
        DrawLineCore(start, end, color, thickness);
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        if (IsCulled(rect)) return;
        DrawRectangleCore(rect, color, thickness, false);
    }

    public void FillRectangle(Rect rect, Color color)
    {
        if (IsCulled(rect)) return;
        FillRectangleCore(rect, color);
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (IsCulled(rect)) return;
        DrawRoundedRectangleCore(rect, radiusX, radiusY, color, thickness);
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (IsCulled(rect)) return;
        FillRoundedRectangleCore(rect, radiusX, radiusY, color);
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        if (IsCulled(bounds)) return;
        DrawEllipseCore(bounds, color, thickness);
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        if (IsCulled(bounds)) return;
        FillEllipseCore(bounds, color);
    }

    protected abstract void DrawLineCore(Point start, Point end, Color color, double thickness = 1);

    protected abstract void DrawRectangleCore(Rect rect, Color color, double thickness, bool strokeInset);

    protected abstract void FillRectangleCore(Rect rect, Color color);

    protected abstract void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1);

    protected abstract void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color);

    protected abstract void DrawEllipseCore(Rect bounds, Color color, double thickness = 1);

    protected abstract void FillEllipseCore(Rect bounds, Color color);

    #endregion

    #region Abstract — non-culled (pass-through)

    public abstract double DpiScale { get; }

    public virtual bool EnableAlphaTextHint { get; set; }

    public abstract void Clear(Color color);

    public abstract void DrawPath(PathGeometry path, Color color, double thickness = 1);

    public abstract void FillPath(PathGeometry path, Color color);

    // --- Core text API: layout + draw separation ---

    public TextResourceTracker? TextTracker { get; set; }

    public virtual TextFormat CreateTextFormat(IFont font,
        TextAlignment horizontalAlignment, TextAlignment verticalAlignment,
        TextWrapping wrapping, TextTrimming trimming)
    {
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Wrapping = wrapping,
            Trimming = trimming
        };
        TextTracker?.TrackFormat(format);
        return format;
    }

    public abstract TextLayout CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints);

    public abstract void DrawTextLayout(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color);

    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        if (IsCulled(bounds)) return;

        // Cap-height centering: shift text so the cap-height midpoint aligns with
        // bounds center instead of the line-height midpoint.
        if (verticalAlignment == TextAlignment.Center)
        {
            double lineHeight = font.Size + font.InternalLeading;
            double leadingTrim = Math.Max(0, lineHeight / 2.0 - font.Descent - font.CapHeight / 2.0);
            if (leadingTrim > 0)
            {
                bounds = new Rect(bounds.X, bounds.Y - leadingTrim, bounds.Width, bounds.Height);
            }
        }

        DrawTextCore(text, bounds, font, color, horizontalAlignment, verticalAlignment, wrapping, trimming);
    }

    protected abstract void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None);

    public abstract Size MeasureText(ReadOnlySpan<char> text, IFont font);

    public abstract Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth);

    public abstract ImageScaleQuality ImageScaleQuality { get; set; }

    public abstract void DrawImage(IImage image, Point location);

    public void DrawImage(IImage image, Rect destRect)
    {
        if (IsCulled(destRect)) return;
        DrawImageCore(image, destRect);
    }

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
    {
        if (IsCulled(destRect)) return;
        DrawImageCore(image, destRect, sourceRect);
    }

    protected abstract void DrawImageCore(IImage image, Rect destRect);

    protected abstract void DrawImageCore(IImage image, Rect destRect, Rect sourceRect);

    public virtual void Dispose()
    { }

    #endregion

    #region Optional capabilities

    public virtual float GlobalAlpha { get => 1f; set { } }

    public virtual bool TextPixelSnap { get => true; set { } }

    #endregion

    #region FillPath with FillRule

    public virtual void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        FillPath(path, color);
    }

    #endregion

    #region IPen / IBrush overloads

    public virtual void DrawLine(Point start, Point end, IPen pen)
    {
        if (pen.Brush is ISolidColorBrush s) DrawLineCore(start, end, s.Color, pen.Thickness);
    }

    public virtual void DrawRectangle(Rect rect, IPen pen)
    {
        if (IsCulled(rect)) return;
        if (pen.Brush is ISolidColorBrush s) DrawRectangleCore(rect, s.Color, pen.Thickness, false);
    }

    public virtual void FillRectangle(Rect rect, IBrush brush)
    {
        if (IsCulled(rect)) return;
        if (brush is ISolidColorBrush s) FillRectangleCore(rect, s.Color);
        else if (brush is IGradientBrush g) FillRectangleCore(rect, g.GetRepresentativeColor());
    }

    public virtual void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
    {
        if (IsCulled(rect)) return;
        if (pen.Brush is ISolidColorBrush s) DrawRoundedRectangleCore(rect, radiusX, radiusY, s.Color, pen.Thickness);
    }

    public virtual void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (IsCulled(rect)) return;
        if (brush is ISolidColorBrush s) FillRoundedRectangleCore(rect, radiusX, radiusY, s.Color);
        else if (brush is IGradientBrush g) FillRoundedRectangleCore(rect, radiusX, radiusY, g.GetRepresentativeColor());
    }

    public virtual void DrawEllipse(Rect bounds, IPen pen)
    {
        if (IsCulled(bounds)) return;
        if (pen.Brush is ISolidColorBrush s) DrawEllipseCore(bounds, s.Color, pen.Thickness);
    }

    public virtual void FillEllipse(Rect bounds, IBrush brush)
    {
        if (IsCulled(bounds)) return;
        if (brush is ISolidColorBrush s) FillEllipseCore(bounds, s.Color);
        else if (brush is IGradientBrush g) FillEllipseCore(bounds, g.GetRepresentativeColor());
    }

    public virtual void DrawPath(PathGeometry path, IPen pen)
    {
        if (pen.Brush is ISolidColorBrush s) DrawPath(path, s.Color, pen.Thickness);
    }

    public virtual void FillPath(PathGeometry path, IBrush brush)
    {
        if (brush is ISolidColorBrush s) FillPath(path, s.Color, path.FillRule);
        else if (brush is IGradientBrush g) FillPath(path, g.GetRepresentativeColor(), path.FillRule);
    }

    public virtual void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (brush is ISolidColorBrush s) FillPath(path, s.Color, fillRule);
        else if (brush is IGradientBrush g) FillPath(path, g.GetRepresentativeColor(), fillRule);
    }

    #endregion

    #region DrawBoxShadow

    public virtual void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius,
        Color shadowColor, double offsetX = 0, double offsetY = 0)
    {
        if (blurRadius <= 0 || shadowColor.A == 0) return;

        // Early cull on the full shadow extent
        double br = blurRadius * 0.5;
        var shadowExtent = new Rect(
            bounds.X + offsetX - br,
            bounds.Y + offsetY - br,
            bounds.Width + br * 2,
            bounds.Height + br * 2);
        if (IsCulled(shadowExtent)) return;

        double sx = bounds.X + offsetX;
        double sy = bounds.Y + offsetY;
        double sw = bounds.Width;
        double sh = bounds.Height;
        double cr = Math.Min(Math.Max(cornerRadius, 0), Math.Min(sw, sh) * 0.5);

        // NanoVG-compatible: transition is centered on box edge,
        // so visible shadow extends feather/2 outward with 50% intensity at the edge.
        byte edgeAlpha = (byte)(shadowColor.A / 2);
        var edgeColor = Color.FromArgb(edgeAlpha, shadowColor.R, shadowColor.G, shadowColor.B);
        var transparent = Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B);

        GradientStop[] fadeOut = [new(0, edgeColor), new(1, transparent)];
        GradientStop[] fadeIn = [new(0, transparent), new(1, edgeColor)];

        double cornerSize = cr + br;
        double innerStop = cr > 0 ? cr / cornerSize : 0;
        GradientStop[] cornerStops = innerStop > 0
            ? [new(0, shadowColor), new(innerStop, edgeColor), new(1, transparent)]
            : [new(0, edgeColor), new(1, transparent)];

        double edgeW = sw - 2 * cr;
        double edgeH = sh - 2 * cr;

        // Draw as 3 non-overlapping pieces to prevent double-blending with semi-transparent colors.
        if (edgeH > 0)
            FillRectangle(new Rect(sx, sy + cr, sw, edgeH), shadowColor);
        if (edgeW > 0 && cr > 0)
        {
            FillRectangle(new Rect(sx + cr, sy, edgeW, cr), shadowColor);
            FillRectangle(new Rect(sx + cr, sy + sh - cr, edgeW, cr), shadowColor);
        }
        if (edgeW > 0)
        {
            // Top
            FillRectangle(new Rect(sx + cr, sy - br, edgeW, br),
                new LinearGradientBrush(new Point(0, sy - br), new Point(0, sy),
                    fadeIn, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));
            // Bottom
            FillRectangle(new Rect(sx + cr, sy + sh, edgeW, br),
                new LinearGradientBrush(new Point(0, sy + sh), new Point(0, sy + sh + br),
                    fadeOut, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));
        }

        if (edgeH > 0)
        {
            // Left
            FillRectangle(new Rect(sx - br, sy + cr, br, edgeH),
                new LinearGradientBrush(new Point(sx - br, 0), new Point(sx, 0),
                    fadeIn, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));
            // Right
            FillRectangle(new Rect(sx + sw, sy + cr, br, edgeH),
                new LinearGradientBrush(new Point(sx + sw, 0), new Point(sx + sw + br, 0),
                    fadeOut, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));
        }
        double radius = cornerSize;

        // Top-left
        var tlCenter = new Point(sx + cr, sy + cr);
        FillRectangle(new Rect(sx - br, sy - br, cornerSize, cornerSize),
            new RadialGradientBrush(tlCenter, tlCenter, radius, radius,
                cornerStops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));

        // Top-right
        var trCenter = new Point(sx + sw - cr, sy + cr);
        FillRectangle(new Rect(sx + sw - cr, sy - br, cornerSize, cornerSize),
            new RadialGradientBrush(trCenter, trCenter, radius, radius,
                cornerStops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));

        // Bottom-left
        var blCenter = new Point(sx + cr, sy + sh - cr);
        FillRectangle(new Rect(sx - br, sy + sh - cr, cornerSize, cornerSize),
            new RadialGradientBrush(blCenter, blCenter, radius, radius,
                cornerStops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));

        // Bottom-right
        var brCenter = new Point(sx + sw - cr, sy + sh - cr);
        FillRectangle(new Rect(sx + sw - cr, sy + sh - cr, cornerSize, cornerSize),
            new RadialGradientBrush(brCenter, brCenter, radius, radius,
                cornerStops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null));
    }

    #endregion

    #region Stroke Inset

    /// <summary>
    /// Draws a rounded rectangle with the stroke inset within <paramref name="rect"/>.
    /// When <paramref name="strokeInset"/> is <c>true</c>, the rect is deflated by half the
    /// quantized thickness so that the stroke outer edge aligns with <paramref name="rect"/>.
    /// </summary>
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset)
    {
        if (!strokeInset)
        {
            DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness);
            return;
        }

        if (IsCulled(rect)) return;

        var half = QuantizeHalfStroke(thickness, DpiScale);

        DrawRoundedRectangleCore(
            rect.Deflate(new Thickness(half)),
            Math.Max(0, radiusX - half),
            Math.Max(0, radiusY - half),
            color, thickness);
    }

    /// <summary>
    /// Draws a rectangle with the stroke inset within <paramref name="rect"/>.
    /// </summary>
    public void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset)
    {
        if (!strokeInset)
        {
            DrawRectangle(rect, color, thickness);
            return;
        }

        if (IsCulled(rect)) return;
        DrawRectangleCore(rect, color, thickness, true);
    }

    /// <summary>
    /// Draws an ellipse with the stroke inset within <paramref name="bounds"/>.
    /// </summary>
    public void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset)
    {
        if (!strokeInset)
        {
            DrawEllipse(bounds, color, thickness);
            return;
        }

        if (IsCulled(bounds)) return;
        var half = QuantizeHalfStroke(thickness, DpiScale);
        var full = half * 2;
        DrawEllipseCore(
            new Rect(bounds.X + half, bounds.Y + half,
                     Math.Max(0, bounds.Width - full),
                     Math.Max(0, bounds.Height - full)),
            color, thickness);
    }

    #endregion

    #region Clip Inset

    /// <summary>
    /// Sets a rounded-rectangle clipping region adjusted for a border's inner contour.
    /// The radius is reduced by the quantized full stroke width so that the clip matches
    /// the inner edge of a stroke rendered with <paramref name="borderThickness"/>.
    /// </summary>
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness)
    {
        if (borderThickness <= 0)
        {
            SetClipRoundedRect(rect, radiusX, radiusY);
            return;
        }
        var half = QuantizeHalfStroke(borderThickness, DpiScale);
        var full = half * 2;
        SetClipRoundedRect(rect, Math.Max(0, radiusX - full), Math.Max(0, radiusY - full));
    }

    #endregion

    #region Line Pixel Snap

    /// <summary>
    /// Draws a pixel-snapped line. For axis-aligned lines with odd device-pixel widths,
    /// the position is offset by half a device pixel so stroke edges land on pixel boundaries.
    /// </summary>
    public virtual void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap)
    {
        if (pixelSnap && thickness > 0)
        {
            SnapLinePosition(DpiScale, thickness, ref start, ref end);
        }

        DrawLine(start, end, color, thickness);
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Quantizes stroke thickness to integer device pixels and returns half.
    /// Ensures the inset deflation matches the actual rendered stroke width.
    /// </summary>
    protected static double QuantizeHalfStroke(double thickness, double dpiScale)
    {
        if (thickness <= 0 || dpiScale <= 0)
        {
            return 0;
        }

        double snappedPx = Math.Max(1, Math.Round(thickness * dpiScale));
        return snappedPx * 0.5 / dpiScale;
    }

    /// <summary>
    /// Snaps an axis-aligned line position to device pixel boundaries.
    /// First rounds the position to the nearest integer pixel, then adds
    /// a half-pixel offset for odd-width strokes.
    /// </summary>
    protected static void SnapLinePosition(double scale, double thickness, ref Point start, ref Point end)
    {
        if (scale <= 0)
        {
            return;
        }

        double snappedDevPx = Math.Max(1, Math.Round(thickness * scale));
        double halfSnap = ((int)snappedDevPx & 1) != 0 ? 0.5 / scale : 0;

        if (Math.Abs(start.Y - end.Y) < 0.001) // horizontal
        {
            double y = Math.Round(start.Y * scale) / scale + halfSnap;
            start = new Point(start.X, y);
            end = new Point(end.X, y);
        }
        else if (Math.Abs(start.X - end.X) < 0.001) // vertical
        {
            double x = Math.Round(start.X * scale) / scale + halfSnap;
            start = new Point(x, start.Y);
            end = new Point(x, end.Y);
        }
    }

    #endregion
}
