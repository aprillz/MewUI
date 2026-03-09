using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Base class for measurement-only <see cref="IGraphicsContext"/> implementations.
/// All rendering operations are no-ops; subclasses only need to implement
/// <see cref="DpiScale"/>, <see cref="MeasureText(ReadOnlySpan{char}, IFont)"/>,
/// <see cref="MeasureText(ReadOnlySpan{char}, IFont, double)"/>, and optionally <see cref="Dispose"/>.
/// </summary>
public abstract class MeasureGraphicsContextBase : IGraphicsContext
{
    public abstract double DpiScale { get; }

    public virtual ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public abstract Size MeasureText(ReadOnlySpan<char> text, IFont font);

    public abstract Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth);

    public virtual void Dispose() { }

    // All other interface members are no-ops for measurement contexts.

    public void Save() { }
    public void Restore() { }
    public void SetClip(Rect rect) { }
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY) { }
    public void Translate(double dx, double dy) { }
    public void Rotate(double angleRadians) { }
    public void Scale(double sx, double sy) { }
    public void SetTransform(Matrix3x2 matrix) { }
    public Matrix3x2 GetTransform() => Matrix3x2.Identity;
    public void ResetTransform() { }
    public float GlobalAlpha { get; set; } = 1f;
    public bool TextPixelSnap { get; set; } = true;
    public void ResetClip() { }
    public void IntersectClip(Rect rect) { }
    public void Clear(Color color) { }
    public void DrawLine(Point start, Point end, Color color, double thickness = 1) { }
    public void DrawRectangle(Rect rect, Color color, double thickness = 1) { }
    public void FillRectangle(Rect rect, Color color) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1) { }
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) { }
    public void DrawEllipse(Rect bounds, Color color, double thickness = 1) { }
    public void FillEllipse(Rect bounds, Color color) { }
    public void DrawPath(PathGeometry path, Color color, double thickness = 1) { }
    public void FillPath(PathGeometry path, Color color) { }
    public void FillPath(PathGeometry path, Color color, FillRule fillRule) { }
    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None) { }
    public void DrawImage(IImage image, Point location) { }
    public void DrawImage(IImage image, Rect destRect) { }
    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset) { }
    public void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset) { }
    public void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset) { }
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness) { }
    public void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap) { }
    public void DrawLine(Point start, Point end, IPen pen) { }
    public void DrawRectangle(Rect rect, IPen pen) { }
    public void FillRectangle(Rect rect, IBrush brush) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen) { }
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush) { }
    public void DrawEllipse(Rect bounds, IPen pen) { }
    public void FillEllipse(Rect bounds, IBrush brush) { }
    public void DrawPath(PathGeometry path, IPen pen) { }
    public void FillPath(PathGeometry path, IBrush brush) { }
    public void FillPath(PathGeometry path, IBrush brush, FillRule fillRule) { }
    public void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius, Color shadowColor, double offsetX = 0, double offsetY = 0) { }
}
