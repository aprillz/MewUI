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

    public override void Dispose()
    {
        var fontFace = _cachedFontFace;
        _cachedFontFace = 0;
        _faceLookupAttempted = true;
        ComHelpers.Release(fontFace);
        base.Dispose();
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
}
