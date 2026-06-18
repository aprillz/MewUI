using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

internal sealed class MewFontDefn : IFontDefn
{
    private readonly IFont _font;
    private readonly IGlyphOutlineFont? _outlineFont;
    private readonly double _ppi;

    public double Size => _font.Size;
    public double SizeInPoints => _font.Size * 72.0 / _ppi;

    public MewFontDefn(IFont font, double ppi)
    {
        _font = font;
        _outlineFont = font as IGlyphOutlineFont;
        _ppi = ppi;
    }

    public void AddStringToPath(ISvgRenderer renderer, PathGeometry path, string text, Point location)
    {
        if (_outlineFont is null)
        {
            return;
        }

        // Each glyph is rendered at its own gmCellIncX-based advance (kerning-unaware),
        // but adjacent-character cursor positions must respect the kerning the layout
        // engine applied to the prefix. Earlier code used `cursor[i] = MeasureText(prefix
        // exclusive of char i)` which placed each char at the kerned end of the previous
        // prefix — but the previous glyph was drawn at its un-kerned own advance, so the
        // two diverged by the kerning amount. Visible as adjacent kerning pairs (e.g.
        // Arial 'Te') overlapping the next glyph by ~5 px at 48 px font.
        //
        // Correct cursor = MeasureText(prefix INCLUSIVE) − own advance of this glyph.
        // The inclusive prefix is the kerned right edge of the run up to and including
        // this glyph; subtracting the glyph's own advance yields its kerned start
        // position. Single-character MeasureText returns the glyph's own advance with
        // no kerning partner.
        Span<char> single = stackalloc char[1];
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var prefixInclusive = renderer.GraphicsContext.MeasureText(text.AsSpan(0, i + 1), _font).Width;
            single[0] = ch;
            var ownAdvance = renderer.GraphicsContext.MeasureText(single, _font).Width;
            var cursor = new Point(location.X + prefixInclusive - ownAdvance, location.Y);
            _outlineFont.TryAppendGlyphOutline(path, ch, cursor, out _);
        }
    }

    public double Ascent(ISvgRenderer renderer) => _font.Ascent;

    public IList<Rect> MeasureCharacters(ISvgRenderer renderer, string text)
    {
        var results = new List<Rect>(text.Length);
        double previousWidth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var prefixSize = renderer.GraphicsContext.MeasureText(text.AsSpan(0, i + 1), _font);
            var width = Math.Max(0, prefixSize.Width - previousWidth);
            results.Add(new Rect(previousWidth, 0, width, Ascent(renderer)));
            previousWidth = prefixSize.Width;
        }

        return results;
    }

    public Size MeasureString(ISvgRenderer renderer, string text)
    {
        var size = renderer.GraphicsContext.MeasureText(text, _font);
        return new Size(size.Width, Ascent(renderer));
    }

    public void Dispose()
    {
        _font.Dispose();
    }

}
