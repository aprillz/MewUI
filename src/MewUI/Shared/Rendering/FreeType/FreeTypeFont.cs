using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Aprillz.MewUI.Native.FreeType;
using FT = Aprillz.MewUI.Native.FreeType.FreeType;

namespace Aprillz.MewUI.Rendering.FreeType;

internal sealed class FreeTypeFont : FontBase, IGlyphOutlineFont
{
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
            }
        }
        catch
        {
            // Fallback: approximate from size.
            Ascent = size;
            Descent = size * 0.25;
            CapHeight = size * 0.7;
        }
    }

    private static unsafe double ResolveCapHeight(
        nint face,
        in FT_Size_Metrics metrics,
        double dpiScale,
        double ascent)
    {
        var os2 = (TT_OS2*)FT.FT_Get_Sfnt_Table(face, FreeTypeSfntTags.FT_SFNT_OS2);
        if (os2 != null && os2->version != ushort.MaxValue && os2->version >= 2 && os2->sCapHeight > 0)
        {
            // y_scale maps font units to 26.6 pixels. Preserve fractional precision instead
            // of using the integer-rounded FT_Size_Metrics ascender.
            double capHeightPx = os2->sCapHeight * (long)metrics.y_scale / 65536.0 / 64.0;
            if (capHeightPx > 0)
            {
                return capHeightPx / dpiScale;
            }
        }

        // Older and non-SFNT fonts have no sCapHeight. A flat-sided capital is a more faithful
        // cap-line probe than an ascent ratio and reflects the active size's hinting.
        if (FT.FT_Load_Char(face, 'H', FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_NO_BITMAP) == 0)
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
