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
    public static partial int FT_Select_Size(nint face, int strike_index);

    [LibraryImport(LibraryName)]
    public static partial int FT_Load_Char(nint face, uint char_code, int load_flags);

    [LibraryImport(LibraryName)]
    public static partial int FT_Load_Glyph(nint face, uint glyph_index, int load_flags);

    [LibraryImport(LibraryName)]
    public static unsafe partial int FT_Outline_Decompose(FT_Outline* outline, FT_Outline_Funcs* funcs, nint user);

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

    // Variable font (Multiple Masters) API.
    [LibraryImport(LibraryName)]
    public static partial int FT_Get_MM_Var(nint face, out nint amaster);

    [LibraryImport(LibraryName)]
    public static partial int FT_Done_MM_Var(nint library, nint amaster);

    [LibraryImport(LibraryName)]
    public static unsafe partial int FT_Set_Var_Design_Coordinates(nint face, uint num_coords, nint coords);

    /// <summary>
    /// FT_HAS_COLOR macro: checks if the face has color glyph tables (CBDT/CBLC, COLR, SVG).
    /// </summary>
    public static unsafe bool FT_HAS_COLOR(nint face)
    {
        if (face == 0) return false;
        var faceRec = (FT_FaceRec*)face;
        return ((long)faceRec->face_flags & FreeTypeFaceFlags.FT_FACE_FLAG_COLOR) != 0;
    }
}

internal static class FreeTypeLoad
{
    public const int FT_LOAD_DEFAULT = 0x0;
    public const int FT_LOAD_RENDER = 0x4;
    public const int FT_LOAD_NO_BITMAP = 0x8;
    public const int FT_LOAD_TARGET_NORMAL = 0x0;
    // FT_LOAD_TARGET_XXX flags are (FT_Render_Mode_XXX << 16).
    // See FreeType: FT_LOAD_TARGET( x ) macro.
    public const int FT_LOAD_TARGET_LIGHT = 0x1 << 16;
    public const int FT_LOAD_TARGET_LCD = 0x3 << 16;
    public const int FT_LOAD_FORCE_AUTOHINT = 0x20;
    public const int FT_LOAD_COLOR = 1 << 20; // FT_LOAD_COLOR - load color bitmaps (CBDT/CBLC, COLR)
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
    public FT_Outline outline;
}

/// <summary>FT_Bitmap_Size - metrics for a fixed-size bitmap strike.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FT_Bitmap_Size
{
    public short height;
    public short width;
    public nint size;   // 26.6 fixed
    public nint x_ppem; // 26.6 fixed
    public nint y_ppem; // 26.6 fixed
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
    public nint size;  // FT_Size
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_BBox
{
    public nint xMin;
    public nint yMin;
    public nint xMax;
    public nint yMax;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_Outline
{
    public short n_contours;
    public short n_points;
    public FT_Vector* points;
    public byte* tags;
    public short* contours;
    public int flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_Outline_Funcs
{
    public delegate* unmanaged[Cdecl]<FT_Vector*, nint, int> move_to;
    public delegate* unmanaged[Cdecl]<FT_Vector*, nint, int> line_to;
    public delegate* unmanaged[Cdecl]<FT_Vector*, FT_Vector*, nint, int> conic_to;
    public delegate* unmanaged[Cdecl]<FT_Vector*, FT_Vector*, FT_Vector*, nint, int> cubic_to;
    public int shift;
    public nint delta;
}

/// <summary>FT_Size_Metrics - scaled font metrics from FT_Size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FT_Size_Metrics
{
    public ushort x_ppem;
    public ushort y_ppem;
    // Padding to align FT_Fixed (long / nint) on natural boundary.
    private readonly uint _pad;
    public nint x_scale;    // FT_Fixed 16.16
    public nint y_scale;    // FT_Fixed 16.16
    public nint ascender;   // FT_Pos 26.6
    public nint descender;  // FT_Pos 26.6
    public nint height;     // FT_Pos 26.6
    public nint max_advance; // FT_Pos 26.6
}

/// <summary>FT_SizeRec - represents a font size object.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FT_SizeRec
{
    public nint face;
    public nint genericData;
    public nint genericFinalizer;
    public FT_Size_Metrics metrics;
}

/// <summary>FT_Var_Axis - describes one variation axis of a variable font.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FT_Var_Axis
{
    public nint name;       // FT_String* (char*)
    public nint minimum;    // FT_Fixed 16.16
    public nint def;        // FT_Fixed 16.16
    public nint maximum;    // FT_Fixed 16.16
    public uint tag;        // FT_ULong - axis tag, e.g. 'wght'
    public uint strid;      // FT_UInt  - name string ID
}

/// <summary>FT_MM_Var - describes variable font axes and named styles.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_MM_Var
{
    public uint num_axis;
    public uint num_designs;
    public uint num_namedstyles;
    public FT_Var_Axis* axis;
    // ... remaining fields omitted (namedstyle, etc.)
}

internal static class FreeTypeFaceFlags
{
    public const long FT_FACE_FLAG_MULTIPLE_MASTERS = 1L << 8;
    public const long FT_FACE_FLAG_COLOR = 1L << 14;
}

internal static class FreeTypeVarAxisTags
{
    // 'wght' = 0x77676874
    public const uint WGHT = ((uint)'w' << 24) | ((uint)'g' << 16) | ((uint)'h' << 8) | (uint)'t';
    // 'ital' = 0x6974616C
    public const uint ITAL = ((uint)'i' << 24) | ((uint)'t' << 16) | ((uint)'a' << 8) | (uint)'l';
    // 'slnt' = 0x736C6E74
    public const uint SLNT = ((uint)'s' << 24) | ((uint)'l' << 16) | ((uint)'n' << 8) | (uint)'t';
}
