using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// A lightweight graphics context for text measurement only.
/// </summary>
internal sealed class GdiMeasurementContext : MeasureGraphicsContextBase
{
    private readonly nint _hdc;
    private bool _disposed;

    public override double DpiScale { get; }

    public GdiMeasurementContext(nint hdc, uint dpi)
    {
        _hdc = hdc;
        DpiScale = dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            User32.ReleaseDC(0, _hdc);
            _disposed = true;
        }
    }

    public override unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font)
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

    public override unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
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
}
