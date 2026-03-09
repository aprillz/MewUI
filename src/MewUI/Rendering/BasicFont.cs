namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Concrete fallback font with no native handle.
/// Used when a platform text stack (e.g. FreeType) is unavailable.
/// </summary>
internal sealed class BasicFont : FontBase
{
    public BasicFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        Ascent = size;
        Descent = size * 0.25;
    }
}
