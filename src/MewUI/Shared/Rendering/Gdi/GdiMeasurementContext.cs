using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// A lightweight graphics context for text measurement only.
/// </summary>
internal sealed class GdiMeasurementContext : IGraphicsContext
{
    private readonly nint _hdc;
    private bool _disposed;

    public double DpiScale { get; }

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public GdiMeasurementContext(nint hdc, uint dpi)
    {
        _hdc = hdc;
        DpiScale = dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            User32.ReleaseDC(0, _hdc);
            _disposed = true;
        }
    }

    public unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty || font is not GdiFont gdiFont)
        {
            return Size.Empty;
        }

        var oldFont = Gdi32.SelectObject(_hdc, gdiFont.Handle);
        try
        {
            var hasLineBreaks = text.IndexOfAny('\r', '\n') >= 0;
            var rect = hasLineBreaks
                ? new RECT(0, 0, LayoutRounding.RoundToPixelInt(1_000_000, DpiScale), 0)
                : new RECT(0, 0, 0, 0);

            uint format = hasLineBreaks
                ? GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX
                : GdiConstants.DT_CALCRECT | GdiConstants.DT_SINGLELINE | GdiConstants.DT_NOPREFIX;

            fixed (char* pText = text)
            {
                Gdi32.DrawText(_hdc, pText, text.Length, ref rect, format);
            }
            return new Size(rect.Width / DpiScale, rect.Height / DpiScale);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldFont);
        }
    }

    public unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty || font is not GdiFont gdiFont)
        {
            return Size.Empty;
        }

        if (double.IsNaN(maxWidth) || maxWidth <= 0 || double.IsInfinity(maxWidth))
        {
            maxWidth = 1_000_000;
        }

        var maxWidthPx = LayoutRounding.RoundToPixelInt(maxWidth, DpiScale);
        if (maxWidthPx <= 0)
        {
            maxWidthPx = LayoutRounding.RoundToPixelInt(1_000_000, DpiScale);
        }

        var oldFont = Gdi32.SelectObject(_hdc, gdiFont.Handle);
        try
        {
            var rect = new RECT(0, 0, maxWidthPx, 0);
            fixed (char* pText = text)
            {
                Gdi32.DrawText(_hdc, pText, text.Length, ref rect,
                    GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX);
            }
            return new Size(rect.Width / DpiScale, rect.Height / DpiScale);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldFont);
        }
    }

    // Below methods are not used for measurement but required by interface
    public void Save() { }
    public void Restore() { }
    public void SetClip(Rect rect) { }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY) { }
    public void Translate(double dx, double dy) { }
    public void Clear(Color color) { }
    public void DrawLine(Point start, Point end, Color color, double thickness = 1) { }
    public void DrawRectangle(Rect rect, Color color, double thickness = 1) { }
    public void FillRectangle(Rect rect, Color color) { }
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1) { }
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color) { }
    public void DrawEllipse(Rect bounds, Color color, double thickness = 1) { }
    public void FillEllipse(Rect bounds, Color color) { }
    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color) { }
    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color, TextAlignment horizontalAlignment = TextAlignment.Left, TextAlignment verticalAlignment = TextAlignment.Top, TextWrapping wrapping = TextWrapping.NoWrap) { }
    public void DrawImage(IImage image, Point location) { }
    public void DrawImage(IImage image, Rect destRect) { }
    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) { }
}
