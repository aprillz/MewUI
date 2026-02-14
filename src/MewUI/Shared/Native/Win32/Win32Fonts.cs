using System.Collections.Concurrent;

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
            return true;
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
}

