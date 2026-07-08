using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Fontconfig;

/// <summary>
/// Minimal P/Invoke bindings for libfontconfig to query system fonts on Linux.
/// </summary>
internal static partial class Fontconfig
{
    private const string LibraryName = "libfontconfig.so.1";

    // FcInit - initialize fontconfig (idempotent).
    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FcInit();

    // FcPatternCreate
    [LibraryImport(LibraryName)]
    public static partial nint FcPatternCreate();

    // FcPatternDestroy
    [LibraryImport(LibraryName)]
    public static partial void FcPatternDestroy(nint pattern);

    // FcPatternAddString(pattern, object, value)
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FcPatternAddString(nint pattern, string obj, string value);

    // FcPatternAddInteger(pattern, object, value)
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FcPatternAddInteger(nint pattern, string obj, int value);

    // FcPatternGetString(pattern, object, n, &value)
    // Returns FcResultMatch (0) on success.
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FcPatternGetString(nint pattern, string obj, int n, out nint value);

    // FcConfigSubstitute(config, pattern, kind)
    // kind: FcMatchPattern = 0
    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FcConfigSubstitute(nint config, nint pattern, int kind);

    // FcDefaultSubstitute(pattern)
    [LibraryImport(LibraryName)]
    public static partial void FcDefaultSubstitute(nint pattern);

    // FcFontMatch(config, pattern, &result) -> FcPattern*
    [LibraryImport(LibraryName)]
    public static partial nint FcFontMatch(nint config, nint pattern, out int result);

    // Well-known property names.
    public const string FC_FAMILY = "family";
    public const string FC_STYLE = "style";
    public const string FC_FILE = "file";
    public const string FC_WEIGHT = "weight";
    public const string FC_SLANT = "slant";

    // FcWeight constants (fontconfig scale, NOT CSS).
    public const int FC_WEIGHT_THIN = 0;
    public const int FC_WEIGHT_LIGHT = 50;
    public const int FC_WEIGHT_REGULAR = 80;
    public const int FC_WEIGHT_MEDIUM = 100;
    public const int FC_WEIGHT_SEMIBOLD = 180;
    public const int FC_WEIGHT_BOLD = 200;
    public const int FC_WEIGHT_BLACK = 210;

    // FcSlant constants.
    public const int FC_SLANT_ROMAN = 0;
    public const int FC_SLANT_ITALIC = 100;
    public const int FC_SLANT_OBLIQUE = 110;

    // --- Charset API (for fallback font queries) ---

    // FcCharSetCreate
    [LibraryImport(LibraryName)]
    public static partial nint FcCharSetCreate();

    // FcCharSetAddChar(charset, ucs4) -> bool
    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FcCharSetAddChar(nint charset, uint ucs4);

    // FcCharSetDestroy(charset)
    [LibraryImport(LibraryName)]
    public static partial void FcCharSetDestroy(nint charset);

    // FcPatternAddCharSet(pattern, object, charset) -> bool
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FcPatternAddCharSet(nint pattern, string obj, nint charset);

    // --- FcFontSort ---

    // FcFontSort(config, pattern, trim, &charsets, &result) -> FcFontSet*
    [LibraryImport(LibraryName)]
    public static partial nint FcFontSort(nint config, nint pattern,
        [MarshalAs(UnmanagedType.Bool)] bool trim, out nint charsets, out int result);

    // FcFontSetDestroy(fontSet)
    [LibraryImport(LibraryName)]
    public static partial void FcFontSetDestroy(nint fontSet);

    // FcFontSet structure: { int nfont; int sfont; FcPattern** fonts; }
    public const string FC_CHARSET = "charset";

    // FcMatchPattern
    public const int FcMatchPattern = 0;

    // FcResultMatch
    public const int FcResultMatch = 0;
}
