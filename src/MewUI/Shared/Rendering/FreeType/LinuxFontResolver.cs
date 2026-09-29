using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;

using FC = Aprillz.MewUI.Native.Fontconfig.Fontconfig;

namespace Aprillz.MewUI.Rendering.FreeType;

internal static class LinuxFontResolver
{
    private static int _fontconfigProbed; // 0=unknown, 1=available, -1=unavailable

    // fontconfig aliases rather than families: whatever font answers one of them is the one asked for.
    private static readonly HashSet<string> _genericFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "sans-serif", "sans", "serif", "monospace", "mono", "system-ui", "cursive", "fantasy", "emoji", "math",
    };

    // Resolving a family asks fontconfig, which costs far more than the text drawn with it; each answer is
    // kept until a registered font could change it.
    private static ConcurrentDictionary<PathKey, string?> _paths = new();
    private static int _pathsVersion = -1;

    /// <summary>
    /// The font file for <paramref name="family"/>. A family that is not installed gets the file fontconfig
    /// offers in its place, then the system's sans-serif, then any font found on disk.
    /// </summary>
    public static string? ResolveFontPath(string family, FontWeight weight, bool italic)
        => Cached(new PathKey(family, weight, italic, InstalledOnly: false));

    /// <summary>
    /// The font file for <paramref name="family"/> when that family is installed or registered, or
    /// <see langword="null"/> when fontconfig could only offer another family in its place.
    /// </summary>
    public static string? ResolveInstalledFontPath(string family, FontWeight weight, bool italic)
        => Cached(new PathKey(family, weight, italic, InstalledOnly: true));

    private static string? Cached(PathKey key)
    {
        var paths = _paths;
        int version = FontRegistry.Version;
        if (Volatile.Read(ref _pathsVersion) != version)
        {
            paths = new ConcurrentDictionary<PathKey, string?>();
            _paths = paths;
            Volatile.Write(ref _pathsVersion, version);
        }

        return paths.GetOrAdd(key, static key => key.InstalledOnly
            ? ResolveInstalledFontPathCore(key.Family, key.Weight, key.Italic)
            : ResolveFontPathCore(key.Family, key.Weight, key.Italic));
    }

    private static string? ResolveInstalledFontPathCore(string family, FontWeight weight, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        if (LooksLikePath(family))
        {
            return File.Exists(family) ? family : null;
        }

        var resolved = FontRegistry.Resolve(family);
        if (resolved != null && File.Exists(resolved.Value.FilePath))
        {
            return resolved.Value.FilePath;
        }

        return FontconfigResolve(family, weight, italic, requireFamily: !_genericFamilies.Contains(family));
    }

    private static string? ResolveFontPathCore(string family, FontWeight weight, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            family = "DejaVu Sans";
        }

        // Allow explicit path.
        if (LooksLikePath(family))
        {
            return family;
        }

        // Check FontRegistry (fonts registered via FontResources.Register).
        var resolved = FontRegistry.Resolve(family);
        if (resolved != null && File.Exists(resolved.Value.FilePath))
        {
            return resolved.Value.FilePath;
        }


        // Primary: use fontconfig for proper family/weight/style matching.
        var fcPath = FontconfigResolve(family, weight, italic);
        if (fcPath != null)
        {
            return fcPath;
        }

        // Secondary fontconfig: requested family unavailable - try generic
        // "sans-serif" so the system's configured sans default kicks in.
        if (!string.Equals(family, "sans-serif", StringComparison.OrdinalIgnoreCase))
        {
            fcPath = FontconfigResolve("sans-serif", weight, italic);
            if (fcPath != null)
            {
                return fcPath;
            }
        }

        // Fallback: filename heuristic.
        string[] roots =
        [
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")
        ];

        foreach (var root in roots)
        {
            var path = ProbeDirectory(root, family, weight, italic);
            if (path != null)
            {
                return path;
            }
        }

        // Last-resort: any TTF/OTF file in the standard roots. Better to render
        // text in some font than to silently drop glyph rendering (which makes
        // SvgText look invisible because IGlyphOutlineFont path isn't taken).
        foreach (var root in roots)
        {
            var anyFont = FindAnyFontFile(root);
            if (anyFont != null)
            {
                return anyFont;
            }
        }

        return null;
    }

    private static string? FindAnyFontFile(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Ignore permission issues.
        }
        return null;
    }

    /// <summary>The file fontconfig matches to <paramref name="family"/>; with <paramref name="requireFamily"/>, only a match that is that family.</summary>
    private static string? FontconfigResolve(string family, FontWeight weight, bool italic, bool requireFamily = false)
    {
        if (!IsFontconfigAvailable())
        {
            return null;
        }

        try
        {
            FC.FcInit();

            nint pattern = FC.FcPatternCreate();
            if (pattern == 0)
            {
                return null;
            }

            try
            {
                FC.FcPatternAddString(pattern, FC.FC_FAMILY, family);
                FC.FcPatternAddInteger(pattern, FC.FC_WEIGHT, ToFcWeight(weight));
                FC.FcPatternAddInteger(pattern, FC.FC_SLANT,
                    italic ? FC.FC_SLANT_ITALIC : FC.FC_SLANT_ROMAN);

                FC.FcConfigSubstitute(0, pattern, FC.FcMatchPattern);
                FC.FcDefaultSubstitute(pattern);

                nint match = FC.FcFontMatch(0, pattern, out int result);
                if (match == 0 || result != FC.FcResultMatch)
                {
                    return null;
                }

                try
                {
                    if (requireFamily && !IsFamily(match, family))
                    {
                        return null;
                    }

                    int r = FC.FcPatternGetString(match, FC.FC_FILE, 0, out nint filePtr);
                    if (r != FC.FcResultMatch || filePtr == 0)
                    {
                        return null;
                    }

                    string? path = Marshal.PtrToStringUTF8(filePtr);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
                finally
                {
                    FC.FcPatternDestroy(match);
                }
            }
            finally
            {
                FC.FcPatternDestroy(pattern);
            }
        }
        catch
        {
            // Fontconfig call failed - fall through to heuristic.
        }

        return null;
    }

    /// <summary>Whether <paramref name="match"/> is <paramref name="family"/>, under any of the names the font gives itself.</summary>
    private static bool IsFamily(nint match, string family)
    {
        for (int index = 0; FC.FcPatternGetString(match, FC.FC_FAMILY, index, out nint namePointer) == FC.FcResultMatch; index++)
        {
            if (namePointer != 0 && string.Equals(Marshal.PtrToStringUTF8(namePointer), family, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static int ToFcWeight(FontWeight weight) => weight switch
    {
        <= FontWeight.Thin => FC.FC_WEIGHT_THIN,
        <= FontWeight.Light => FC.FC_WEIGHT_LIGHT,
        <= FontWeight.Normal => FC.FC_WEIGHT_REGULAR,
        <= FontWeight.Medium => FC.FC_WEIGHT_MEDIUM,
        <= FontWeight.SemiBold => FC.FC_WEIGHT_SEMIBOLD,
        <= FontWeight.Bold => FC.FC_WEIGHT_BOLD,
        _ => FC.FC_WEIGHT_BLACK,
    };

    private static bool IsFontconfigAvailable()
    {
        if (_fontconfigProbed != 0)
        {
            return _fontconfigProbed == 1;
        }

        bool ok = NativeLibrary.TryLoad("libfontconfig.so.1", out var handle);
        if (!ok)
        {
            ok = NativeLibrary.TryLoad("libfontconfig.so", out handle);
        }

        if (ok && handle != 0)
        {
            NativeLibrary.Free(handle);
        }

        _fontconfigProbed = ok ? 1 : -1;
        return ok;
    }

    private static bool LooksLikePath(string s)
    {
        if (s.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return s.Contains('/') || s.Contains('\\');
    }

    private static string? ProbeDirectory(string root, string family, FontWeight weight, bool italic)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        var candidates = BuildCandidateFileNames(family, weight, italic);

        foreach (var fileName in candidates)
        {
            foreach (var ext in new[] { ".ttf", ".otf", ".ttc" })
            {
                var path = FindFileCaseInsensitive(root, fileName + ext);
                if (path != null)
                {
                    return path;
                }
            }
        }

        // Fallback to a well-known font.
        var dejavu = FindFileCaseInsensitive(root, "DejaVuSans.ttf");
        if (dejavu != null)
        {
            return dejavu;
        }

        return null;
    }

    private static IEnumerable<string> BuildCandidateFileNames(string family, FontWeight weight, bool italic)
    {
        string normalized = family.Replace(" ", string.Empty);

        bool bold = weight >= FontWeight.SemiBold;

        // Styled variants first - the unstyled name matches the regular weight file.
        if (bold && italic)
        {
            yield return normalized + "-BoldOblique";
            yield return normalized + "-BoldItalic";
        }

        if (bold)
        {
            yield return normalized + "-Bold";
        }

        if (italic)
        {
            yield return normalized + "-Oblique";
            yield return normalized + "-Italic";
        }

        // Regular (unstyled) last - fallback.
        yield return normalized;
    }

    private static string? FindFileCaseInsensitive(string root, string fileName)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Ignore permission issues.
        }
        return null;
    }

    private readonly record struct PathKey(string Family, FontWeight Weight, bool Italic, bool InstalledOnly);
}
