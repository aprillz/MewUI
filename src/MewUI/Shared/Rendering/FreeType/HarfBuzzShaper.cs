using System.Runtime.CompilerServices;

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
    // Recent shapings by face and text, so a run that is measured and then drawn, or drawn again
    // unchanged, is shaped once. Direct-mapped by hash: a colliding run replaces the one in its slot.
    private const int SHAPE_CACHE_SIZE = 256;
    // Longer text is mostly a paragraph measured prefix by prefix while wrapping, which would only churn the cache.
    private const int MAX_CACHED_TEXT_LENGTH = 64;

    [ThreadStatic]
    private static nint t_buffer;

    [ThreadStatic]
    private static ShapeCacheEntry[]? _shapeCache;

    /// <summary>
    /// Shapes <paramref name="text"/> using HarfBuzz and returns positioned glyphs, which may be shared
    /// with other callers and must not be modified.
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

        ShapeCacheEntry[]? cache = null;
        int hash = 0;
        int slot = 0;
        if (text.Length <= MAX_CACHED_TEXT_LENGTH)
        {
            hash = HashCode.Combine(string.GetHashCode(text), RuntimeHelpers.GetHashCode(face));
            cache = _shapeCache ??= new ShapeCacheEntry[SHAPE_CACHE_SIZE];
            slot = hash & (SHAPE_CACHE_SIZE - 1);
            var cached = cache[slot];
            if (cached.Hash == hash && ReferenceEquals(cached.Face, face) && text.SequenceEqual(cached.Text))
            {
                return cached.Glyphs;
            }
        }

        var result = ShapeUncached(text, hbFont, face);
        if (cache != null)
        {
            cache[slot] = new ShapeCacheEntry(hash, face, text.ToString(), result);
        }

        return result;
    }

    private static ShapedGlyph[] ShapeUncached(ReadOnlySpan<char> text, nint hbFont, FreeTypeFaceCache.FaceEntry face)
    {
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

    private readonly record struct ShapeCacheEntry(int Hash, FreeTypeFaceCache.FaceEntry? Face, string? Text, ShapedGlyph[] Glyphs);
}
