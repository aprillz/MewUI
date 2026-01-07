using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.FreeType;

internal static class LinuxFontResolver
{
    public static string? ResolveFontPath(string family, FontWeight weight, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family))
            family = "DejaVu Sans";

        // Allow explicit path.
        if (LooksLikePath(family))
            return family;

        var envPath = Environment.GetEnvironmentVariable("MEWUI_FONT_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        var envDir = Environment.GetEnvironmentVariable("MEWUI_FONT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            var p = ProbeDir(envDir, family, weight, italic);
            if (p != null) return p;
        }

        // Very small heuristic list for early bring-up.
        // TODO: integrate Fontconfig for proper family/style selection.
        string[] roots =
        [
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts")
        ];

        foreach (var root in roots)
        {
            var p = ProbeDir(root, family, weight, italic);
            if (p != null) return p;
        }

        return null;
    }

    private static bool LooksLikePath(string s)
    {
        if (s.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
            return true;

        return s.Contains('/') || s.Contains('\\');
    }

    private static string? ProbeDir(string root, string family, FontWeight weight, bool italic)
    {
        if (!Directory.Exists(root))
            return null;

        // Prefer DejaVu on most distros.
        // Try "Family" exact match in filename first, then common fallbacks.
        var candidates = BuildCandidateFileNames(family, weight, italic);

        foreach (var fileName in candidates)
        {
            foreach (var ext in new[] { ".ttf", ".otf", ".ttc" })
            {
                var path = FindFileCaseInsensitive(root, fileName + ext);
                if (path != null)
                    return path;
            }
        }

        // Fallback to a well-known font.
        var dejavu = FindFileCaseInsensitive(root, "DejaVuSans.ttf");
        if (dejavu != null)
            return dejavu;

        return null;
    }

    private static IEnumerable<string> BuildCandidateFileNames(string family, FontWeight weight, bool italic)
    {
        string normalized = family.Replace(" ", string.Empty);

        yield return normalized;

        bool bold = weight >= FontWeight.SemiBold;
        if (bold && italic) yield return normalized + "-BoldOblique";
        if (bold && italic) yield return normalized + "-BoldItalic";
        if (bold) yield return normalized + "-Bold";
        if (italic) yield return normalized + "-Oblique";
        if (italic) yield return normalized + "-Italic";
    }

    private static string? FindFileCaseInsensitive(string root, string fileName)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                    return path;
            }
        }
        catch
        {
            // Ignore permission issues.
        }
        return null;
    }
}

