namespace Aprillz.MewUI.Native.DirectWrite;

internal enum DWRITE_FACTORY_TYPE : uint
{
    SHARED = 0,
    ISOLATED = 1
}

internal enum DWRITE_FONT_WEIGHT : uint
{
    THIN = 100,
    EXTRA_LIGHT = 200,
    LIGHT = 300,
    NORMAL = 400,
    MEDIUM = 500,
    SEMI_BOLD = 600,
    BOLD = 700,
    EXTRA_BOLD = 800,
    BLACK = 900
}

internal enum DWRITE_FONT_STYLE : uint
{
    NORMAL = 0,
    OBLIQUE = 1,
    ITALIC = 2
}

internal enum DWRITE_FONT_STRETCH : uint
{
    UNDEFINED = 0,
    ULTRA_CONDENSED = 1,
    EXTRA_CONDENSED = 2,
    CONDENSED = 3,
    SEMI_CONDENSED = 4,
    NORMAL = 5,
    SEMI_EXPANDED = 6,
    EXPANDED = 7,
    EXTRA_EXPANDED = 8,
    ULTRA_EXPANDED = 9
}

internal enum DWRITE_TEXT_ALIGNMENT : uint
{
    LEADING = 0,
    TRAILING = 1,
    CENTER = 2,
    JUSTIFIED = 3
}

internal enum DWRITE_PARAGRAPH_ALIGNMENT : uint
{
    NEAR = 0,
    FAR = 1,
    CENTER = 2
}

internal enum DWRITE_WORD_WRAPPING : uint
{
    WRAP = 0,
    NO_WRAP = 1,
    EMERGENCY_BREAK = 2,
    WHOLE_WORD = 3,
    CHARACTER = 4
}

internal enum DWRITE_LINE_SPACING_METHOD : uint
{
    DEFAULT = 0,
    UNIFORM = 1,
    PROPORTIONAL = 2
}

internal enum DWRITE_TRIMMING_GRANULARITY : uint
{
    NONE = 0,
    CHARACTER = 1,
    WORD = 2
}

internal enum DWRITE_FONT_FACE_TYPE : uint
{
    CFF = 0,
    TRUETYPE = 1,
    OPENTYPE_COLLECTION = 2, // TTC
    TYPE1 = 3,
    VECTOR = 4,
    BITMAP = 5,
    UNKNOWN = 6,
    RAW_CFF = 7
}

internal enum DWRITE_FONT_SIMULATIONS : uint
{
    NONE = 0x0000,
    BOLD = 0x0001,
    OBLIQUE = 0x0002
}

internal enum DWRITE_PIXEL_GEOMETRY : uint
{
    FLAT = 0,
    RGB = 1,
    BGR = 2
}

internal enum DWRITE_RENDERING_MODE : uint
{
    DEFAULT = 0,
    ALIASED = 1,
    GDI_CLASSIC = 2,
    GDI_NATURAL = 3,
    NATURAL = 4,
    NATURAL_SYMMETRIC = 5,
    OUTLINE = 6
}

internal enum DWRITE_MEASURING_MODE : uint
{
    NATURAL = 0,
    GDI_CLASSIC = 1,
    GDI_NATURAL = 2
}

internal struct DWRITE_TRIMMING
{
    public DWRITE_TRIMMING_GRANULARITY granularity;
    public uint delimiter;
    public uint delimiterCount;
}
