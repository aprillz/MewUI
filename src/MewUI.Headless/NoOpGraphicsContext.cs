using System.Numerics;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Headless;

/// <summary>A drawing context that accepts every call and draws nothing; measure-only frames render through it.</summary>
internal sealed class NoOpGraphicsContext : IGraphicsContext
{
    private readonly NoOpTextRenderContext _text;
    private double _dpiScale = 1;

    public NoOpGraphicsContext(double dpiScale)
    {
        _dpiScale = dpiScale;
        _text = new NoOpTextRenderContext(this);
    }

    public ITextRenderContext Text => _text;
    public double DpiScale => _dpiScale;
    public bool EnableAlphaTextHint { get; set; }
    public ImageScaleQuality ImageScaleQuality { get; set; }
    public float GlobalAlpha { get; set; } = 1;
    public bool TextPixelSnap { get; set; } = true;
    public void BeginFrame(IRenderTarget target) => _dpiScale = target.DpiScale;
    public void EndFrame() { }
    public void Save() { }
    public void Restore() { }
    public void SetClip(Rect rect) { }
    public void BeginOpaqueBackdrop() { }
    public void EndOpaqueBackdrop() { }
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY) { }
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY, double borderThickness) { }
    public void SetClipPath(PathGeometry path) { }
    public void ResetClip() { }
    public void IntersectClip(Rect rect) { }
    public void Translate(double dx, double dy) { }
    public void Rotate(double angleRadians) { }
    public void Scale(double sx, double sy) { }
    public void SetTransform(Matrix3x2 matrix) { }
    public Matrix3x2 GetTransform() => Matrix3x2.Identity;
    public void ResetTransform() { }
    public void BeginOpacity(double opacity) { }
    public void EndOpacity() { }
    public void Clear(Color color) { }
    public void DrawLine(Point start, Point end, Color color, double thickness = 1) { }
    public void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap) { }
    public void DrawLine(Point start, Point end, Pen pen) { }
    public void DrawRectangle(Rect rect, Color color, double thickness = 1) { }
    public void DrawRectangle(Rect rect, Color color, double thickness, bool strokeInset) { }
    public void DrawRectangle(Rect rect, Pen pen) { }
    public void FillRectangle(Rect rect, Color color) { }
    public void FillRectangle(Rect rect, Brush brush) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness, bool strokeInset) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Pen pen) { }
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) { }
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Brush brush) { }
    public void DrawEllipse(Rect bounds, Color color, double thickness = 1) { }
    public void DrawEllipse(Rect bounds, Color color, double thickness, bool strokeInset) { }
    public void DrawEllipse(Rect bounds, Pen pen) { }
    public void FillEllipse(Rect bounds, Color color) { }
    public void FillEllipse(Rect bounds, Brush brush) { }
    public void DrawPath(PathGeometry path, Color color, double thickness = 1) { }
    public void DrawPath(PathGeometry path, Pen pen) { }
    public void FillPath(PathGeometry path, Color color) { }
    public void FillPath(PathGeometry path, Color color, FillRule fillRule) { }
    public void FillPath(PathGeometry path, Brush brush) { }
    public void FillPath(PathGeometry path, Brush brush, FillRule fillRule) { }
    public void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius, Color shadowColor, double offsetX = 0, double offsetY = 0) { }
    public void DrawImage(IImage image, Point location) { }
    public void DrawImage(IImage image, Rect destRect) { }
    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) { }
    public void Dispose() { }

    private sealed class NoOpTextRenderContext(IGraphicsContext graphics) : ITextRenderContext
    {
        public IGraphicsContext Graphics => graphics;
        public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) { }
        public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options) { }
        public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options) { }
    }
}

/// <summary>The surface a measure-only window is "drawn" into: it only carries the pixel size and scale a frame is laid out for.</summary>
internal sealed class NoOpRenderSurface : IRenderSurface
{
    public NoOpRenderSurface(int pixelWidth, int pixelHeight, double dpiScale)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiScale { get; }
    public RenderPixelFormat Format => RenderPixelFormat.Bgra8888;
    public SurfaceUsage Usage => SurfaceUsage.Offscreen;
    public SurfaceCapabilities Capabilities => SurfaceCapabilities.Renderable;
    public ulong Version => 0;
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}
