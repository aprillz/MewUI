using System.Runtime.InteropServices;

using Aprillz.MewUI.Core;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed class OpenGLGraphicsContext : IGraphicsContext
{
    private readonly nint _hwnd;
    private readonly nint _hdc;
    private readonly IOpenGLWindowResources _resources;
    private readonly Stack<SavedState> _stateStack = new();
    private double _translateX;
    private double _translateY;
    private ClipRectPx? _clipPx;
    private int _viewportWidthPx;
    private int _viewportHeightPx;
    private bool _disposed;

    public double DpiScale { get; }

    private readonly struct SavedState
    {
        public required double TranslateXDip { get; init; }
        public required double TranslateYDip { get; init; }
        public required ClipRectPx? ClipPx { get; init; }
    }

    private readonly struct ClipRectPx
    {
        public required int X { get; init; }
        public required int Y { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
    }

    public OpenGLGraphicsContext(nint hwnd, nint hdc, double dpiScale, IOpenGLWindowResources resources)
    {
        _hwnd = hwnd;
        _hdc = hdc;
        _resources = resources;
        DpiScale = dpiScale;

        _resources.MakeCurrent(_hdc);

        int w;
        int h;
        if (OperatingSystem.IsWindows())
        {
            User32.GetClientRect(_hwnd, out var client);
            w = Math.Max(1, client.Width);
            h = Math.Max(1, client.Height);
        }
        else
        {
            // Linux/X11: _hdc is Display*, _hwnd is Window.
            if (X11.XGetWindowAttributes(_hdc, _hwnd, out var attrs) != 0)
            {
                w = Math.Max(1, attrs.width);
                h = Math.Max(1, attrs.height);
            }
            else
            {
                w = 1;
                h = 1;
            }
        }

        _viewportWidthPx = w;
        _viewportHeightPx = h;

        GL.Viewport(0, 0, w, h);

        GL.MatrixMode(GL.GL_PROJECTION);
        GL.LoadIdentity();
        GL.Ortho(0, w, h, 0, -1, 1);

        GL.MatrixMode(GL.GL_MODELVIEW);
        GL.LoadIdentity();

        GL.Disable(GL.GL_SCISSOR_TEST);
        GL.Enable(GL.GL_BLEND);
        GL.BlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.GL_TEXTURE_2D);
        GL.Enable(GL.GL_LINE_SMOOTH);
        GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (SmokeCapture.TryConsume(out var capturePath) && !string.IsNullOrEmpty(capturePath))
        {
            TryCaptureBackbuffer(capturePath);
        }

        _resources.SwapBuffers(_hdc, _hwnd);
        _resources.ReleaseCurrent();
    }

    private void TryCaptureBackbuffer(string path)
    {
        try
        {
            int w = Math.Max(1, _viewportWidthPx);
            int h = Math.Max(1, _viewportHeightPx);
            var rgba = new byte[w * h * 4];
            unsafe
            {
                fixed (byte* p = rgba)
                {
                    GL.ReadPixels(0, 0, w, h, GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)p);
                }
            }

            // Flip vertically (OpenGL origin is bottom-left).
            int stride = w * 4;
            var flipped = new byte[rgba.Length];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(rgba, (h - 1 - y) * stride, flipped, y * stride, stride);

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            WritePpm(path, w, h, flipped);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SmokeCapture failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static void WritePpm(string path, int width, int height, byte[] rgba)
    {
        // Binary PPM (P6). Ignore alpha.
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));

        for (int i = 0; i < rgba.Length; i += 4)
        {
            bw.Write(rgba[i + 0]); // R
            bw.Write(rgba[i + 1]); // G
            bw.Write(rgba[i + 2]); // B
        }
    }

    #region State Management

    public void Save()
    {
        _stateStack.Push(new SavedState
        {
            TranslateXDip = _translateX,
            TranslateYDip = _translateY,
            ClipPx = _clipPx
        });
    }

    public void Restore()
    {
        if (_stateStack.Count == 0)
            return;

        var state = _stateStack.Pop();
        _translateX = state.TranslateXDip;
        _translateY = state.TranslateYDip;
        _clipPx = state.ClipPx;
        ApplyClip();
    }

    public void SetClip(Rect rect)
    {
        var r = ToDeviceRect(rect);
        var next = new ClipRectPx
        {
            X = r.left,
            Y = r.top,
            Width = Math.Max(0, r.Width),
            Height = Math.Max(0, r.Height),
        };

        _clipPx = _clipPx.HasValue ? Intersect(_clipPx.Value, next) : next;
        ApplyClip();
    }

    public void Translate(double dx, double dy)
    {
        _translateX += dx;
        _translateY += dy;
    }

    private static ClipRectPx Intersect(ClipRectPx a, ClipRectPx b)
    {
        int left = Math.Max(a.X, b.X);
        int top = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.X + a.Width, b.X + b.Width);
        int bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return new ClipRectPx
        {
            X = left,
            Y = top,
            Width = Math.Max(0, right - left),
            Height = Math.Max(0, bottom - top),
        };
    }

    private void ApplyClip()
    {
        if (!_clipPx.HasValue)
        {
            GL.Disable(GL.GL_SCISSOR_TEST);
            return;
        }

        var c = _clipPx.Value;

        // OpenGL scissor uses bottom-left origin.
        int x = c.X;
        int y = _viewportHeightPx - (c.Y + c.Height);
        int w = Math.Max(0, c.Width);
        int h = Math.Max(0, c.Height);

        GL.Enable(GL.GL_SCISSOR_TEST);
        GL.Scissor(x, y, w, h);
    }

    #endregion

    #region Drawing Primitives

    public void Clear(Color color)
    {
        GL.Disable(GL.GL_SCISSOR_TEST);
        GL.ClearColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT);
        ApplyClip();
    }

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        int thicknessPx = Math.Max(1, LayoutRounding.RoundToPixelInt(thickness, DpiScale));
        GL.LineWidth(thicknessPx);

        float x0 = ToDeviceCoord(start.X + _translateX, thicknessPx);
        float y0 = ToDeviceCoord(start.Y + _translateY, thicknessPx);
        float x1 = ToDeviceCoord(end.X + _translateX, thicknessPx);
        float y1 = ToDeviceCoord(end.Y + _translateY, thicknessPx);

        GL.Begin(GL.GL_LINE_STRIP);
        GL.Vertex2f(x0, y0);
        GL.Vertex2f(x1, y1);
        GL.End();
        GL.Enable(GL.GL_TEXTURE_2D);
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        int thicknessPx = Math.Max(1, LayoutRounding.RoundToPixelInt(thickness, DpiScale));
        GL.LineWidth(thicknessPx);

        var r = ToDeviceRect(rect);
        float offset = (thicknessPx & 1) == 1 ? 0.5f : 0f;
        float x0 = r.left + offset;
        float y0 = r.top + offset;
        float x1 = (r.right) - offset;
        float y1 = (r.bottom) - offset;

        GL.Begin(GL.GL_LINE_LOOP);
        GL.Vertex2f(x0, y0);
        GL.Vertex2f(x1, y0);
        GL.Vertex2f(x1, y1);
        GL.Vertex2f(x0, y1);
        GL.End();

        GL.Enable(GL.GL_TEXTURE_2D);
    }

    public void FillRectangle(Rect rect, Color color)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        var r = ToDeviceRect(rect);
        float x0 = r.left;
        float y0 = r.top;
        float x1 = r.right;
        float y1 = r.bottom;

        GL.Begin(GL.GL_QUADS);
        GL.Vertex2f(x0, y0);
        GL.Vertex2f(x1, y0);
        GL.Vertex2f(x1, y1);
        GL.Vertex2f(x0, y1);
        GL.End();
        GL.Enable(GL.GL_TEXTURE_2D);
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        int thicknessPx = Math.Max(1, LayoutRounding.RoundToPixelInt(thickness, DpiScale));
        GL.LineWidth(thicknessPx);

        var pts = GetRoundedRectPointsPx(rect, radiusX, radiusY, thicknessPx, includeClose: true);
        GL.Begin(GL.GL_LINE_STRIP);
        for (int i = 0; i < pts.Length; i += 2)
            GL.Vertex2f(pts[i], pts[i + 1]);
        GL.End();

        GL.Enable(GL.GL_TEXTURE_2D);
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        var pts = GetRoundedRectPointsPx(rect, radiusX, radiusY, thicknessPx: 0, includeClose: true);

        var r = ToDeviceRect(rect);
        float cx = (r.left + r.right) / 2f;
        float cy = (r.top + r.bottom) / 2f;

        GL.Begin(GL.GL_TRIANGLE_FAN);
        GL.Vertex2f(cx, cy);
        for (int i = 0; i < pts.Length; i += 2)
            GL.Vertex2f(pts[i], pts[i + 1]);
        GL.End();

        GL.Enable(GL.GL_TEXTURE_2D);
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        int thicknessPx = Math.Max(1, LayoutRounding.RoundToPixelInt(thickness, DpiScale));
        GL.LineWidth(thicknessPx);

        var pts = GetEllipsePointsPx(bounds, thicknessPx, includeClose: true);
        GL.Begin(GL.GL_LINE_STRIP);
        for (int i = 0; i < pts.Length; i += 2)
            GL.Vertex2f(pts[i], pts[i + 1]);
        GL.End();

        GL.Enable(GL.GL_TEXTURE_2D);
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        GL.Disable(GL.GL_TEXTURE_2D);
        GL.Color4ub(color.R, color.G, color.B, color.A);

        var r = ToDeviceRect(bounds);
        float cx = (r.left + r.right) / 2f;
        float cy = (r.top + r.bottom) / 2f;

        var pts = GetEllipsePointsPx(bounds, thicknessPx: 0, includeClose: true);
        GL.Begin(GL.GL_TRIANGLE_FAN);
        GL.Vertex2f(cx, cy);
        for (int i = 0; i < pts.Length; i += 2)
            GL.Vertex2f(pts[i], pts[i + 1]);
        GL.End();

        GL.Enable(GL.GL_TEXTURE_2D);
    }

    #endregion

    #region Text Rendering

    public void DrawText(string text, Point location, IFont font, Color color)
    {
        if (string.IsNullOrEmpty(text))
            return;

        DrawText(text, new Rect(location.X, location.Y, 0, 0), font, color, TextAlignment.Left, TextAlignment.Top,
            text.AsSpan().IndexOfAny('\r', '\n') >= 0 ? TextWrapping.Wrap : TextWrapping.NoWrap);
    }

    public void DrawText(string text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var boundsPx = ToDeviceRect(bounds);
        int widthPx = boundsPx.Width;
        int heightPx = boundsPx.Height;

        // Point-based draw uses measured size.
        if (widthPx <= 0 || heightPx <= 0)
        {
            if (OperatingSystem.IsLinux() && font is FreeTypeFont ftForMeasure)
            {
                var px = FreeTypeText.Measure(text, ftForMeasure);
                widthPx = Math.Max(1, (int)Math.Ceiling(px.Width));
                heightPx = Math.Max(1, (int)Math.Ceiling(px.Height));
            }
            else
            {
                var measured = MeasureText(text, font);
                widthPx = Math.Max(1, LayoutRounding.RoundToPixelInt(measured.Width, DpiScale));
                heightPx = Math.Max(1, LayoutRounding.RoundToPixelInt(measured.Height, DpiScale));
            }
            boundsPx = new RECT(boundsPx.left, boundsPx.top, boundsPx.left + widthPx, boundsPx.top + heightPx);
        }

        // Guard against "unbounded" widths (e.g. 1_000_000) used by some controls for left-aligned text.
        // Creating extremely large bitmaps/textures is slow and can produce undefined results on some drivers.
        widthPx = ClampTextRasterExtent(widthPx, boundsPx, axis: 0);
        heightPx = ClampTextRasterExtent(heightPx, boundsPx, axis: 1);
        boundsPx = new RECT(boundsPx.left, boundsPx.top, boundsPx.left + widthPx, boundsPx.top + heightPx);

        if (OperatingSystem.IsWindows() && font is GdiFont gdiFont)
        {
            var key = new OpenGLTextCacheKey(text, gdiFont.Handle, FontId: string.Empty, FontSizePx: 0, color.ToArgb(), widthPx, heightPx,
                (int)horizontalAlignment, (int)verticalAlignment, (int)wrapping);

            var texture = _resources.TextCache.GetOrCreateTexture(_resources.SupportsBgra, _hdc, key,
                () => OpenGLTextRasterizer.Rasterize(_hdc, gdiFont, text, widthPx, heightPx, color, horizontalAlignment, verticalAlignment, wrapping));

            DrawTexturedQuad(boundsPx, texture);
            return;
        }

        if (OperatingSystem.IsLinux() && font is FreeTypeFont ftFont)
        {
            var key = new OpenGLTextCacheKey(text, 0, ftFont.FontPath, ftFont.PixelHeight, color.ToArgb(), widthPx, heightPx,
                (int)horizontalAlignment, (int)verticalAlignment, (int)wrapping);

            var texture = _resources.TextCache.GetOrCreateTexture(_resources.SupportsBgra, _hdc, key,
                () => FreeTypeText.Rasterize(text, ftFont, widthPx, heightPx, color, horizontalAlignment, verticalAlignment, wrapping));

            DrawTexturedQuad(boundsPx, texture);
        }
    }

    private int ClampTextRasterExtent(int extentPx, RECT boundsPx, int axis)
    {
        // axis: 0 = width, 1 = height
        int viewport = axis == 0 ? _viewportWidthPx : _viewportHeightPx;
        if (extentPx <= 0)
            return 1;

        // Hard guard to avoid pathological allocations.
        int hardMax = Math.Max(256, viewport * 4);
        if (extentPx <= hardMax)
            return extentPx;

        // Prefer current clip size when available (TextBox uses clip to constrain content).
        if (_clipPx.HasValue)
            return Math.Clamp(axis == 0 ? _clipPx.Value.Width : _clipPx.Value.Height, 1, hardMax);

        // Otherwise clamp to remaining viewport space.
        int remaining = axis == 0 ? Math.Max(1, viewport - boundsPx.left) : Math.Max(1, viewport - boundsPx.top);
        return Math.Clamp(remaining, 1, hardMax);
    }

    public Size MeasureText(string text, IFont font)
    {
        if (OperatingSystem.IsWindows())
        {
            using var measure = new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));
            return measure.MeasureText(text, font);
        }

        if (OperatingSystem.IsLinux() && font is FreeTypeFont ftFont)
        {
            var px = FreeTypeText.Measure(text, ftFont);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        using var measureFallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96));
        return measureFallback.MeasureText(text, font);
    }

    public Size MeasureText(string text, IFont font, double maxWidth)
    {
        if (OperatingSystem.IsWindows())
        {
            using var measure = new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));
            return measure.MeasureText(text, font, maxWidth);
        }

        if (OperatingSystem.IsLinux() && font is FreeTypeFont ftFont)
        {
            // TODO: wrapping-aware measurement; for now ignore maxWidth.
            var px = FreeTypeText.Measure(text, ftFont);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        using var measureFallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96));
        return measureFallback.MeasureText(text, font, maxWidth);
    }

    private static void DrawTexturedQuad(RECT boundsPx, OpenGLTextureEntry texture)
    {
        GL.Enable(GL.GL_TEXTURE_2D);
        GL.BindTexture(GL.GL_TEXTURE_2D, texture.TextureId);
        GL.Color4ub(255, 255, 255, 255);

        float x = boundsPx.left;
        float y = boundsPx.top;
        float w = texture.WidthPx;
        float h = texture.HeightPx;

        GL.Begin(GL.GL_QUADS);
        GL.TexCoord2f(0, 0); GL.Vertex2f(x, y);
        GL.TexCoord2f(1, 0); GL.Vertex2f(x + w, y);
        GL.TexCoord2f(1, 1); GL.Vertex2f(x + w, y + h);
        GL.TexCoord2f(0, 1); GL.Vertex2f(x, y + h);
        GL.End();
    }

    #endregion

    #region Image Rendering

    public void DrawImage(IImage image, Point location) => throw new NotImplementedException();

    public void DrawImage(IImage image, Rect destRect) => throw new NotImplementedException();

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) => throw new NotImplementedException();

    #endregion

    #region Helpers

    private float ToDeviceCoord(double valueDipWithTranslate, int thicknessPx)
    {
        int px = LayoutRounding.RoundToPixelInt(valueDipWithTranslate, DpiScale);
        float offset = (thicknessPx & 1) == 1 ? 0.5f : 0f;
        return px + offset;
    }

    private RECT ToDeviceRect(Rect rect)
    {
        int left = LayoutRounding.RoundToPixelInt(rect.X + _translateX, DpiScale);
        int top = LayoutRounding.RoundToPixelInt(rect.Y + _translateY, DpiScale);
        int width = LayoutRounding.RoundToPixelInt(rect.Width, DpiScale);
        int height = LayoutRounding.RoundToPixelInt(rect.Height, DpiScale);

        int right = left + Math.Max(0, width);
        int bottom = top + Math.Max(0, height);
        return new RECT(left, top, right, bottom);
    }

    private float[] GetEllipsePointsPx(Rect boundsDip, int thicknessPx, bool includeClose)
    {
        var r = ToDeviceRect(boundsDip);
        float cx = (r.left + r.right) / 2f;
        float cy = (r.top + r.bottom) / 2f;
        float rx = Math.Max(0.5f, (r.right - r.left) / 2f);
        float ry = Math.Max(0.5f, (r.bottom - r.top) / 2f);

        int segments = Math.Clamp((int)Math.Ceiling(Math.Max(rx, ry) * 0.7f), 32, 160);
        int count = includeClose ? segments + 1 : segments;
        var pts = new float[count * 2];

        float offset = (thicknessPx & 1) == 1 ? 0.5f : 0f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)(i % segments) / segments * (float)(Math.PI * 2);
            float x = cx + (float)Math.Cos(t) * rx + offset;
            float y = cy + (float)Math.Sin(t) * ry + offset;
            pts[i * 2] = x;
            pts[i * 2 + 1] = y;
        }
        return pts;
    }

    private float[] GetRoundedRectPointsPx(Rect rectDip, double radiusX, double radiusY, int thicknessPx, bool includeClose)
    {
        var r = ToDeviceRect(rectDip);
        float left = r.left;
        float top = r.top;
        float right = r.right;
        float bottom = r.bottom;

        float rx = (float)Math.Max(0, radiusX * DpiScale);
        float ry = (float)Math.Max(0, radiusY * DpiScale);

        float w = Math.Max(0, right - left);
        float h = Math.Max(0, bottom - top);
        rx = Math.Min(rx, w / 2f);
        ry = Math.Min(ry, h / 2f);

        int arcSegments = Math.Clamp((int)Math.Ceiling(Math.Max(rx, ry) * 0.7f), 12, 64);

        float offset = (thicknessPx & 1) == 1 ? 0.5f : 0f;
        left += offset;
        top += offset;
        right -= offset;
        bottom -= offset;

        // Build perimeter clockwise, starting at top-left (after arc).
        var pts = new List<float>((arcSegments * 4 + 1) * 2);

        // Helper for quarter arc
        void AddArc(float cx, float cy, float ax, float ay, float startRad, float endRad)
        {
            for (int i = 0; i <= arcSegments; i++)
            {
                float t = i / (float)arcSegments;
                float a = startRad + (endRad - startRad) * t;
                pts.Add(cx + (float)Math.Cos(a) * ax);
                pts.Add(cy + (float)Math.Sin(a) * ay);
            }
        }

        // Angles assume y grows downward (screen space).
        // TL: 180..270, TR: 270..360, BR: 0..90, BL: 90..180
        AddArc(left + rx, top + ry, rx, ry, (float)Math.PI, (float)(Math.PI * 1.5));
        AddArc(right - rx, top + ry, rx, ry, (float)(Math.PI * 1.5), (float)(Math.PI * 2));
        AddArc(right - rx, bottom - ry, rx, ry, 0, (float)(Math.PI * 0.5));
        AddArc(left + rx, bottom - ry, rx, ry, (float)(Math.PI * 0.5), (float)Math.PI);

        if (includeClose && pts.Count >= 2)
        {
            pts.Add(pts[0]);
            pts.Add(pts[1]);
        }

        return pts.ToArray();
    }

    #endregion
}
