namespace Aprillz.MewUI.Native.Constants;

/// <summary>
/// GDI constants
/// </summary>
internal static class GdiConstants
{
    // Background modes
    public const int TRANSPARENT = 1;
    public const int OPAQUE = 2;

    // Stock objects
    public const int WHITE_BRUSH = 0;
    public const int LTGRAY_BRUSH = 1;
    public const int GRAY_BRUSH = 2;
    public const int DKGRAY_BRUSH = 3;
    public const int BLACK_BRUSH = 4;
    public const int NULL_BRUSH = 5;
    public const int HOLLOW_BRUSH = NULL_BRUSH;
    public const int WHITE_PEN = 6;
    public const int BLACK_PEN = 7;
    public const int NULL_PEN = 8;
    public const int OEM_FIXED_FONT = 10;
    public const int ANSI_FIXED_FONT = 11;
    public const int ANSI_VAR_FONT = 12;
    public const int SYSTEM_FONT = 13;
    public const int DEVICE_DEFAULT_FONT = 14;
    public const int DEFAULT_PALETTE = 15;
    public const int SYSTEM_FIXED_FONT = 16;
    public const int DEFAULT_GUI_FONT = 17;
    public const int DC_BRUSH = 18;
    public const int DC_PEN = 19;

    // Pen styles (cosmetic)
    public const int PS_SOLID = 0;
    public const int PS_DASH = 1;
    public const int PS_DOT = 2;
    public const int PS_DASHDOT = 3;
    public const int PS_DASHDOTDOT = 4;
    public const int PS_NULL = 5;
    public const int PS_INSIDEFRAME = 6;

    // Geometric pen type (used with ExtCreatePen)
    public const uint PS_GEOMETRIC = 0x00010000;
    public const uint PS_COSMETIC  = 0x00000000;

    // End-cap styles (combined into PS_GEOMETRIC pen style)
    public const uint PS_ENDCAP_ROUND  = 0x00000100;
    public const uint PS_ENDCAP_SQUARE = 0x00000200;
    public const uint PS_ENDCAP_FLAT   = 0x00000000;

    // Join styles (combined into PS_GEOMETRIC pen style)
    public const uint PS_JOIN_ROUND = 0x00001000;
    public const uint PS_JOIN_BEVEL = 0x00002000;
    public const uint PS_JOIN_MITER = 0x00000000;

    // Polygon fill modes (SetPolyFillMode)
    public const int ALTERNATE = 1;
    public const int WINDING   = 2;

    // Brush styles
    public const int BS_SOLID = 0;
    public const int BS_NULL = 1;
    public const int BS_HOLLOW = BS_NULL;
    public const int BS_HATCHED = 2;
    public const int BS_PATTERN = 3;

    // Hatch styles
    public const int HS_HORIZONTAL = 0;
    public const int HS_VERTICAL = 1;
    public const int HS_FDIAGONAL = 2;
    public const int HS_BDIAGONAL = 3;
    public const int HS_CROSS = 4;
    public const int HS_DIAGCROSS = 5;

    // Object types
    public const uint OBJ_PEN = 1;
    public const uint OBJ_BRUSH = 2;
    public const uint OBJ_DC = 3;
    public const uint OBJ_METADC = 4;
    public const uint OBJ_PAL = 5;
    public const uint OBJ_FONT = 6;
    public const uint OBJ_BITMAP = 7;

    // Text alignment
    public const uint TA_NOUPDATECP = 0;
    public const uint TA_UPDATECP = 1;
    public const uint TA_LEFT = 0;
    public const uint TA_RIGHT = 2;
    public const uint TA_CENTER = 6;
    public const uint TA_TOP = 0;
    public const uint TA_BOTTOM = 8;
    public const uint TA_BASELINE = 24;

    // DrawText format
    public const uint DT_TOP = 0x00000000;
    public const uint DT_LEFT = 0x00000000;
    public const uint DT_CENTER = 0x00000001;
    public const uint DT_RIGHT = 0x00000002;
    public const uint DT_VCENTER = 0x00000004;
    public const uint DT_BOTTOM = 0x00000008;
    public const uint DT_WORDBREAK = 0x00000010;
    public const uint DT_SINGLELINE = 0x00000020;
    public const uint DT_EXPANDTABS = 0x00000040;
    public const uint DT_TABSTOP = 0x00000080;
    public const uint DT_NOCLIP = 0x00000100;
    public const uint DT_EXTERNALLEADING = 0x00000200;
    public const uint DT_CALCRECT = 0x00000400;
    public const uint DT_NOPREFIX = 0x00000800;
    public const uint DT_INTERNAL = 0x00001000;
    public const uint DT_EDITCONTROL = 0x00002000;
    public const uint DT_PATH_ELLIPSIS = 0x00004000;
    public const uint DT_END_ELLIPSIS = 0x00008000;
    public const uint DT_MODIFYSTRING = 0x00010000;
    public const uint DT_RTLREADING = 0x00020000;
    public const uint DT_WORD_ELLIPSIS = 0x00040000;

    // Raster operation codes
    public const uint SRCCOPY = 0x00CC0020;
    public const uint SRCPAINT = 0x00EE0086;
    public const uint SRCAND = 0x008800C6;
    public const uint SRCINVERT = 0x00660046;
    public const uint SRCERASE = 0x00440328;
    public const uint NOTSRCCOPY = 0x00330008;
    public const uint NOTSRCERASE = 0x001100A6;
    public const uint MERGECOPY = 0x00C000CA;
    public const uint MERGEPAINT = 0x00BB0226;
    public const uint PATCOPY = 0x00F00021;
    public const uint PATPAINT = 0x00FB0A09;
    public const uint PATINVERT = 0x005A0049;
    public const uint DSTINVERT = 0x00550009;
    public const uint BLACKNESS = 0x00000042;
    public const uint WHITENESS = 0x00FF0062;

    // Bitmap
    public const uint DIB_RGB_COLORS = 0;
    public const uint DIB_PAL_COLORS = 1;

    // StretchBltMode
    public const int BLACKONWHITE = 1;
    public const int WHITEONBLACK = 2;
    public const int COLORONCOLOR = 3;
    public const int HALFTONE = 4;
    public const int STRETCH_ANDSCANS = BLACKONWHITE;
    public const int STRETCH_ORSCANS = WHITEONBLACK;
    public const int STRETCH_DELETESCANS = COLORONCOLOR;
    public const int STRETCH_HALFTONE = HALFTONE;

    // Font weight
    public const int FW_DONTCARE = 0;
    public const int FW_THIN = 100;
    public const int FW_EXTRALIGHT = 200;
    public const int FW_ULTRALIGHT = 200;
    public const int FW_LIGHT = 300;
    public const int FW_NORMAL = 400;
    public const int FW_REGULAR = 400;
    public const int FW_MEDIUM = 500;
    public const int FW_SEMIBOLD = 600;
    public const int FW_DEMIBOLD = 600;
    public const int FW_BOLD = 700;
    public const int FW_EXTRABOLD = 800;
    public const int FW_ULTRABOLD = 800;
    public const int FW_HEAVY = 900;
    public const int FW_BLACK = 900;

    // Font charset
    public const uint ANSI_CHARSET = 0;
    public const uint DEFAULT_CHARSET = 1;
    public const uint SYMBOL_CHARSET = 2;
    public const uint SHIFTJIS_CHARSET = 128;
    public const uint HANGUL_CHARSET = 129;
    public const uint GB2312_CHARSET = 134;
    public const uint CHINESEBIG5_CHARSET = 136;
    public const uint GREEK_CHARSET = 161;
    public const uint TURKISH_CHARSET = 162;
    public const uint HEBREW_CHARSET = 177;
    public const uint ARABIC_CHARSET = 178;
    public const uint BALTIC_CHARSET = 186;
    public const uint RUSSIAN_CHARSET = 204;
    public const uint THAI_CHARSET = 222;
    public const uint EASTEUROPE_CHARSET = 238;
    public const uint OEM_CHARSET = 255;

    // Font output precision
    public const uint OUT_DEFAULT_PRECIS = 0;
    public const uint OUT_STRING_PRECIS = 1;
    public const uint OUT_CHARACTER_PRECIS = 2;
    public const uint OUT_STROKE_PRECIS = 3;
    public const uint OUT_TT_PRECIS = 4;
    public const uint OUT_DEVICE_PRECIS = 5;
    public const uint OUT_RASTER_PRECIS = 6;
    public const uint OUT_TT_ONLY_PRECIS = 7;
    public const uint OUT_OUTLINE_PRECIS = 8;
    public const uint OUT_SCREEN_OUTLINE_PRECIS = 9;
    public const uint OUT_PS_ONLY_PRECIS = 10;

    // Font clip precision
    public const uint CLIP_DEFAULT_PRECIS = 0;
    public const uint CLIP_CHARACTER_PRECIS = 1;
    public const uint CLIP_STROKE_PRECIS = 2;

    // Font quality
    public const uint DEFAULT_QUALITY = 0;
    public const uint DRAFT_QUALITY = 1;
    public const uint PROOF_QUALITY = 2;
    public const uint NONANTIALIASED_QUALITY = 3;
    public const uint ANTIALIASED_QUALITY = 4;
    public const uint CLEARTYPE_QUALITY = 5;
    public const uint CLEARTYPE_NATURAL_QUALITY = 6;

    // Font pitch
    public const uint DEFAULT_PITCH = 0;
    public const uint FIXED_PITCH = 1;
    public const uint VARIABLE_PITCH = 2;

    // Font family
    public const uint FF_DONTCARE = 0 << 4;
    public const uint FF_ROMAN = 1 << 4;
    public const uint FF_SWISS = 2 << 4;
    public const uint FF_MODERN = 3 << 4;
    public const uint FF_SCRIPT = 4 << 4;
    public const uint FF_DECORATIVE = 5 << 4;
}
