using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public sealed class SvgFontDefn : IFontDefn
{
    private readonly SvgFont _font;
    private readonly double _emScale;
    private readonly double _ppi;
    private readonly double _size;
    private Dictionary<string, SvgGlyph>? _glyphs;
    private Dictionary<string, SvgKern>? _kerning;

    public double Size => _size;
    public double SizeInPoints => _size * 72.0 / _ppi;

    public SvgFontDefn(SvgFont font, double size, double ppi)
    {
        _font = font;
        _size = size;
        _ppi = ppi;
        var face = _font.Children.OfType<SvgFontFace>().First();
        _emScale = _size / face.UnitsPerEm;
    }

    public double Ascent(ISvgRenderer renderer)
    {
        var ascent = _font.Descendants().OfType<SvgFontFace>().First().Ascent;
        var baselineOffset = SizeInPoints * (_emScale / _size) * ascent;
        return _ppi / 72.0 * baselineOffset;
    }

    public IList<Rect> MeasureCharacters(ISvgRenderer renderer, string text)
    {
        var result = new List<Rect>();
        _ = GetPath(renderer, text, result, false);
        return result;
    }

    public Size MeasureString(ISvgRenderer renderer, string text)
    {
        var result = new List<Rect>();
        _ = GetPath(renderer, text, result, true);

        double? firstLeft = null;
        double? lastRight = null;
        foreach (var rect in result.Where(r => r != Rect.Empty))
        {
            firstLeft ??= rect.Left;
            lastRight = rect.Right;
        }

        if (firstLeft is null || lastRight is null)
        {
            return new Size(0, 0);
        }

        return new Size(lastRight.Value - firstLeft.Value, Ascent(renderer));
    }

    public void AddStringToPath(ISvgRenderer renderer, PathGeometry path, string text, Point location)
    {
        var textPath = GetPath(renderer, text, null, false);
        if (textPath.IsEmpty)
        {
            return;
        }

        var matrix = Matrix3x2.CreateTranslation((float)location.X, (float)location.Y);
        path.AddPath(MewSvgPathUtilities.TransformPath(textPath, matrix));
    }

    private PathGeometry GetPath(ISvgRenderer renderer, string text, IList<Rect>? ranges, bool measureSpaces)
    {
        EnsureDictionaries();

        var result = new PathGeometry();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var ascent = Ascent(renderer);
        SvgGlyph? previousGlyph = null;
        double xPos = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (!_glyphs!.TryGetValue(text.Substring(i, 1), out var glyph))
            {
                glyph = _font.Descendants().OfType<SvgMissingGlyph>().First();
            }

            if (previousGlyph is not null && _kerning!.TryGetValue(previousGlyph.GlyphName + "|" + glyph.GlyphName, out var kern))
            {
                xPos -= kern.Kerning * _emScale;
            }

            var glyphPath = glyph.Path(renderer) ?? new PathGeometry();
            var matrix =
                Matrix3x2.CreateScale((float)_emScale, (float)-_emScale) *
                Matrix3x2.CreateTranslation((float)xPos, (float)ascent);
            var transformed = MewSvgPathUtilities.TransformPath(glyphPath, matrix);
            var bounds = transformed.GetBounds();

            if (ranges is not null)
            {
                if (measureSpaces && bounds == Rect.Empty)
                {
                    ranges.Add(new Rect(xPos, 0, glyph.HorizAdvX * _emScale, ascent));
                }
                else
                {
                    ranges.Add(bounds);
                }
            }

            if (!transformed.IsEmpty)
            {
                result.AddPath(transformed);
            }

            xPos += glyph.HorizAdvX * _emScale;
            previousGlyph = glyph;
        }

        return result;
    }

    private void EnsureDictionaries()
    {
        _glyphs ??= _font.Descendants().OfType<SvgGlyph>().ToDictionary(g => g.Unicode ?? g.GlyphName ?? g.ID);
        _kerning ??= _font.Descendants().OfType<SvgKern>().ToDictionary(k => k.Glyph1 + "|" + k.Glyph2);
    }

    public void Dispose()
    {
        _glyphs = null;
        _kerning = null;
    }
}
