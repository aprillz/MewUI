using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Builds a custom IDWriteFontFallback that incorporates the user's
/// <see cref="FontFallback.FallbackChain"/> on top of the system defaults.
/// Requires Windows 8.1+ (IDWriteFactory2).
/// </summary>
internal static unsafe class DWriteFontFallbackHelper
{
    private static nint _cachedFallback;
    private static int _cachedVersion = -1;

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

    /// <summary>
    /// Gets or creates a custom IDWriteFontFallback that includes the user's fallback chain
    /// prepended to the system defaults. Returns 0 if IDWriteFactory2 is not available.
    /// The returned pointer is cached and must NOT be released by the caller.
    /// </summary>
    public static nint GetOrCreate(IDWriteFactory* factory)
    {
        int version = FontFallback.Version;
        if (_cachedFallback != 0 && _cachedVersion == version)
        {
            return _cachedFallback;
        }

        var chain = FontFallback.GetChainSnapshot();
        if (chain.Length == 0)
        {
            // No user chain — use system default (return 0 to signal "use default").
            ReleaseCached();
            return 0;
        }

        nint newFallback = Build(factory, chain);
        if (newFallback == 0) return 0;

        var old = _cachedFallback;
        _cachedFallback = newFallback;
        _cachedVersion = version;

        if (old != 0)
        {
            ComHelpers.Release(old);
        }

        return newFallback;
    }

    // IDWriteFactory2 IID — required for CreateFontFallbackBuilder.
    private static readonly Guid IID_IDWriteFactory2 = new("0439FC60-CA44-4994-8DEE-3A9AF7B732EC");

    private static nint Build(IDWriteFactory* factory, string[] chain)
    {
        // QI for IDWriteFactory2 — CreateFontFallbackBuilder is not available on IDWriteFactory.
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

                // Add user chain entries — each family maps to full Unicode range
                fixed (DWRITE_UNICODE_RANGE* pRanges = FullRange)
                fixed (char* pLocale = locale)
                {
                    foreach (var family in chain)
                    {
                        fixed (char* pFamily = family)
                        {
                            char** familyNames = &pFamily;
                            // Errors on individual mappings are non-fatal — skip.
                            _ = DWriteFontFallbackBuilderVTable.AddMapping(
                                builder, pRanges, (uint)FullRange.Length,
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

    private static void ReleaseCached()
    {
        var old = _cachedFallback;
        _cachedFallback = 0;
        _cachedVersion = -1;
        if (old != 0)
        {
            ComHelpers.Release(old);
        }
    }
}
