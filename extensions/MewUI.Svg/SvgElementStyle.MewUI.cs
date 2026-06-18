using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgElement
{
    internal IFontDefn GetFont(ISvgRenderer renderer, SvgFontManager? fontManager)
    {
        fontManager ??= new SvgFontManager();

        double fontSize;
        var fontSizeUnit = FontSize;
        if (fontSizeUnit == SvgUnit.None || fontSizeUnit == SvgUnit.Empty)
        {
            fontSize = new SvgUnit(SvgUnitType.Em, 1f).ToDeviceValue(renderer, UnitRenderingType.Vertical, this);
        }
        else
        {
            fontSize = fontSizeUnit.ToDeviceValue(renderer, UnitRenderingType.Vertical, this);
        }

        fontSize = fontSize <= 0 ? 16 : fontSize;

        var ppi = OwnerDocument?.Ppi ?? SvgDocument.PointsPerInch;
        var family = ValidateFontFamily(FontFamily, OwnerDocument, fontManager);
        if (family is IEnumerable<SvgFontFace> svgFaces)
        {
            var svgFont = svgFaces.First().Parent as SvgFont;
            if (svgFont is null)
            {
                var uri = svgFaces.First().Descendants().OfType<SvgFontFaceUri>().FirstOrDefault()?.ReferencedElement;
                svgFont = OwnerDocument?.IdManager.GetElementById(uri) as SvgFont;
            }

            if (svgFont is not null)
            {
                return new SvgFontDefn(svgFont, fontSize, ppi);
            }
        }

        var resolvedFamily = family as string ?? GetGenericSansFallback(fontManager);
        // Pass renderer DpiScale through to font creation. Otherwise the FreeType
        // backend creates glyph outlines at 96-dpi pixel size, but its MeasureText
        // divides pixel widths by the renderer's DpiScale (which can be 4× when
        // SvgView renders at zoom). Result: glyph advances shrink to 1/scale and
        // letters stack on top of each other. Using the DPI overload sizes the
        // outline at scale×96, so MeasureText / DpiScale lands back on DIP units
        // matching the glyph layout.
        uint dpi = renderer.GraphicsContext is { DpiScale: > 0 } ctx
            ? (uint)Math.Max(96, Math.Round(96.0 * ctx.DpiScale))
            : 96u;
        var font = renderer.GraphicsFactory.CreateFont(
            resolvedFamily,
            fontSize,
            dpi,
            MapFontWeight(FontWeight, Parent?.FontWeight ?? SvgFontWeight.Normal),
            italic: FontStyle is SvgFontStyle.Italic or SvgFontStyle.Oblique,
            underline: TextDecoration.HasFlag(SvgTextDecoration.Underline),
            strikethrough: TextDecoration.HasFlag(SvgTextDecoration.LineThrough));
        return new MewFontDefn(font, ppi);
    }

    public static object ValidateFontFamily(string? fontFamilyList, SvgDocument? doc, SvgFontManager fontManager)
    {
        var fontParts = (fontFamilyList ?? string.Empty)
            .Split([','], StringSplitOptions.RemoveEmptyEntries)
            .Select(fontName => fontName.Trim('"', ' ', '\''));

        foreach (var part in fontParts)
        {
            if (doc is not null && doc.FontDefns().TryGetValue(part, out var fontFaces))
            {
                return fontFaces;
            }

            var family = fontManager.FindFont(part);
            if (!string.IsNullOrWhiteSpace(family))
            {
                return family;
            }
        }

        return GetGenericSansFallback(fontManager);
    }

    private static string GetGenericSansFallback(SvgFontManager fontManager)
        => fontManager.FindFont("sans-serif") ?? GetPlatformDefaultFontFamily();

    private static string GetPlatformDefaultFontFamily()
    {
        try
        {
            return Application.Current.PlatformHost.DefaultFontFamily;
        }
        catch
        {
            try
            {
                return Application.DefaultPlatformHost.DefaultFontFamily;
            }
            catch
            {
                return "sans-serif";
            }
        }
    }

    private static Aprillz.MewUI.FontWeight MapFontWeight(global::Svg.SvgFontWeight weight, global::Svg.SvgFontWeight parentWeight)
    {
        if (weight.HasFlag(SvgFontWeight.Bold) ||
            weight.HasFlag(SvgFontWeight.W600) ||
            weight.HasFlag(SvgFontWeight.W700) ||
            weight.HasFlag(SvgFontWeight.W800) ||
            weight.HasFlag(SvgFontWeight.W900))
            return Aprillz.MewUI.FontWeight.Bold;
        if (weight.HasFlag(SvgFontWeight.Bolder))
        {
            return parentWeight switch
            {
                SvgFontWeight.W100 or SvgFontWeight.W200 or SvgFontWeight.W300 => Aprillz.MewUI.FontWeight.Normal,
                _ => Aprillz.MewUI.FontWeight.Bold,
            };
        }
        if (weight.HasFlag(SvgFontWeight.Lighter))
        {
            return parentWeight switch
            {
                SvgFontWeight.W800 or SvgFontWeight.W900 => Aprillz.MewUI.FontWeight.Bold,
                _ => Aprillz.MewUI.FontWeight.Normal,
            };
        }
        if (weight.HasFlag(SvgFontWeight.W100))
            return Aprillz.MewUI.FontWeight.Thin;
        if (weight.HasFlag(SvgFontWeight.W200))
            return Aprillz.MewUI.FontWeight.ExtraLight;
        if (weight.HasFlag(SvgFontWeight.W300))
            return Aprillz.MewUI.FontWeight.Light;
        if (weight.HasFlag(SvgFontWeight.W500))
            return Aprillz.MewUI.FontWeight.Medium;
        if (weight.HasFlag(SvgFontWeight.W600))
            return Aprillz.MewUI.FontWeight.SemiBold;
        if (weight.HasFlag(SvgFontWeight.W800))
            return Aprillz.MewUI.FontWeight.ExtraBold;
        if (weight.HasFlag(SvgFontWeight.W900))
            return Aprillz.MewUI.FontWeight.Black;

        return Aprillz.MewUI.FontWeight.Normal;
    }
}
