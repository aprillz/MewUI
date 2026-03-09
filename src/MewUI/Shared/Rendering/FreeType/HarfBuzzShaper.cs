using Aprillz.MewUI.Native.HarfBuzz;

using HB = Aprillz.MewUI.Native.HarfBuzz.HarfBuzz;

namespace Aprillz.MewUI.Rendering.FreeType;

internal readonly record struct ShapedGlyph(
    uint GlyphId,
    int XAdvance26_6,
    int YAdvance26_6,
    int XOffset26_6,
    int YOffset26_6,
    uint Cluster);

internal static unsafe class HarfBuzzShaper
{
    [ThreadStatic]
    private static nint t_buffer;

    /// <summary>
    /// Shapes <paramref name="text"/> using HarfBuzz and returns positioned glyphs.
    /// Returns <c>null</c> if HarfBuzz is unavailable or the font has no hb_font.
    /// </summary>
    public static ShapedGlyph[]? Shape(ReadOnlySpan<char> text, FreeTypeFaceCache.FaceEntry face)
    {
        if (text.IsEmpty)
        {
            return [];
        }

        nint hbFont = face.GetOrCreateHbFont();
        if (hbFont == 0)
        {
            return null;
        }

        nint buf = GetBuffer();
        HB.hb_buffer_reset(buf);

        fixed (char* ptr = text)
        {
            HB.hb_buffer_add_utf16(buf, (ushort*)ptr, text.Length, 0, text.Length);
        }

        HB.hb_buffer_guess_segment_properties(buf);

        lock (face.SyncRoot)
        {
            HB.hb_shape(hbFont, buf, 0, 0);
        }

        uint len;
        var infos = HB.hb_buffer_get_glyph_infos(buf, out len);
        var positions = HB.hb_buffer_get_glyph_positions(buf, out _);

        if (len == 0 || infos == null || positions == null)
        {
            return [];
        }

        var result = new ShapedGlyph[len];
        for (uint i = 0; i < len; i++)
        {
            result[i] = new ShapedGlyph(
                infos[i].codepoint,
                positions[i].x_advance,
                positions[i].y_advance,
                positions[i].x_offset,
                positions[i].y_offset,
                infos[i].cluster);
        }

        return result;
    }

    private static nint GetBuffer()
    {
        if (t_buffer == 0)
        {
            t_buffer = HB.hb_buffer_create();
        }

        return t_buffer;
    }
}
