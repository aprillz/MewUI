using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Fontconfig;

using FC = Aprillz.MewUI.Native.Fontconfig.Fontconfig;

namespace Aprillz.MewUI.Rendering.FreeType;

internal static class LinuxFontResolver
{
    private static int _fontconfigProbed; // 0=unknown, 1=available, -1=unavailable

    public static string? ResolveFontPath(string family, FontWeight weight, bool italic)
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

        var envPath = Environment.GetEnvironmentVariable("MEWUI_FONT_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var envDir = Environment.GetEnvironmentVariable("MEWUI_FONT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            var p = ProbeDir(envDir, family, weight, italic);
            if (p != null)
            {
                return p;
            }
        }

        // Primary: use fontconfig for proper family/weight/style matching.
        var fcPath = FontconfigResolve(family, weight, italic);
        if (fcPath != null)
        {
            return fcPath;
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
            var p = ProbeDir(root, family, weight, italic);
            if (p != null)
            {
                return p;
            }
        }

        return null;
    }

    private static string? FontconfigResolve(string family, FontWeight weight, bool italic)
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
            // Fontconfig call failed — fall through to heuristic.
        }

        return null;
    }

    private static int ToFcWeight(FontWeight weight) => weight switch
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

    private static string? ProbeDir(string root, string family, FontWeight weight, bool italic)
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

        // Styled variants first — the unstyled name matches the regular weight file.
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

        // Regular (unstyled) last — fallback.
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
}
