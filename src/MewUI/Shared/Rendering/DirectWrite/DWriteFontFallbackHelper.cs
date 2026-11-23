using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.DirectWrite;

/// <summary>
/// Builds a custom IDWriteFontFallback that tries a font's listed families, then the user's
/// <see cref="FontFallback.FallbackChain"/>, then the system defaults.
/// Requires Windows 8.1+ (IDWriteFactory2).
/// </summary>
internal static unsafe class DWriteFontFallbackHelper
{
    private static readonly object _gate = new();

    // Keyed by the listed families; the empty key is the chain alone.
    private static readonly Dictionary<string, nint> _cached = new(StringComparer.Ordinal);
    private static long _cachedVersion = -1;

    // Common Unicode ranges for the fallback chain.
    // Each entry in the user chain is mapped to a broad range (BMP + supplementary).
    private static readonly DWRITE_UNICODE_RANGE[] BmpRange =
    [
        new() { first = 0x0000, last = 0xFFFF },
    ];

    private static readonly DWRITE_UNICODE_RANGE[] FullRange =
    [
        new() { first = 0x0000, last = 0x10FFFF },
    ];

    // Emoji blocks and the joiner, keycap and selectors their sequences use. A color font also carries
    // plain letters and digits, which must not be drawn from it just because it comes first in the chain.
    private static readonly DWRITE_UNICODE_RANGE[] EmojiRanges =
    [
        new() { first = 0x200D, last = 0x200D },
        new() { first = 0x203C, last = 0x203C },
        new() { first = 0x2049, last = 0x2049 },
        new() { first = 0x20E3, last = 0x20E3 },
        new() { first = 0x2122, last = 0x2122 },
        new() { first = 0x2139, last = 0x2139 },
        new() { first = 0x2190, last = 0x21FF },
        new() { first = 0x2300, last = 0x23FF },
        new() { first = 0x24C2, last = 0x24C2 },
        new() { first = 0x25A0, last = 0x27BF },
        new() { first = 0x2900, last = 0x297F },
        new() { first = 0x2B00, last = 0x2BFF },
        new() { first = 0x3030, last = 0x3030 },
        new() { first = 0x303D, last = 0x303D },
        new() { first = 0x3297, last = 0x3297 },
        new() { first = 0x3299, last = 0x3299 },
        new() { first = 0xFE00, last = 0xFE0F },
        new() { first = 0x1F000, last = 0x1FAFF },
        new() { first = 0xE0020, last = 0xE007F },
    ];

    /// <summary>
    /// Gets or creates the fallback for <paramref name="font"/>: its listed families and the user's chain ahead of the
    /// system defaults. Returns 0 when neither adds anything, or when IDWriteFactory2 is not available; the system
    /// default applies then. The returned pointer is cached and must NOT be released by the caller.
    /// </summary>
    public static nint GetOrCreate(IDWriteFactory* factory, DirectWriteFont? font = null)
    {
        var listed = font?.ListedFallbacks ?? [];
        var chain = FontFallback.GetChainSnapshot();
        if (listed.Length == 0 && chain.Length == 0)
        {
            return 0;
        }

        string key = font?.ListedFallbackKey ?? string.Empty;
        lock (_gate)
        {
            // A registered family's collection changes when it gains a file.
            long version = ((long)FontFallback.Version << 32) | (uint)FontRegistry.Version;
            if (_cachedVersion != version)
            {
                ReleaseCachedNoLock();
                _cachedVersion = version;
            }

            if (_cached.TryGetValue(key, out nint cached))
            {
                return cached;
            }

            nint built = Build(factory, listed, chain);
            if (built != 0)
            {
                _cached[key] = built;
            }

            return built;
        }
    }

    // IDWriteFactory2 IID - required for CreateFontFallbackBuilder.
    private static readonly Guid IID_IDWriteFactory2 = new("0439FC60-CA44-4994-8DEE-3A9AF7B732EC");

    private static nint Build(IDWriteFactory* factory, ListedFontFamily[] listed, string[] chain)
    {
        // QI for IDWriteFactory2 - CreateFontFallbackBuilder is not available on IDWriteFactory.
        int hr = ComHelpers.QueryInterface((nint)factory, in IID_IDWriteFactory2, out nint factory2);
        if (hr < 0 || factory2 == 0) return 0;

        try
        {
            hr = DWriteFactory2VTable.CreateFontFallbackBuilder((IDWriteFactory*)factory2, out nint builder);
            if (hr < 0 || builder == 0) return 0;

            try
            {
                // Get system font collection for resolving families
                hr = DWriteVTable.GetSystemFontCollection(factory, out nint fontCollection, false);
                nint collection = hr >= 0 ? fontCollection : 0;

                string locale = FontFallback.ResolvedLocale;

                // Listed families were named for this font and take every character; the chain's color fonts take emoji only.
                fixed (DWRITE_UNICODE_RANGE* pRanges = FullRange)
                fixed (DWRITE_UNICODE_RANGE* pEmojiRanges = EmojiRanges)
                fixed (char* pLocale = locale)
                {
                    foreach (var family in listed)
                    {
                        fixed (char* pFamily = family.Family)
                        {
                            char** familyNames = &pFamily;
                            _ = DWriteFontFallbackBuilderVTable.AddMapping(
                                builder, pRanges, (uint)FullRange.Length,
                                familyNames, 1, family.Collection != 0 ? family.Collection : collection, pLocale, null, 1.0f);
                        }
                    }

                    foreach (var family in chain)
                    {
                        bool isColor = IsColorFamily(collection, family);
                        fixed (char* pFamily = family)
                        {
                            char** familyNames = &pFamily;
                            // Errors on individual mappings are non-fatal - skip.
                            _ = DWriteFontFallbackBuilderVTable.AddMapping(
                                builder,
                                isColor ? pEmojiRanges : pRanges,
                                (uint)(isColor ? EmojiRanges.Length : FullRange.Length),
                                familyNames, 1, collection, pLocale, null, 1.0f);
                        }
                    }
                }

                // Copy system default fallback mappings so we don't lose them
                hr = DWriteFactory2VTable.GetSystemFontFallback((IDWriteFactory*)factory2, out nint systemFallback);
                if (hr >= 0 && systemFallback != 0)
                {
                    DWriteFontFallbackBuilderVTable.AddMappings(builder, systemFallback);
                    ComHelpers.Release(systemFallback);
                }

                // Build the final fallback
                hr = DWriteFontFallbackBuilderVTable.CreateFontFallback(builder, out nint fallback);
                if (hr >= 0 && fallback != 0)
                {
                    return fallback;
                }
            }
            finally
            {
                ComHelpers.Release(builder);
            }
        }
        finally
        {
            ComHelpers.Release(factory2);
        }

        return 0;
    }

    private static bool IsColorFamily(nint collection, string family)
    {
        if (collection == 0 ||
            DWriteVTable.FindFamilyName(collection, family, out uint index, out int exists) < 0 || exists == 0 ||
            DWriteVTable.GetFontFamily(collection, index, out nint fontFamily) < 0 || fontFamily == 0)
        {
            return false;
        }

        try
        {
            if (DWriteVTable.GetFirstMatchingFont(fontFamily, DWRITE_FONT_WEIGHT.NORMAL, DWRITE_FONT_STRETCH.NORMAL,
                    DWRITE_FONT_STYLE.NORMAL, out nint font) < 0 || font == 0)
            {
                return false;
            }

            try
            {
                return DWriteFont2VTable.IsColorFont(font);
            }
            finally
            {
                ComHelpers.Release(font);
            }
        }
        finally
        {
            ComHelpers.Release(fontFamily);
        }
    }

    private static void ReleaseCachedNoLock()
    {
        // A layout the fallback was set on holds its own reference.
        foreach (nint fallback in _cached.Values)
        {
            ComHelpers.Release(fallback);
        }

        _cached.Clear();
    }
}
