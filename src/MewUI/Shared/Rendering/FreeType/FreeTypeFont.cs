namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed class FreeTypeFont : FontBase
{
    public string FontPath { get; }
    public int PixelHeight { get; }

    public FreeTypeFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, string fontPath, int pixelHeight)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        FontPath = fontPath;
        PixelHeight = pixelHeight;

        // Query metrics from FreeType face.
        try
        {
            var face = FreeTypeFaceCache.Instance.Get(fontPath, pixelHeight, weight, italic);
            var metrics = FreeTypeFaceCache.GetSizeMetrics(face.Face);
            double ascentPx = (long)metrics.ascender / 64.0;
            double descentPx = -(long)metrics.descender / 64.0; // FreeType descender is negative
            double heightPx = (long)metrics.height / 64.0;
            double dpiScale = pixelHeight > 0 ? pixelHeight / size : 1.0;

            Ascent = ascentPx / dpiScale;
            Descent = descentPx / dpiScale;
            InternalLeading = Math.Max(0, (heightPx - ascentPx - descentPx) / dpiScale);
        }
        catch
        {
            // Fallback: approximate from size.
            Ascent = size;
            Descent = size * 0.25;
        }
    }
}

