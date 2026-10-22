namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Optional capability exposed by fonts that can emit vector glyph outlines.
/// </summary>
public interface IGlyphOutlineFont
{
    /// <summary>
    /// Appends the outline of <paramref name="ch"/> to <paramref name="path"/> at the given baseline origin.
    /// Returns the glyph advance in DIPs.
    /// </summary>
    bool TryAppendGlyphOutline(PathGeometry path, char ch, Point baselineOrigin, out double advance);
}
