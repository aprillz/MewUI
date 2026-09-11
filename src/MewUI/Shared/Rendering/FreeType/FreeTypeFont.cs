using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Aprillz.MewUI.Native.FreeType;
using Aprillz.MewUI.Text;
using FT = Aprillz.MewUI.Native.FreeType.FreeType;

namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed class FreeTypeFont : FontBase, IGlyphOutlineFont
{
    // OS/2 fsSelection bit 7: the font asks layout to use its typographic metrics.
    private const ushort USE_TYPO_METRICS = 1 << 7;

    public string FontPath { get; }
    public int PixelHeight { get; }

    public FreeTypeFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough, string fontPath, int pixelHeight)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        FontPath = fontPath;
        PixelHeight = pixelHeight;

        // Query metrics from FreeType face.
        try
        {
            var face = FreeTypeFaceCache.Instance.Get(fontPath, pixelHeight, weight, italic);
            lock (face.SyncRoot)
            {
                var metrics = FreeTypeFaceCache.GetSizeMetrics(face.Face);
                double ascentPx = (long)metrics.ascender / 64.0;
                double descentPx = -(long)metrics.descender / 64.0; // FreeType descender is negative
                double heightPx = (long)metrics.height / 64.0;
                double dpiScale = pixelHeight > 0 ? pixelHeight / size : 1.0;

                Ascent = ascentPx / dpiScale;
                Descent = descentPx / dpiScale;
                InternalLeading = Math.Max(0, (heightPx - ascentPx - descentPx) / dpiScale);
                CapHeight = ResolveCapHeight(face.Face, in metrics, dpiScale, Ascent);
                XHeight = ResolveXHeight(face.Face, dpiScale, CapHeight);
            }
        }
        catch
        {
            // Fallback: approximate from size.
            Ascent = size;
            Descent = size * 0.25;
            CapHeight = size * 0.7;
            XHeight = size * 0.5;
        }
    }

    private readonly object _glyphInkGate = new();
    private Dictionary<uint, GlyphInk>? _glyphInk;

    /// <summary>
    /// Ink of a single-line run that falls outside its advance box and the font's ascent/descent band,
    /// in device-independent units, from the hinted glyph metrics the rasterizer draws with. Zero for
    /// code points this face has no glyph for.
    /// </summary>
    internal unsafe TextInkOverhang GetRunInkOverhang(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || string.IsNullOrWhiteSpace(FontPath) || PixelHeight <= 0 || Size <= 0)
        {
            return TextInkOverhang.None;
        }

        FreeTypeFaceCache.FaceEntry face;
        try
        {
            face = FreeTypeFaceCache.Instance.Get(FontPath, PixelHeight, Weight, IsItalic);
        }
        catch
        {
            return TextInkOverhang.None;
        }

        double dpiScale = PixelHeight / Size;
        // Same rounding FreeTypeText uses to place the baseline in its bitmap.
        double baselinePx = Math.Max(1, Math.Round(Ascent * dpiScale));
        double descentPx = Descent * dpiScale;
        double left = 0;
        double right = 0;
        double above = 0;
        double below = 0;
        double advanceAfter = 0;
        // The right overhang is the furthest any glyph's ink reaches past the run's end, so a wide
        // italic followed by a narrow glyph still counts; the left one only comes from the first glyph.
        for (int index = text.Length - 1; index >= 0; index--)
        {
            uint code = text[index];
            if (char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1]))
            {
                code = (uint)char.ConvertToUtf32(text[index - 1], text[index]);
                index--;
            }

            var ink = GetGlyphInk(face, code);
            right = Math.Max(right, ink.Right - advanceAfter);
            above = Math.Max(above, ink.Top - baselinePx);
            below = Math.Max(below, ink.Bottom - descentPx);
            advanceAfter += ink.Advance;
            if (index == 0)
            {
                left = ink.Left;
            }
        }

        return TextInkOverhang.FromEdges(left / dpiScale, above / dpiScale, right / dpiScale, below / dpiScale);
    }

    private unsafe GlyphInk GetGlyphInk(FreeTypeFaceCache.FaceEntry face, uint code)
    {
        lock (_glyphInkGate)
        {
            _glyphInk ??= new Dictionary<uint, GlyphInk>();
            if (_glyphInk.TryGetValue(code, out var cached))
            {
                return cached;
            }
        }

        // A code point this face lacks is drawn from the fallback face the rasterizer picks, so its ink
        // comes from there too.
        var activeFace = face;
        uint glyph = face.GetGlyphIndex(code);
        if (glyph == 0 &&
            LinuxFontFallbackResolver.Resolve(code, PixelHeight, Weight, IsItalic) is FreeTypeFaceCache.FaceEntry fallbackFace)
        {
            activeFace = fallbackFace;
            glyph = fallbackFace.GetGlyphIndex(code);
        }

        var ink = default(GlyphInk);
        if (glyph != 0)
        {
            lock (activeFace.SyncRoot)
            {
                nint slotPointer =
                    FT.FT_Load_Glyph(activeFace.Face, glyph, FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT) == 0
                        ? activeFace.GetGlyphSlotPointer()
                        : 0;
                if (slotPointer != 0)
                {
                    var metrics = ((FT_GlyphSlotRec*)slotPointer)->metrics;
                    double bearingX = (long)metrics.horiBearingX / 64.0;
                    double bearingY = (long)metrics.horiBearingY / 64.0;
                    double width = (long)metrics.width / 64.0;
                    double height = (long)metrics.height / 64.0;
                    double advance = (long)metrics.horiAdvance / 64.0;
                    ink = new GlyphInk(-bearingX, bearingX + width - advance, bearingY, height - bearingY, advance);
                }
            }
        }

        lock (_glyphInkGate)
        {
            _glyphInk[code] = ink;
        }
        return ink;
    }

    // Pixel extents of one glyph's ink: past the pen origin on the left, past its advance on the right,
    // above and below the baseline.
    private readonly record struct GlyphInk(double Left, double Right, double Top, double Bottom, double Advance);

    /// <summary>
    /// Reads the ascent and descent the font asks line layout to use, in pixels at the active size.
    /// Returns false when the face carries no usable OS/2 table.
    /// </summary>
    private static unsafe bool TryGetLineMetrics(
        nint face,
        in FT_Size_Metrics metrics,
        out double ascentPx,
        out double descentPx)
    {
        ascentPx = 0;
        descentPx = 0;

        var os2 = (TT_OS2*)FT.FT_Get_Sfnt_Table(face, FreeTypeSfntTags.FT_SFNT_OS2);
        if (os2 == null || os2->version == ushort.MaxValue)
        {
            return false;
        }

        double ascentUnits;
        double descentUnits;
        if ((os2->fsSelection & USE_TYPO_METRICS) != 0)
        {
            // The font asks for its typographic metrics explicitly.
            ascentUnits = os2->sTypoAscender;
            descentUnits = -os2->sTypoDescender;
        }
        else
        {
            ascentUnits = os2->usWinAscent;
            descentUnits = os2->usWinDescent;
        }

        if (ascentUnits <= 0 || descentUnits < 0)
        {
            return false;
        }

        // y_scale maps font units to 26.6 pixels.
        double unitScale = (long)metrics.y_scale / 65536.0 / 64.0;
        ascentPx = ascentUnits * unitScale;
        descentPx = descentUnits * unitScale;
        return ascentPx > 0;
    }

    private static unsafe double ResolveCapHeight(
        nint face,
        in FT_Size_Metrics metrics,
        double dpiScale,
        double ascent)
    {
        // Loaded with the flags FreeTypeText rasterizes with, so the cap line sits where the glyphs are
        // drawn. The ascent and line height above come from the same grid-fitted metrics, which is what
        // lets a box trimmed to this cap line land on a whole pixel row.
        if (FT.FT_Load_Char(face, 'H', FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT) == 0)
        {
            var faceRec = (FT_FaceRec*)face;
            if (faceRec->glyph != 0)
            {
                var slot = (FT_GlyphSlotRec*)faceRec->glyph;
                double capHeightPx = (long)slot->metrics.horiBearingY / 64.0;
                if (capHeightPx > 0)
                {
                    return capHeightPx / dpiScale;
                }
            }
        }

        return ascent * 0.7;
    }

    private static unsafe double ResolveXHeight(nint face, double dpiScale, double capHeight)
    {
        if (FT.FT_Load_Char(face, 'x', FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT) == 0)
        {
            var faceRec = (FT_FaceRec*)face;
            if (faceRec->glyph != 0)
            {
                var slot = (FT_GlyphSlotRec*)faceRec->glyph;
                double xHeightPx = (long)slot->metrics.horiBearingY / 64.0;
                if (xHeightPx > 0)
                {
                    return xHeightPx / dpiScale;
                }
            }
        }

        return capHeight * 0.72;
    }

    public unsafe bool TryAppendGlyphOutline(PathGeometry path, char ch, Point baselineOrigin, out double advance)
    {
        advance = 0;
        if (path is null || string.IsNullOrWhiteSpace(FontPath) || PixelHeight <= 0)
        {
            return false;
        }

        try
        {
            var face = FreeTypeFaceCache.Instance.Get(FontPath, PixelHeight, Weight, IsItalic);
            lock (face.SyncRoot)
            {
                int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_NO_BITMAP;
                if (FT.FT_Load_Char(face.Face, ch, flags) != 0)
                {
                    return false;
                }

                var slotPtr = face.GetGlyphSlotPointer();
                if (slotPtr == 0)
                {
                    return false;
                }

                var slot = (FT_GlyphSlotRec*)slotPtr;
                if (slot->outline.n_contours <= 0 || slot->outline.points == null)
                {
                    return false;
                }

                double dipScale = Size / PixelHeight;
                advance = face.GetAdvancePx(ch) * dipScale;

                // baselineOrigin is the actual baseline (per IGlyphOutlineFont contract).
                // FreeType outlines are baseline-relative (Y up from baseline), so map by
                // negating Y onto SVG's top-down screen coords with no extra ascent shift.
                var state = new OutlineState(path, baselineOrigin, dipScale);
                var handle = GCHandle.Alloc(state);
                try
                {
                    var funcs = new FT_Outline_Funcs
                    {
                        move_to = &MoveToCallback,
                        line_to = &LineToCallback,
                        conic_to = &ConicToCallback,
                        cubic_to = &CubicToCallback,
                        shift = 0,
                        delta = 0
                    };

                    int err = FT.FT_Outline_Decompose(&slot->outline, &funcs, GCHandle.ToIntPtr(handle));
                    return err == 0;
                }
                finally
                {
                    handle.Free();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int MoveToCallback(FT_Vector* to, nint user)
    {
        var state = GetState(user);
        state.Path.MoveTo(state.ToWorldX((long)to->x), state.ToWorldY((long)to->y));
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int LineToCallback(FT_Vector* to, nint user)
    {
        var state = GetState(user);
        state.Path.LineTo(state.ToWorldX((long)to->x), state.ToWorldY((long)to->y));
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int ConicToCallback(FT_Vector* control, FT_Vector* to, nint user)
    {
        var state = GetState(user);
        state.Path.QuadTo(
            state.ToWorldX((long)control->x), state.ToWorldY((long)control->y),
            state.ToWorldX((long)to->x), state.ToWorldY((long)to->y));
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int CubicToCallback(FT_Vector* control1, FT_Vector* control2, FT_Vector* to, nint user)
    {
        var state = GetState(user);
        state.Path.BezierTo(
            state.ToWorldX((long)control1->x), state.ToWorldY((long)control1->y),
            state.ToWorldX((long)control2->x), state.ToWorldY((long)control2->y),
            state.ToWorldX((long)to->x), state.ToWorldY((long)to->y));
        return 0;
    }

    private static OutlineState GetState(nint user)
        => (OutlineState)GCHandle.FromIntPtr(user).Target!;

    private sealed class OutlineState(PathGeometry path, Point baselineOrigin, double dipScale)
    {
        public PathGeometry Path { get; } = path;

        public double ToWorldX(long x26_6) => baselineOrigin.X + ((x26_6 / 64.0) * dipScale);

        // FreeType y is baseline-relative (positive = above baseline). SVG screen coords
        // are top-down (positive = below baseline), so subtract from baseline world y.
        public double ToWorldY(long y26_6) => baselineOrigin.Y - ((y26_6 / 64.0) * dipScale);
    }
}
