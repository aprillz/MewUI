namespace Aprillz.MewUI.Rendering.OpenGL;

using Aprillz.MewUI.Rendering.FreeType;

/// <summary>
/// Temporary, cross-platform measurement context for Linux bring-up.
/// Uses a heuristic until a real text stack is integrated.
/// </summary>
internal sealed class OpenGLMeasurementContext : IGraphicsContext
{
    public double DpiScale { get; }

    public OpenGLMeasurementContext(uint dpi) => DpiScale = dpi <= 0 ? 1.0 : dpi / 96.0;

    public void Dispose()
    { }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (OperatingSystem.IsLinux() && font is FreeTypeFont ftFont)
        {
            var px = FreeTypeText.Measure(text, ftFont);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        double size = font.Size <= 0 ? 12 : font.Size;
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines++;
            }
        }

        double lineHeight = size * 1.25;
        double maxLineChars = 0;
        int current = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                if (current > maxLineChars)
                {
                    maxLineChars = current;
                }

                current = 0;
                continue;
            }
            current++;
        }
        if (current > maxLineChars)
        {
            maxLineChars = current;
        }

        double width = maxLineChars * size * 0.6;
        double height = lines * lineHeight;
        return new Size(width, height);
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (double.IsNaN(maxWidth) || maxWidth <= 0 || double.IsInfinity(maxWidth))
        {
            return MeasureText(text, font);
        }

        if (OperatingSystem.IsLinux() && font is FreeTypeFont ftFont)
        {
            // TODO: wrapping-aware measurement; for now ignore maxWidth.
            var px = FreeTypeText.Measure(text, ftFont);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        var raw = MeasureText(text, font);
        if (raw.Width <= maxWidth)
        {
            return raw;
        }

        double size = font.Size <= 0 ? 12 : font.Size;
        double charsPerLine = Math.Max(1, maxWidth / (size * 0.6));
        int totalChars = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n')
            {
                totalChars++;
            }
        }
        double lineCount = Math.Ceiling(totalChars / charsPerLine);
        double height = lineCount * size * 1.25;
        return new Size(maxWidth, height);
    }

    public void Save()
    { }

    public void Restore()
    { }

    public void SetClip(Rect rect)
    { }

    public void Translate(double dx, double dy)
    { }

    public void Clear(Color color)
    { }

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    { }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    { }

    public void FillRectangle(Rect rect, Color color)
    { }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    { }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    { }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    { }

    public void FillEllipse(Rect bounds, Color color)
    { }

    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color)
    { }

    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color, TextAlignment horizontalAlignment = TextAlignment.Left, TextAlignment verticalAlignment = TextAlignment.Top, TextWrapping wrapping = TextWrapping.NoWrap)
    { }

    public void DrawImage(IImage image, Point location)
    { }

    public void DrawImage(IImage image, Rect destRect)
    { }

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
    { }
}