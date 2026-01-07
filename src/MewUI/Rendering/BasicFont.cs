namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Cross-platform fallback font representation (no native handle).
/// Used for early Linux bring-up until a real text stack (e.g. FreeType/HarfBuzz) is integrated.
/// </summary>
internal sealed class BasicFont : IFont
{
    public string Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public bool IsItalic { get; }
    public bool IsUnderline { get; }
    public bool IsStrikethrough { get; }

    public BasicFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        Family = family;
        Size = size;
        Weight = weight;
        IsItalic = italic;
        IsUnderline = underline;
        IsStrikethrough = strikethrough;
    }

    public void Dispose()
    {
        // No native resources.
    }
}

