using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Provides font loading helpers for scenarios where fonts are supplied as streams (e.g. embedded resources).
/// Fonts are persisted to a local, user-safe cache directory. Rendering backends may interpret cached file paths
/// and load/register the font as needed.
/// </summary>
public static class FontResources
{
    private static readonly ConcurrentDictionary<string, FontEntry> FontEntries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Caches a font from a stream and returns a handle that can be used to set <c>Control.FontFamily</c>.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="FontResource.FontFamily"/> is the cached font file path.
    /// </remarks>
    public static FontResource Register(Stream fontStream, string? extensionHint = null, string? nameHint = null)
    {
        ArgumentNullException.ThrowIfNull(fontStream);

        var cached = CacheToLocalFile(fontStream, extensionHint, nameHint);
        var entry = EnsureEntry(cached.HashHex, cached.Path, addRef: true);

        return new FontResource(entry.FontFamilyForControls, cached.Path, entry.ParsedFamilyName, cached.HashHex);
    }

    /// <summary>
    /// Returns a filesystem path for the supplied font stream. Does not register the font.
    /// </summary>
    public static string CacheToFile(Stream fontStream, string? extensionHint = null, string? nameHint = null)
    {
        ArgumentNullException.ThrowIfNull(fontStream);
        var cached = CacheToLocalFile(fontStream, extensionHint, nameHint);
        _ = EnsureEntry(cached.HashHex, cached.Path, addRef: false);
        return cached.Path;
    }

    /// <summary>
    /// Determines whether the given string likely represents a font file path.
    /// </summary>
    public static bool LooksLikeFontFilePath(string value) => LooksLikePath(value);

    internal static bool TryGetParsedFamilyName(string fontFilePath, out string familyName)
    {
        familyName = string.Empty;

        if (!LooksLikePath(fontFilePath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(fontFilePath);
        string key = ComputeStableKeyForPath(fullPath);

        var entry = EnsureEntry(key, fullPath, addRef: false);
        familyName = entry.ParsedFamilyName;
        return !string.IsNullOrWhiteSpace(familyName);
    }

    internal static void Release(string key)
    {
        if (!FontEntries.TryGetValue(key, out var entry))
        {
            return;
        }

        if (Interlocked.Decrement(ref entry.RefCount) > 0)
        {
            return;
        }

        // Remove first so re-entrancy doesn't keep a stale entry alive.
        FontEntries.TryRemove(key, out _);

        // Best-effort cleanup for cache files we own.
        try
        {
            if (IsUnderCacheDirectory(entry.Path))
            {
                File.Delete(entry.Path);
            }
        }
        catch
        {
            // Ignore cleanup failures (file may be in use).
        }
    }

    private static FontEntry EnsureEntry(string key, string path, bool addRef)
    {
        var entry = FontEntries.GetOrAdd(key, _ => CreateEntry(path));
        if (addRef)
        {
            Interlocked.Increment(ref entry.RefCount);
        }
        return entry;
    }

    private static FontEntry CreateEntry(string path)
    {
        var parsed = TrueTypeNameTable.TryGetFamilyName(path, out var name) ? name : string.Empty;
        // For a stream-supplied font, the control-level value is the cached file path.
        return new FontEntry(path, path, parsed);
    }

    private static bool LooksLikePath(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        if (s.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return s.Contains('/') || s.Contains('\\') || s.Contains(':');
    }

    private static string ComputeStableKeyForPath(string fullPath)
    {
        // Path-based fonts are considered "global for the process" (no refcount-based lifetime),
        // but we still store them in the dictionary to avoid repeated registration work.
        // Use SHA256(path) to keep the key compact and case-insensitive on Windows.
        using var sha = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(fullPath);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool IsUnderCacheDirectory(string path)
    {
        try
        {
            var cacheRoot = GetCacheDirectory();
            var full = Path.GetFullPath(path);
            return full.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetCacheDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Aprillz", "MewUI", "FontCache");
    }

    private readonly record struct CachedFont(string Path, string HashHex);

    private static CachedFont CacheToLocalFile(Stream fontStream, string? extensionHint, string? nameHint)
    {
        Directory.CreateDirectory(GetCacheDirectory());

        string extension = NormalizeExtensionHint(extensionHint);

        // Write to a temp file while hashing.
        string tempPath = Path.Combine(GetCacheDirectory(), Guid.NewGuid().ToString("N") + ".tmp");
        string hashHex;

        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            Span<byte> buffer = stackalloc byte[64 * 1024];
            int read;
            while ((read = fontStream.Read(buffer)) > 0)
            {
                hasher.AppendData(buffer[..read]);
                output.Write(buffer[..read]);
            }

            hashHex = Convert.ToHexString(hasher.GetHashAndReset());
        }

        if (extension == string.Empty)
        {
            extension = GuessExtensionFromFile(tempPath);
        }

        string fileNameStem = string.IsNullOrWhiteSpace(nameHint) ? hashHex : $"{SanitizeFileName(nameHint)}-{hashHex}";
        string finalPath = Path.Combine(GetCacheDirectory(), fileNameStem + extension);

        try
        {
            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }
        catch
        {
            // Best-effort: if atomic move fails, keep temp file as the final path.
            finalPath = tempPath;
        }

        return new CachedFont(finalPath, hashHex);
    }

    private static string NormalizeExtensionHint(string? extensionHint)
    {
        if (string.IsNullOrWhiteSpace(extensionHint))
        {
            return string.Empty;
        }

        var ext = extensionHint.Trim();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        if (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            return ext.ToLowerInvariant();
        }

        return string.Empty;
    }

    private static string GuessExtensionFromFile(string filePath)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            using var fs = File.OpenRead(filePath);
            if (fs.Read(header) != 4)
            {
                return ".ttf";
            }

            // TTC: 'ttcf'
            if (header[0] == (byte)'t' && header[1] == (byte)'t' && header[2] == (byte)'c' && header[3] == (byte)'f')
            {
                return ".ttc";
            }

            // OTF: 'OTTO'
            if (header[0] == (byte)'O' && header[1] == (byte)'T' && header[2] == (byte)'T' && header[3] == (byte)'O')
            {
                return ".otf";
            }

            // TTF: 0x00010000
            if (header[0] == 0x00 && header[1] == 0x01 && header[2] == 0x00 && header[3] == 0x00)
            {
                return ".ttf";
            }
        }
        catch
        {
            // ignore
        }

        return ".ttf";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> result = stackalloc char[name.Length];
        int i = 0;

        foreach (var ch in name)
        {
            result[i++] = Array.IndexOf(invalid, ch) >= 0 ? '_' : ch;
        }

        return new string(result[..i]).Trim();
    }

    private sealed class FontEntry
    {
        public string Path { get; }
        public string FontFamilyForControls { get; }
        public string ParsedFamilyName { get; }
        public int RefCount;

        public FontEntry(string path, string fontFamilyForControls, string parsedFamilyName)
        {
            Path = path;
            FontFamilyForControls = fontFamilyForControls;
            ParsedFamilyName = parsedFamilyName;
            RefCount = 0;
        }
    }
}
