using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Infrastructure;

internal abstract class NoOpGraphicsContext : IGraphicsContext
{
    private ITextRenderContext? _text;

    public ITextRenderContext Text => _text ??= new NoOpTextRenderContext(this);
    public virtual double DpiScale => 1;
    public virtual bool EnableAlphaTextHint { get; set; }
    public virtual ImageScaleQuality ImageScaleQuality { get; set; }
    public virtual float GlobalAlpha { get; set; } = 1;
    public virtual bool TextPixelSnap { get; set; } = true;
    public virtual void BeginFrame(IRenderTarget target) { }
    public virtual void EndFrame() { }
    public virtual void Save() { }
    public virtual void Restore() { }
    public virtual void SetClip(Rect rect) { }
    public virtual void BeginOpaqueBackdrop() { }
    public virtual void EndOpaqueBackdrop() { }
    public virtual void SetClipRoundedRect(Rect rect, double radiusX, double radiusY) { }
    public virtual void SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness) { }
    public virtual void SetClipPath(PathGeometry path) { }
    public virtual void ResetClip() { }
    public virtual void IntersectClip(Rect rect) { }
    public virtual void Translate(double dx, double dy) { }
    public virtual void Rotate(double angleRadians) { }
    public virtual void Scale(double sx, double sy) { }
    public virtual void SetTransform(Matrix3x2 matrix) { }
    public virtual Matrix3x2 GetTransform() => Matrix3x2.Identity;
    public virtual void ResetTransform() { }
    public virtual void BeginOpacity(double opacity) { }
    public virtual void EndOpacity() { }
    public virtual void Clear(Color color) { }
    public virtual void DrawLine(Point start, Point end, Color color, double thickness = 1) { }
    public virtual void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap) { }
    public virtual void DrawLine(Point start, Point end, Pen pen) { }
    public virtual void DrawRectangle(Rect rect, Color color, double thickness = 1) { }
    public virtual void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset) { }
    public virtual void DrawRectangle(Rect rect, Pen pen) { }
    public virtual void FillRectangle(Rect rect, Color color) { }
    public virtual void FillRectangle(Rect rect, Brush brush) { }
    public virtual void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1) { }
    public virtual void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset) { }
    public virtual void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Pen pen) { }
    public virtual void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) { }
    public virtual void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Brush brush) { }
    public virtual void DrawEllipse(Rect bounds, Color color, double thickness = 1) { }
    public virtual void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset) { }
    public virtual void DrawEllipse(Rect bounds, Pen pen) { }
    public virtual void FillEllipse(Rect bounds, Color color) { }
    public virtual void FillEllipse(Rect bounds, Brush brush) { }
    public virtual void DrawPath(PathGeometry path, Color color, double thickness = 1) { }
    public virtual void DrawPath(PathGeometry path, Pen pen) { }
    public virtual void FillPath(PathGeometry path, Color color) { }
    public virtual void FillPath(PathGeometry path, Color color, FillRule fillRule) { }
    public virtual void FillPath(PathGeometry path, Brush brush) { }
    public virtual void FillPath(PathGeometry path, Brush brush, FillRule fillRule) { }
    public virtual void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius, Color shadowColor, double offsetX = 0, double offsetY = 0) { }
    public virtual void DrawImage(IImage image, Point location) { }
    public virtual void DrawImage(IImage image, Rect destRect) { }
    public virtual void DrawImage(IImage image, Rect destRect, Rect sourceRect) { }
    public virtual void Dispose() { }

    private sealed class NoOpTextRenderContext(IGraphicsContext graphics) : ITextRenderContext
    {
        public IGraphicsContext Graphics => graphics;
        public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) { }
        public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options) { }
        public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options) { }
    }
}
