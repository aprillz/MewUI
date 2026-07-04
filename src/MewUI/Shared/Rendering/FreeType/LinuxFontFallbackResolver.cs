using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Fontconfig;
using Aprillz.MewUI.Native.FreeType;

using FC = Aprillz.MewUI.Native.Fontconfig.Fontconfig;
using FT = Aprillz.MewUI.Native.FreeType.FreeType;

namespace Aprillz.MewUI.Rendering.FreeType;

/// <summary>
/// Resolves fallback fonts for missing glyphs on Linux/FreeType.
/// L1 cache maps codepoint → chain index (which font family covers this codepoint).
/// L2 cache is the existing <see cref="FreeTypeFaceCache"/>.
/// </summary>
internal static class LinuxFontFallbackResolver
{
    private static ConcurrentDictionary<uint, int> _cache = new();
    private static int _cacheVersion;

    /// <summary>
    /// Resolves a fallback face for the given codepoint, or <c>null</c> if no fallback covers it.
    /// </summary>
    public static FreeTypeFaceCache.FaceEntry? Resolve(
        uint codepoint, int pixelHeight, FontWeight weight, bool italic)
    {
        EnsureCacheVersion();

        // L1 lookup
        if (_cache.TryGetValue(codepoint, out int index))
        {
            if (index < 0) return null;
            return GetFaceForChainIndex(index, pixelHeight, weight, italic);
        }

        // Walk the user-configured fallback chain
        var chain = FontFallback.GetChainSnapshot();
        for (int i = 0; i < chain.Length; i++)
        {
            var path = LinuxFontResolver.ResolveFontPath(chain[i], weight, italic);
            if (path == null) continue;

            var face = FreeTypeFaceCache.Instance.Get(path, pixelHeight, weight, italic);
            if (face.GetGlyphIndex(codepoint) != 0)
            {
                _cache[codepoint] = i;
                return face;
            }
        }

        // Last resort: fontconfig FcFontSort query
        var fcFace = FontconfigSortResolve(codepoint, pixelHeight, weight, italic);
        if (fcFace != null)
        {
            // Cache as negative chain index (-2 means "fontconfig resolved, not in user chain")
            // We don't cache fontconfig results in L1 by chain index since they're outside the chain.
            // Instead, just return the face - subsequent calls for the same codepoint will re-query
            // fontconfig, but that's rare since most CJK/emoji should be in the user chain.
            return fcFace;
        }

        // No fallback found
        _cache[codepoint] = -1;
        return null;
    }

    /// <summary>
    /// Checks if a given codepoint is available in any fallback font (without loading a specific size).
    /// Used for measurement fast-paths.
    /// </summary>
    public static bool HasFallback(uint codepoint, int pixelHeight, FontWeight weight, bool italic)
    {
        return Resolve(codepoint, pixelHeight, weight, italic) != null;
    }

    private static void EnsureCacheVersion()
    {
        int currentVersion = FontFallback.Version;
        if (_cacheVersion != currentVersion)
        {
            Interlocked.Exchange(ref _cache, new ConcurrentDictionary<uint, int>());
            _cacheVersion = currentVersion;
        }
    }

    private static FreeTypeFaceCache.FaceEntry? GetFaceForChainIndex(
        int chainIndex, int pixelHeight, FontWeight weight, bool italic)
    {
        var chain = FontFallback.GetChainSnapshot();
        if ((uint)chainIndex >= (uint)chain.Length) return null;

        var path = LinuxFontResolver.ResolveFontPath(chain[chainIndex], weight, italic);
        if (path == null) return null;

        return FreeTypeFaceCache.Instance.Get(path, pixelHeight, weight, italic);
    }

    private static FreeTypeFaceCache.FaceEntry? FontconfigSortResolve(
        uint codepoint, int pixelHeight, FontWeight weight, bool italic)
    {
        // Check if fontconfig is available (reuse LinuxFontResolver's probe)
        nint charset = 0;
        nint pattern = 0;
        nint fontSet = 0;

        try
        {
            FC.FcInit();

            charset = FC.FcCharSetCreate();
            if (charset == 0) return null;

            FC.FcCharSetAddChar(charset, codepoint);

            pattern = FC.FcPatternCreate();
            if (pattern == 0) return null;

            FC.FcPatternAddCharSet(pattern, FC.FC_CHARSET, charset);
            FC.FcConfigSubstitute(0, pattern, FC.FcMatchPattern);
            FC.FcDefaultSubstitute(pattern);

            fontSet = FC.FcFontSort(0, pattern, true, out _, out int result);
            if (fontSet == 0 || result != FC.FcResultMatch) return null;

            // FcFontSet: { int nfont; int sfont; FcPattern** fonts; }
            int nfont = Marshal.ReadInt32(fontSet, 0);
            nint fontsPtr = Marshal.ReadIntPtr(fontSet, 2 * sizeof(int));

            for (int i = 0; i < nfont; i++)
            {
                nint fontPattern = Marshal.ReadIntPtr(fontsPtr, i * nint.Size);
                if (fontPattern == 0) continue;

                int r = FC.FcPatternGetString(fontPattern, FC.FC_FILE, 0, out nint filePtr);
                if (r != FC.FcResultMatch || filePtr == 0) continue;

                string? filePath = Marshal.PtrToStringUTF8(filePtr);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) continue;

                var face = FreeTypeFaceCache.Instance.Get(filePath, pixelHeight, weight, italic);
                if (face.GetGlyphIndex(codepoint) != 0)
                {
                    return face;
                }
            }
        }
        catch
        {
            // Fontconfig unavailable or call failed
        }
        finally
        {
            if (fontSet != 0) FC.FcFontSetDestroy(fontSet);
            if (pattern != 0) FC.FcPatternDestroy(pattern);
            if (charset != 0) FC.FcCharSetDestroy(charset);
        }

        return null;
    }
}
