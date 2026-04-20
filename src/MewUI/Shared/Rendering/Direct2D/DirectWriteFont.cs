using System.Collections.Concurrent;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class DirectWriteFont : FontBase
{
    /// <summary>
    /// Non-zero DWrite custom font collection for private fonts.
    /// Stored so CreateTextFormat can use it.
    /// </summary>
    internal nint PrivateFontCollection { get; private set; }

    // Cache raw metrics per (family, weight, italic, isPrivate) — size-independent.
    // Avoids repeated COM calls (FindFamilyName → GetFontFamily → GetFirstMatchingFont → GetMetrics).
    private static readonly ConcurrentDictionary<(string family, FontWeight weight, bool italic, bool isPrivate), DWRITE_FONT_METRICS?> _metricsCache = new();

    public DirectWriteFont(string family, double size, FontWeight weight, bool italic,
        bool underline, bool strikethrough, nint dwriteFactory, nint privateFontCollection = 0)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        if (dwriteFactory == 0 || size <= 0)
        {
            return;
        }

        PrivateFontCollection = privateFontCollection;

        var cacheKey = (family, weight, italic, isPrivate: privateFontCollection != 0);

        if (_metricsCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.HasValue)
            {
                ApplyMetrics(cached.Value, size);
            }

            return;
        }

        // Not cached — do the full COM lookup
        var factory = (IDWriteFactory*)dwriteFactory;
        DWRITE_FONT_METRICS? metrics = null;

        if (privateFontCollection != 0)
        {
            metrics = LoadMetricsFromCollection(factory, privateFontCollection, family, weight, italic);
        }

        metrics ??= LoadMetricsFromCollection(factory, 0, family, weight, italic);

        if (metrics == null)
        {
            var resolved = FontRegistry.Resolve(family);
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
}
