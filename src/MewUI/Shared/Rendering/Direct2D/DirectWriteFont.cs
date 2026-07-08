using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Resources;
namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe partial class DirectWriteFont : FontBase, IGlyphOutlineFont
{
    /// <summary>
    /// Non-zero DWrite custom font collection for private fonts.
    /// Stored so CreateTextFormat can use it.
    /// </summary>
    internal nint PrivateFontCollection { get; private set; }

    // Cache raw metrics per (family, weight, italic, isPrivate) - size-independent.
    // Avoids repeated COM calls (FindFamilyName → GetFontFamily → GetFirstMatchingFont → GetMetrics).
    private static readonly ConcurrentDictionary<(string family, FontWeight weight, bool italic, bool isPrivate), DWRITE_FONT_METRICS?> _metricsCache = new();

    // Native DWrite resources retained for the lifetime of this font for outline
    // extraction (IDWriteFontFace::GetGlyphRunOutline). Cached lazily on first use.
    private readonly nint _dwriteFactoryHandle;
    private nint _cachedFontFace;
    private bool _faceLookupAttempted;

    public DirectWriteFont(string family, double size, FontWeight weight, bool italic,
        bool underline, bool strikethrough, nint dwriteFactory, nint privateFontCollection = 0, uint outlineDpi = 96)
        : base(ValidateFamilyName(family), size, weight, italic, underline, strikethrough)
    {
        _dwriteFactoryHandle = dwriteFactory;
        if (dwriteFactory == 0 || size <= 0)
        {
            return;
        }

        PrivateFontCollection = privateFontCollection;

        var resolvedFamily = Family;
        var cacheKey = (resolvedFamily, weight, italic, isPrivate: privateFontCollection != 0);

        if (_metricsCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.HasValue)
            {
                ApplyMetrics(cached.Value, size);
            }

            return;
        }

        // Not cached - do the full COM lookup
        var factory = (IDWriteFactory*)dwriteFactory;
        DWRITE_FONT_METRICS? metrics = null;

        if (privateFontCollection != 0)
        {
            metrics = LoadMetricsFromCollection(factory, privateFontCollection, resolvedFamily, weight, italic);
        }

        metrics ??= LoadMetricsFromCollection(factory, 0, resolvedFamily, weight, italic);

        if (metrics == null)
        {
            var resolved = FontRegistry.Resolve(resolvedFamily);
            if (resolved != null)
            {
                metrics = LoadMetricsFromFile(factory, resolved.Value.FilePath);
            }
        }

        _metricsCache[cacheKey] = metrics;

        if (metrics.HasValue)
        {
            ApplyMetrics(metrics.Value, size);
        }
    }

    private static string ValidateFamilyName(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("Font family must be provided by the caller.", nameof(family));
        }

        return family.Trim();
    }

    private static DWRITE_FONT_METRICS? LoadMetricsFromCollection(IDWriteFactory* factory, nint fontCollection,
        string family, FontWeight weight, bool italic)
    {
        nint collection = fontCollection, fontFamily = 0, dwriteFont = 0;
        bool ownCollection = false;
        try
        {
            if (collection == 0)
            {
                int hr2 = DWriteVTable.GetSystemFontCollection(factory, out collection, checkForUpdates: false);
                if (hr2 < 0 || collection == 0)
                {
                    return null;
                }

                ownCollection = true;
            }

            int hr = DWriteVTable.FindFamilyName(collection, family, out uint familyIndex, out int exists);
            if (hr < 0 || exists == 0)
            {
                return null;
            }

            hr = DWriteVTable.GetFontFamily(collection, familyIndex, out fontFamily);
            if (hr < 0 || fontFamily == 0)
            {
                return null;
            }

            var dwWeight = (DWRITE_FONT_WEIGHT)(uint)weight;
            var dwStyle = italic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            hr = DWriteVTable.GetFirstMatchingFont(fontFamily, dwWeight,
                DWRITE_FONT_STRETCH.NORMAL, dwStyle, out dwriteFont);
            if (hr < 0 || dwriteFont == 0)
            {
                return null;
            }

            DWriteVTable.GetFontMetrics(dwriteFont, out DWRITE_FONT_METRICS metrics);
            return metrics;
        }
        finally
        {
            ComHelpers.Release(dwriteFont);
            ComHelpers.Release(fontFamily);
            if (ownCollection)
            {
                ComHelpers.Release(collection);
            }
        }
    }

    private static DWRITE_FONT_METRICS? LoadMetricsFromFile(IDWriteFactory* factory, string filePath)
    {
        nint fontFile = 0, fontFace = 0;
        try
        {
            int hr = DWriteVTable.CreateFontFileReference(factory, filePath, out fontFile);
            if (hr < 0 || fontFile == 0)
            {
                return null;
            }

            var faceType = filePath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                ? DWRITE_FONT_FACE_TYPE.CFF
                : DWRITE_FONT_FACE_TYPE.TRUETYPE;

            hr = DWriteVTable.CreateFontFace(factory, faceType, fontFile, 0,
                DWRITE_FONT_SIMULATIONS.NONE, out fontFace);
            if (hr < 0 || fontFace == 0)
            {
                faceType = faceType == DWRITE_FONT_FACE_TYPE.CFF
                    ? DWRITE_FONT_FACE_TYPE.TRUETYPE
                    : DWRITE_FONT_FACE_TYPE.CFF;
                hr = DWriteVTable.CreateFontFace(factory, faceType, fontFile, 0,
                    DWRITE_FONT_SIMULATIONS.NONE, out fontFace);
                if (hr < 0 || fontFace == 0)
                {
                    return null;
                }
            }

            DWriteVTable.GetFontFaceMetrics(fontFace, out DWRITE_FONT_METRICS metrics);
            return metrics;
        }
        finally
        {
            ComHelpers.Release(fontFace);
            ComHelpers.Release(fontFile);
        }
    }

    private void ApplyMetrics(DWRITE_FONT_METRICS metrics, double size)
    {
        if (metrics.designUnitsPerEm == 0)
        {
            return;
        }

        double scale = size / metrics.designUnitsPerEm;
        Ascent = metrics.ascent * scale;
        Descent = metrics.descent * scale;
        double leading = (metrics.ascent + metrics.descent + metrics.lineGap
            - metrics.designUnitsPerEm) * scale;
        InternalLeading = Math.Max(0, leading);
        CapHeight = metrics.capHeight > 0 ? metrics.capHeight * scale : Ascent * 0.7;
    }

    public unsafe bool TryAppendGlyphOutline(PathGeometry path, char ch, Point baselineOrigin, out double advance)
    {
        advance = 0;
        if (path is null || Size <= 0)
        {
            return false;
        }

        nint face = ResolveFontFace();
        if (face == 0) return false;

        uint codePoint = ch;
        ushort glyphIndex = 0;
        int hr = DWriteVTable.GetGlyphIndices(face, &codePoint, 1, &glyphIndex);
        if (hr < 0 || glyphIndex == 0) return false;

        // Compute design-units-per-em for advance scaling. Use the cached face's metrics.
        DWriteVTable.GetFontFaceMetrics(face, out var fm);
        if (fm.designUnitsPerEm == 0)
        {
            return false;
        }

        DWRITE_GLYPH_METRICS gm;
        hr = DWriteVTable.GetDesignGlyphMetrics(face, &glyphIndex, 1, &gm, 0);
        if (hr >= 0)
        {
            advance = (double)gm.advanceWidth * Size / fm.designUnitsPerEm;
        }

        nint sink = DWriteGeometrySink.Create(path, baselineOrigin.X, baselineOrigin.Y);
        try
        {
            // emSize = font size in DIPs. DWrite emits outline coords directly in DIPs
            // (no DPI scaling, no hinting grid-fit) - sink handles the Y-flip into SVG
            // top-down screen coords against the supplied baseline origin.
            hr = DWriteVTable.GetGlyphRunOutline(
                face,
                (float)Size,
                &glyphIndex,
                null,    // glyphAdvances - null = use natural advances (we don't need them since we pass advance back manually)
                null,    // glyphOffsets
                1,
                isSideways: 0,
                isRightToLeft: 0,
                sink);
        }
        finally
        {
            DWriteGeometrySink.Destroy(sink);
        }

        return hr >= 0;
    }

    /// <summary>Lazily resolves and caches an <c>IDWriteFontFace</c> for this font's
    /// (family, weight, italic). Lookup goes through the private collection (if any)
    /// first, then the system collection, then a sans-serif fallback if the requested
    /// family isn't installed (e.g. SVG specifies an Apple-only "Optima" on Windows).
    /// Returns 0 on failure; result is cached so repeated calls don't re-walk COM.</summary>
    private unsafe nint ResolveFontFace()
    {
        if (_faceLookupAttempted)
        {
            return _cachedFontFace;
        }
        _faceLookupAttempted = true;

        if (_dwriteFactoryHandle == 0)
        {
            return 0;
        }

        var factory = (IDWriteFactory*)_dwriteFactoryHandle;
        nint face = TryCreateFontFace(factory, PrivateFontCollection, Family);
        if (face == 0)
        {
            face = TryCreateFontFace(factory, 0, Family);
        }
        if (face == 0)
        {
            // Family not installed - fall back to sans-serif so glyphs still render
            // instead of dropping the whole text element. Refresh metrics so callers
            // (cursor advance, baseline) match the substituted face.
            face = TryCreateFontFace(factory, 0, "Segoe UI");
            if (face != 0)
            {
                DWriteVTable.GetFontFaceMetrics(face, out var fmFallback);
                if (fmFallback.designUnitsPerEm > 0)
                {
                    ApplyMetrics(fmFallback, Size);
                }
            }
        }
        _cachedFontFace = face;
        return face;
    }

    private unsafe nint TryCreateFontFace(IDWriteFactory* factory, nint fontCollection, string familyName)
    {
        nint collection = fontCollection;
        bool ownCollection = false;
        nint fontFamily = 0;
        nint dwriteFont = 0;
        try
        {
            if (collection == 0)
            {
                int hr = DWriteVTable.GetSystemFontCollection(factory, out collection, checkForUpdates: false);
                if (hr < 0 || collection == 0)
                {
                    return 0;
                }
                ownCollection = true;
            }

            int hr2 = DWriteVTable.FindFamilyName(collection, familyName, out uint familyIndex, out int exists);
            if (hr2 < 0 || exists == 0)
            {
                return 0;
            }

            hr2 = DWriteVTable.GetFontFamily(collection, familyIndex, out fontFamily);
            if (hr2 < 0 || fontFamily == 0)
            {
                return 0;
            }

            var dwWeight = (DWRITE_FONT_WEIGHT)(uint)Weight;
            var dwStyle = IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            hr2 = DWriteVTable.GetFirstMatchingFont(fontFamily, dwWeight,
                DWRITE_FONT_STRETCH.NORMAL, dwStyle, out dwriteFont);
            if (hr2 < 0 || dwriteFont == 0)
            {
                return 0;
            }

            hr2 = DWriteVTable.CreateFontFace(dwriteFont, out nint face);
            if (hr2 < 0 || face == 0)
            {
                return 0;
            }
            return face;
        }
        finally
        {
            ComHelpers.Release(dwriteFont);
            ComHelpers.Release(fontFamily);
            if (ownCollection)
            {
                ComHelpers.Release(collection);
            }
        }
    }

    private static partial class Win32GlyphOutline
    {
        public static unsafe bool TryAppendGlyphOutline(
            PathGeometry path,
            string family,
            double size,
            FontWeight weight,
            bool italic,
            bool underline,
            bool strikethrough,
            uint dpi,
            char ch,
            Point baselineOrigin,
            out double advance)
        {
            advance = 0;
            if (path is null || string.IsNullOrWhiteSpace(family) || size <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' SKIP path=null:{path is null} family='{family}' size={size}");
                return false;
            }

            int height = -(int)Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero);
            // ANSI_CHARSET (not DEFAULT_CHARSET): on non-English OS locales (e.g. Korean,
            // Japanese), DEFAULT_CHARSET makes GDI pick the locale's charset variant of
            // the requested family. Segoe UI's HANGUL_CHARSET (129) variant routes through
            // a substituted/linked Korean font whose outline path returns
            // ERROR_INVALID_DATATYPE (1003) for ASCII characters. Forcing ANSI_CHARSET
            // pins selection to the canonical Latin variant which exposes outlines.
            nint font = Gdi32.CreateFont(
                height,
                0, 0, 0,
                (int)weight,
                italic ? 1u : 0u,
                underline ? 1u : 0u,
                strikethrough ? 1u : 0u,
                GdiConstants.ANSI_CHARSET,
                GdiConstants.OUT_TT_PRECIS,
                GdiConstants.CLIP_DEFAULT_PRECIS,
                GdiConstants.CLEARTYPE_QUALITY,
                GdiConstants.DEFAULT_PITCH | GdiConstants.FF_DONTCARE,
                family);

            if (font == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' CreateFont FAIL family='{family}' height={height}");
                return false;
            }

            nint dc = 0;
            nint oldObject = 0;
            nint screenDc = 0;
            try
            {
                // CreateCompatibleDC(0) returns a memory DC compatible with whatever the
                // calling thread's "default" device is - on a worker thread without a UI
                // context this can produce a DC whose RASTERCAPS lack outline support, and
                // GetGlyphOutlineW returns ERROR_INVALID_DATATYPE (1003). Anchoring on the
                // desktop DC guarantees a screen-compatible memory DC.
                screenDc = User32.GetDC(0);
                dc = Gdi32.CreateCompatibleDC(screenDc);
                if (dc == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' CreateCompatibleDC FAIL");
                    return false;
                }

                oldObject = Gdi32.SelectObject(dc, font);
                // Force GDI to fully realize the selected font on this DC. Without this,
                // GetGlyphOutlineW on some configurations sees the font as not-yet-realized
                // and refuses outline extraction (also returning ERROR_INVALID_DATATYPE).
                bool tmOk = Gdi32.GetTextMetrics(dc, out var tm);
                var faceBuf = new char[64];
                int faceLen = Gdi32.GetTextFace(dc, faceBuf.Length, faceBuf);
                string actualFace = faceLen > 0 ? new string(faceBuf, 0, faceLen - 1) : "<?>";
                System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' selected face='{actualFace}' tmOk={tmOk} tmPitchAndFamily=0x{tm.tmPitchAndFamily:X} tmCharSet={tm.tmCharSet} oldObject={(oldObject == 0 ? "0(FAIL)" : "OK")}");

                var matrix = MAT2.Identity;
                GLYPHMETRICS metrics;
                uint glyph = ch;
                uint format = GdiConstants.GGO_NATIVE;
                uint sizeBytes = GetGlyphOutlineW(dc, glyph, format, &metrics, 0, null, &matrix);
                int err1 = sizeBytes == 0xFFFFFFFF ? Marshal.GetLastWin32Error() : 0;
                if (sizeBytes == 0xFFFFFFFF)
                {
                    format = GdiConstants.GGO_BEZIER;
                    sizeBytes = GetGlyphOutlineW(dc, glyph, format, &metrics, 0, null, &matrix);
                    int err1b = sizeBytes == 0xFFFFFFFF ? Marshal.GetLastWin32Error() : 0;
                    System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' GGO_NATIVE→GGO_BEZIER fallback lastErr1={err1} bezSize={sizeBytes:X8} lastErr1b={err1b}");
                }

                advance = metrics.gmCellIncX * (96.0 / dpi);
                if (sizeBytes == 0xFFFFFFFF || sizeBytes == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' probe FAIL sizeBytes={sizeBytes:X8} adv={advance:F2} font=0x{font:X} dc=0x{dc:X} height={height} format={format}");
                    return sizeBytes != 0xFFFFFFFF;
                }

                var buffer = new byte[sizeBytes];
                fixed (byte* bufferPtr = buffer)
                {
                    uint result = GetGlyphOutlineW(dc, glyph, format, &metrics, sizeBytes, bufferPtr, &matrix);
                    if (result == 0xFFFFFFFF)
                    {
                        int err2 = Marshal.GetLastWin32Error();
                        advance = metrics.gmCellIncX * (96.0 / dpi);
                        System.Diagnostics.Debug.WriteLine($"[Win32GlyphOutline] '{ch}' 2nd-call FAIL lastErr={err2} format={format}");
                        return false;
                    }
                }

                ParseGlyphOutline(path, buffer, baselineOrigin, dpi);
                return true;
            }
            finally
            {
                if (dc != 0)
                {
                    if (oldObject != 0)
                    {
                        Gdi32.SelectObject(dc, oldObject);
                    }

                    Gdi32.DeleteDC(dc);
                }

                if (screenDc != 0)
                {
                    User32.ReleaseDC(0, screenDc);
                }

                Gdi32.DeleteObject(font);
            }
        }

        private static void ParseGlyphOutline(PathGeometry path, byte[] buffer, Point baselineOrigin, uint dpi)
        {
            var data = buffer.AsSpan();
            int offset = 0;

            while (offset < data.Length)
            {
                int contourSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                int contourEnd = offset + contourSize;
                var start = ReadPointFx(data, offset + 8);
                path.MoveTo(ToWorldX(start.X, baselineOrigin.X, dpi), ToWorldY(start.Y, baselineOrigin.Y, dpi));

                int curveOffset = offset + 16;
                while (curveOffset < contourEnd)
                {
                    ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(curveOffset, 2));
                    ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(curveOffset + 2, 2));
                    int pointsOffset = curveOffset + 4;

                    switch (type)
                    {
                        case GdiConstants.TT_PRIM_LINE:
                            for (int i = 0; i < count; i++)
                            {
                                var point = ReadPointFx(data, pointsOffset + (i * 8));
                                path.LineTo(ToWorldX(point.X, baselineOrigin.X, dpi), ToWorldY(point.Y, baselineOrigin.Y, dpi));
                            }
                            break;

                        case GdiConstants.TT_PRIM_QSPLINE:
                            for (int i = 0; i < count - 1; i++)
                            {
                                var control = ReadPointFx(data, pointsOffset + (i * 8));
                                var end = i < count - 2
                                    ? Midpoint(control, ReadPointFx(data, pointsOffset + ((i + 1) * 8)))
                                    : ReadPointFx(data, pointsOffset + ((count - 1) * 8));

                                path.QuadTo(
                                    ToWorldX(control.X, baselineOrigin.X, dpi), ToWorldY(control.Y, baselineOrigin.Y, dpi),
                                    ToWorldX(end.X, baselineOrigin.X, dpi), ToWorldY(end.Y, baselineOrigin.Y, dpi));
                            }
                            break;

                        case GdiConstants.TT_PRIM_CSPLINE:
                            for (int i = 0; i + 2 < count; i += 3)
                            {
                                var c1 = ReadPointFx(data, pointsOffset + (i * 8));
                                var c2 = ReadPointFx(data, pointsOffset + ((i + 1) * 8));
                                var end = ReadPointFx(data, pointsOffset + ((i + 2) * 8));
                                path.BezierTo(
                                    ToWorldX(c1.X, baselineOrigin.X, dpi), ToWorldY(c1.Y, baselineOrigin.Y, dpi),
                                    ToWorldX(c2.X, baselineOrigin.X, dpi), ToWorldY(c2.Y, baselineOrigin.Y, dpi),
                                    ToWorldX(end.X, baselineOrigin.X, dpi), ToWorldY(end.Y, baselineOrigin.Y, dpi));
                            }
                            break;
                    }

                    curveOffset += 4 + (count * 8);
                }

                path.Close();
                offset = contourEnd;
            }
        }

        private static double ToWorldX(double x, double baselineX, uint dpi) => baselineX + (x * (96.0 / dpi));
        private static double ToWorldY(double y, double baselineY, uint dpi) => baselineY - (y * (96.0 / dpi));
        private static PointFx Midpoint(PointFx a, PointFx b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
        private static PointFx ReadPointFx(ReadOnlySpan<byte> data, int offset)
            => new(ReadFixed(data, offset), ReadFixed(data, offset + 4));

        private static double ReadFixed(ReadOnlySpan<byte> data, int offset)
        {
            // FIXED layout in the GDI outline buffer is { WORD fract; SHORT value; } -
            // fract first (matches the corrected struct definition above).
            ushort fraction = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            short value = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 2, 2));
            return value + (fraction / 65536.0);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FIXED
        {
            // Per MS docs the on-wire layout is { WORD fract; SHORT value; } - fract FIRST.
            // Reversing this makes MAT2.Identity decode as ~0 in GDI's eyes and
            // GetGlyphOutlineW returns ERROR_INVALID_DATATYPE (1003).
            public ushort fract;
            public short value;

            public static FIXED One => new() { value = 1, fract = 0 };
            public static FIXED Zero => default;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MAT2
        {
            public FIXED eM11;
            public FIXED eM12;
            public FIXED eM21;
            public FIXED eM22;

            public static MAT2 Identity => new()
            {
                eM11 = FIXED.One,
                eM12 = FIXED.Zero,
                eM21 = FIXED.Zero,
                eM22 = FIXED.One
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GLYPHMETRICS
        {
            public uint gmBlackBoxX;
            public uint gmBlackBoxY;
            public POINT gmptGlyphOrigin;
            public short gmCellIncX;
            public short gmCellIncY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct PointFx(double x, double y)
        {
            public double X { get; } = x;
            public double Y { get; } = y;
        }

        [LibraryImport("gdi32.dll", EntryPoint = "GetGlyphOutlineW", SetLastError = true)]
        private static unsafe partial uint GetGlyphOutlineW(
            nint hdc,
            uint uChar,
            uint uFormat,
            GLYPHMETRICS* lpgm,
            uint cbBuffer,
            byte* lpvBuffer,
            MAT2* lpmat2);
    }
}
