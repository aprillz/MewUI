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
