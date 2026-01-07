namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed class FreeTypeFont : IFont
{
    public string Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public bool IsItalic { get; }
    public bool IsUnderline { get; }
    public bool IsStrikethrough { get; }

    public string FontPath { get; }
    public int PixelHeight { get; }

    public FreeTypeFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, string fontPath, int pixelHeight)
    {
        Family = family;
        Size = size;
        Weight = weight;
        IsItalic = italic;
        IsUnderline = underline;
        IsStrikethrough = strikethrough;
        FontPath = fontPath;
        PixelHeight = pixelHeight;
    }

    public void Dispose()
    {
        // No per-font native handle; faces are created per operation currently.
    }
}

