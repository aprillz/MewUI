using System.Numerics;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// A double-buffered GDI graphics context that renders to an off-screen buffer
/// and blits to the screen on dispose to reduce flickering.
/// </summary>
internal sealed class GdiDoubleBufferedContext : IGraphicsContext
{
    private readonly nint _hwnd;
    private readonly nint _screenDc;
    private readonly BackBuffer _backBuffer;
    private readonly GdiPlusGraphicsContext _context;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    public double DpiScale => _context.DpiScale;

    public ImageScaleQuality ImageScaleQuality
    {
        get => _context.ImageScaleQuality;
        set => _context.ImageScaleQuality = value;
    }

    private sealed class BackBuffer : IDisposable
    {
        private static readonly Dictionary<nint, BackBuffer> Cache = new();

        public static BackBuffer GetOrCreate(nint hwnd, nint screenDc, int width, int height)
        {
            if (Cache.TryGetValue(hwnd, out var existing))
            {
                existing.EnsureSize(screenDc, width, height);
                return existing;
            }

            var buffer = new BackBuffer(hwnd, screenDc, width, height);
            Cache[hwnd] = buffer;
            return buffer;
        }

        public static void Release(nint hwnd)
        {
            if (!Cache.TryGetValue(hwnd, out var buffer))
            {
                return;
            }

            Cache.Remove(hwnd);
            buffer.Dispose();
        }

        private readonly nint _hwnd;
        private nint _bitmap;
        private nint _oldBitmap;
        private nint _bits;

        public nint MemDc { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        private BackBuffer(nint hwnd, nint screenDc, int width, int height)
        {
            _hwnd = hwnd;
            Create(screenDc, width, height);
        }

        private void Create(nint screenDc, int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);

            MemDc = Gdi32.CreateCompatibleDC(screenDc);
            var bmi = BITMAPINFO.Create32bpp(Width, Height);
            _bitmap = Gdi32.CreateDIBSection(screenDc, ref bmi, usage: 0, out _bits, 0, 0);
            _oldBitmap = Gdi32.SelectObject(MemDc, _bitmap);
        }

        private void Destroy()
        {
            if (MemDc == 0)
            {
                return;
            }

            if (_oldBitmap != 0)
            {
                Gdi32.SelectObject(MemDc, _oldBitmap);
            }

            if (_bitmap != 0)
            {
                Gdi32.DeleteObject(_bitmap);
            }

            Gdi32.DeleteDC(MemDc);

            MemDc = 0;
            _bitmap = 0;
            _oldBitmap = 0;
            _bits = 0;
            Width = 0;
            Height = 0;
        }

        public void EnsureSize(nint screenDc, int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (width == Width && height == Height && MemDc != 0 && _bitmap != 0)
            {
                return;
            }

            Destroy();
            Create(screenDc, width, height);
        }

        public void Dispose() => Destroy();
    }

    public static void ReleaseForWindow(nint hwnd) => BackBuffer.Release(hwnd);

    public GdiDoubleBufferedContext(nint hwnd, nint screenDc, double dpiScale, GdiCurveQuality curveQuality, ImageScaleQuality imageScaleQuality)
    {
        _hwnd = hwnd;
        _screenDc = screenDc;

        // Get client area size
        User32.GetClientRect(hwnd, out var clientRect);
        _width = clientRect.Width;
        _height = clientRect.Height;

        if (_width <= 0)
        {
            _width = 1;
        }

        if (_height <= 0)
        {
            _height = 1;
        }

        _backBuffer = BackBuffer.GetOrCreate(hwnd, screenDc, _width, _height);

        // Create the inner context that renders to the memory DC
        _context = new GdiPlusGraphicsContext(hwnd, _backBuffer.MemDc, dpiScale, imageScaleQuality, false);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Blit from back buffer to screen
            Gdi32.BitBlt(_screenDc, 0, 0, _width, _height, _backBuffer.MemDc, 0, 0, 0x00CC0020); // SRCCOPY

            // Clean up per-frame state; the back buffer is cached per-window.
            _context.Dispose();

            _disposed = true;
        }
    }

    // Delegate all methods to the inner context

    // ── State Management ───────────────────────────────────────────────
    public void Save() => _context.Save();
    public void Restore() => _context.Restore();
    public void SetClip(Rect rect) => _context.SetClip(rect);
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY) => _context.SetClipRoundedRect(rect, radiusX, radiusY);
    public void ResetClip() => _context.ResetClip();
    public void Translate(double dx, double dy) => _context.Translate(dx, dy);
    public void Rotate(double angleRadians) => _context.Rotate(angleRadians);
    public void Scale(double sx, double sy) => _context.Scale(sx, sy);
    public void SetTransform(Matrix3x2 matrix) => _context.SetTransform(matrix);
    public Matrix3x2 GetTransform() => _context.GetTransform();
    public void ResetTransform() => _context.ResetTransform();

    // ── Clear ──────────────────────────────────────────────────────────
    public void Clear(Color color) => _context.Clear(color);

    // ── Draw (Color) ───────────────────────────────────────────────────
    public void DrawLine(Point start, Point end, Color color, double thickness = 1) => _context.DrawLine(start, end, color, thickness);
    public void DrawRectangle(Rect rect, Color color, double thickness = 1) => _context.DrawRectangle(rect, color, thickness);
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1) => _context.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness);
    public void DrawEllipse(Rect bounds, Color color, double thickness = 1) => _context.DrawEllipse(bounds, color, thickness);
    public void DrawPath(PathGeometry path, Color color, double thickness = 1) => _context.DrawPath(path, color, thickness);

    // ── Draw (IPen) ────────────────────────────────────────────────────
    public void DrawLine(Point start, Point end, IPen pen) => _context.DrawLine(start, end, pen);
    public void DrawRectangle(Rect rect, IPen pen) => _context.DrawRectangle(rect, pen);
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen) => _context.DrawRoundedRectangle(rect, radiusX, radiusY, pen);
    public void DrawEllipse(Rect bounds, IPen pen) => _context.DrawEllipse(bounds, pen);
    public void DrawPath(PathGeometry path, IPen pen) => _context.DrawPath(path, pen);

    // ── Fill (Color) ───────────────────────────────────────────────────
    public void FillRectangle(Rect rect, Color color) => _context.FillRectangle(rect, color);
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) => _context.FillRoundedRectangle(rect, radiusX, radiusY, color);
    public void FillEllipse(Rect bounds, Color color) => _context.FillEllipse(bounds, color);
    public void FillPath(PathGeometry path, Color color) => _context.FillPath(path, color);
    public void FillPath(PathGeometry path, Color color, FillRule fillRule) => _context.FillPath(path, color, fillRule);

    // ── Fill (IBrush) ──────────────────────────────────────────────────
    public void FillRectangle(Rect rect, IBrush brush) => _context.FillRectangle(rect, brush);
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush) => _context.FillRoundedRectangle(rect, radiusX, radiusY, brush);
    public void FillEllipse(Rect bounds, IBrush brush) => _context.FillEllipse(bounds, brush);
    public void FillPath(PathGeometry path, IBrush brush) => _context.FillPath(path, brush);
    public void FillPath(PathGeometry path, IBrush brush, FillRule fillRule) => _context.FillPath(path, brush, fillRule);

    // ── Text ───────────────────────────────────────────────────────────
    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color) => _context.DrawText(text, location, font, color);
    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color, TextAlignment horizontalAlignment = TextAlignment.Left, TextAlignment verticalAlignment = TextAlignment.Top, TextWrapping wrapping = TextWrapping.NoWrap) => _context.DrawText(text, bounds, font, color, horizontalAlignment, verticalAlignment, wrapping);
    public Size MeasureText(ReadOnlySpan<char> text, IFont font) => _context.MeasureText(text, font);
    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth) => _context.MeasureText(text, font, maxWidth);

    // ── Image ──────────────────────────────────────────────────────────
    public void DrawImage(IImage image, Point location) => _context.DrawImage(image, location);
    public void DrawImage(IImage image, Rect destRect) => _context.DrawImage(image, destRect);
    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) => _context.DrawImage(image, destRect, sourceRect);
}
