using Aprillz.MewUI.Rendering.CoreText;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalMeasurementContext : IGraphicsContext
{
    private readonly uint _dpi;

    public MewVGMetalMeasurementContext(uint dpi)
    {
        _dpi = dpi == 0 ? 96u : dpi;
    }

    public double DpiScale => _dpi / 96.0;

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public void Dispose() { }

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
    public void DrawPath(PathGeometry path, Color color, double thickness = 1) { }
    public void FillPath(PathGeometry path, Color color) { }

    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color) { }

    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap) { }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not CoreTextFont ct)
        {
            return new Size(text.Length * 8, 16);
        }

        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx: 0, TextWrapping.NoWrap, _dpi);
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not CoreTextFont ct)
        {
            return new Size(text.Length * 8, 16);
        }

        int maxWidthPx = maxWidth <= 0 ? 0 : Math.Max(1, LayoutRounding.CeilToPixelInt(maxWidth, DpiScale));
        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx, TextWrapping.Wrap, _dpi);
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    public void DrawImage(IImage image, Point location) { }
    public void DrawImage(IImage image, Rect destRect) { }
    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) { }
}
