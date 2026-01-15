using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DMeasurementContext : IGraphicsContext
{
    private readonly nint _dwriteFactory;

    public double DpiScale => 1.0;

    public Direct2DMeasurementContext(nint dwriteFactory) => _dwriteFactory = dwriteFactory;

    public void Dispose() { }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font) => MeasureText(text, font, float.MaxValue);

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));
        }

        nint textFormat = 0;
        nint textLayout = 0;
        try
        {
            var weight = (DWRITE_FONT_WEIGHT)(int)dwFont.Weight;
            var style = dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, dwFont.Family, weight, style, (float)dwFont.Size, out textFormat);
            if (hr < 0 || textFormat == 0)
            {
                return Size.Empty;
            }

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)Math.Max(0, maxWidth);
            hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return Size.Empty;
            }

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0)
            {
                return Size.Empty;
            }

            return new Size(metrics.widthIncludingTrailingWhitespace, metrics.height);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    public void Save() { }
    public void Restore() { }
    public void SetClip(Rect rect) { }
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
