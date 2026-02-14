using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.FreeType;

internal static partial class FreeType
{
    private const string LibraryName = "libfreetype.so.6";

    [LibraryImport(LibraryName)]
    public static partial int FT_Init_FreeType(out nint alibrary);

    [LibraryImport(LibraryName)]
    public static partial int FT_Done_FreeType(nint library);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FT_New_Face(nint library, string filepathname, int face_index, out nint aface);

    [LibraryImport(LibraryName)]
    public static partial int FT_Done_Face(nint face);

    [LibraryImport(LibraryName)]
    public static partial int FT_Set_Pixel_Sizes(nint face, uint pixel_width, uint pixel_height);

    [LibraryImport(LibraryName)]
    public static partial int FT_Load_Char(nint face, uint char_code, int load_flags);

    [LibraryImport(LibraryName)]
    public static partial uint FT_Get_Char_Index(nint face, uint charcode);

    [LibraryImport(LibraryName)]
    public static partial int FT_Get_Kerning(nint face, uint left_glyph, uint right_glyph, uint kern_mode, out FT_Vector akerning);

    [LibraryImport(LibraryName)]
    public static partial int FT_Get_Glyph(nint slot, out nint aglyph);

    [LibraryImport(LibraryName)]
    public static partial int FT_Get_Advance(nint face, uint gindex, int load_flags, out nint padvance);

    [LibraryImport(LibraryName)]
    public static partial void FT_Done_Glyph(nint glyph);

    [LibraryImport(LibraryName)]
    public static partial int FT_Glyph_To_Bitmap(ref nint the_glyph, uint render_mode, nint origin, [MarshalAs(UnmanagedType.Bool)] bool destroy);
}

internal static class FreeTypeLoad
{
    public const int FT_LOAD_DEFAULT = 0x0;
    public const int FT_LOAD_RENDER = 0x4;
    public const int FT_LOAD_TARGET_NORMAL = 0x0;
    // FT_LOAD_TARGET_XXX flags are (FT_Render_Mode_XXX << 16).
    // See FreeType: FT_LOAD_TARGET( x ) macro.
    public const int FT_LOAD_TARGET_LIGHT = 0x1 << 16;
    public const int FT_LOAD_TARGET_LCD = 0x3 << 16;
    public const int FT_LOAD_FORCE_AUTOHINT = 0x20;
}

internal static class FreeTypeKerning
{
    public const uint FT_KERNING_DEFAULT = 0;
}

internal static class FreeTypeRenderMode
{
    public const uint FT_RENDER_MODE_NORMAL = 0;
    public const uint FT_RENDER_MODE_LIGHT = 1;
    public const uint FT_RENDER_MODE_LCD = 3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_GlyphRec
{
    public nint library;
    public nint clazz;
    public uint format;
    public FT_Vector advance;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_BitmapGlyphRec
{
    public FT_GlyphRec root;
    public int left;
    public int top;
    public FT_Bitmap bitmap;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Vector
{
    public nint x;
    public nint y;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_Bitmap
{
    public uint rows;
    public uint width;
    public int pitch;
    public byte* buffer;
    public ushort num_grays;
    public byte pixel_mode;
    public byte palette_mode;
    public void* palette;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Glyph_Metrics
{
    public nint width;
    public nint height;
    public nint horiBearingX;
    public nint horiBearingY;
    public nint horiAdvance;
    public nint vertBearingX;
    public nint vertBearingY;
    public nint vertAdvance;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_GlyphSlotRec
{
    public nint library;
    public nint face;
    public nint next;
    public uint glyph_index;
    public nint genericData;
    public nint genericFinalizer;
    public FT_Glyph_Metrics metrics;
    public nint linearHoriAdvance;
    public nint linearVertAdvance;
    public FT_Vector advance;
    public int format;
    public FT_Bitmap bitmap;
    public int bitmap_left;
    public int bitmap_top;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_FaceRec
{
    public nint num_faces;
    public nint face_index;
    public nint face_flags;
    public nint style_flags;
    public nint num_glyphs;
    public byte* family_name;
    public byte* style_name;
    public int num_fixed_sizes;
    public nint available_sizes;
    public int num_charmaps;
    public nint charmaps;
    public nint genericData;
    public nint genericFinalizer;
    public FT_BBox bbox;
    public ushort units_per_EM;
    public short ascender;
    public short descender;
    public short height;
    public short max_advance_width;
    public short max_advance_height;
    public short underline_position;
    public short underline_thickness;
    public nint glyph; // FT_GlyphSlot
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_BBox
{
    public nint xMin;
    public nint yMin;
    public nint xMax;
    public nint yMax;
}
