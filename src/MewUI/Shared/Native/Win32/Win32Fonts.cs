using System.Collections.Concurrent;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Win32 helpers for loading private fonts from files at runtime.
/// </summary>
internal static class Win32Fonts
{
    private static readonly ConcurrentDictionary<string, byte> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures the font file is registered as a private font for the current process.
    /// </summary>
    public static bool EnsurePrivateFont(string fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(fontFilePath))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var path = Path.GetFullPath(fontFilePath);
        if (Loaded.ContainsKey(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            return false;
        }

        const uint FR_PRIVATE = 0x10;
        int added = Gdi32.AddFontResourceEx(path, FR_PRIVATE, 0);
        if (added <= 0)
        {
            return false;
        }

        Loaded.TryAdd(path, 0);
        return true;
    }

    /// <summary>
    /// Ensures the font file and all weight/style variants found in the same directory
    /// sharing the same typographic family name are registered as private fonts.
    /// Returns <see langword="true"/> if any font was newly registered.
    /// </summary>
    public static bool EnsurePrivateFontFamily(string fontFilePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(fontFilePath))
        {
            return false;
        }
        var path = Path.GetFullPath(fontFilePath);
        bool anyAdded = EnsurePrivateFont(path);

        if (!FontResources.TryGetParsedFamilyName(path, out var targetFamily)
            || string.IsNullOrWhiteSpace(targetFamily))
        {
            return anyAdded;
        }

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return anyAdded;
        }
         
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsFontFile(file))
            {
                continue;
            }

            if (!FontResources.TryGetParsedFamilyName(file, out var family)
                || !string.Equals(family, targetFamily, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            anyAdded |= EnsurePrivateFont(file);
        }

        return anyAdded;
    }

    private static bool IsFontFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
    }
}

