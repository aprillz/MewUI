namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed class DirectWriteFont : IFont
{
    public string Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public bool IsItalic { get; }
    public bool IsUnderline { get; }
    public bool IsStrikethrough { get; }

    public DirectWriteFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        Family = family;
        Size = size;
        Weight = weight;
        IsItalic = italic;
        IsUnderline = underline;
        IsStrikethrough = strikethrough;
    }

    public void Dispose() { }
}
