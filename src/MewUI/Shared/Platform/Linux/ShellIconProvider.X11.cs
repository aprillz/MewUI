using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Resolves freedesktop/XDG theme PNGs by MIME type derived from the file extension. Never touches
/// the target file; only reads local theme PNGs. Returns null (vector fallback) when no theme icon is found.
/// </summary>
internal sealed partial class LinuxShellIconProvider : IShellIconProvider
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_ICON_DEBUG", "[Icon.Linux]");

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private static readonly string[] _themes = ["hicolor", "Adwaita", "gnome", "breeze"];

    // Small hardcoded extension -> MIME map. /etc/mime.types is intentionally NOT read.
    private static readonly Dictionary<string, string> _mimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["txt"] = "text/plain",
        ["log"] = "text/plain",
        ["md"] = "text/plain",
        ["pdf"] = "application/pdf",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["bmp"] = "image/bmp",
        ["svg"] = "image/svg+xml",
        ["webp"] = "image/webp",
        ["mp3"] = "audio/mpeg",
        ["wav"] = "audio/x-wav",
        ["flac"] = "audio/flac",
        ["ogg"] = "audio/ogg",
        ["mp4"] = "video/mp4",
        ["mkv"] = "video/x-matroska",
        ["webm"] = "video/webm",
        ["avi"] = "video/x-msvideo",
        ["mov"] = "video/quicktime",
        ["zip"] = "application/zip",
        ["tar"] = "application/x-tar",
        ["gz"] = "application/gzip",
        ["bz2"] = "application/x-bzip2",
        ["xz"] = "application/x-xz",
        ["7z"] = "application/x-7z-compressed",
        ["rar"] = "application/vnd.rar",
        ["html"] = "text/html",
        ["htm"] = "text/html",
        ["css"] = "text/css",
        ["json"] = "application/json",
        ["xml"] = "application/xml",
        ["yaml"] = "text/plain",
        ["yml"] = "text/plain",
        ["csv"] = "text/csv",
        ["c"] = "text/x-csrc",
        ["h"] = "text/x-chdr",
        ["cpp"] = "text/x-c++src",
        ["cc"] = "text/x-c++src",
        ["cs"] = "text/x-csharp",
        ["py"] = "text/x-python",
        ["js"] = "application/javascript",
        ["ts"] = "text/plain",
        ["sh"] = "application/x-shellscript",
        ["rs"] = "text/rust",
        ["go"] = "text/x-go",
        ["java"] = "text/x-java",
    };

    public ImageSource? GetIcon(string path, bool isDirectory, int sizePx)
    {
        if (sizePx <= 0)
        {
            return null;
        }

        string extension = isDirectory
            ? string.Empty
            : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        string cacheKey = $"{extension}|{isDirectory}|{sizePx}";
        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out ImageSource? cached))
            {
                return cached;
            }

            ImageSource? resolved = TryResolveIcon(extension, isDirectory, sizePx);
            _cache[cacheKey] = resolved;
            return resolved;
        }
    }

    public ImageSource? GetPlaceIcon(ShellPlaceKind kind, int sizePx)
    {
        if (sizePx <= 0)
        {
            return null;
        }

        string cacheKey = $"place:{kind}|{sizePx}";
        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out ImageSource? cached))
            {
                return cached;
            }

            ImageSource? resolved;
            try
            {
                resolved = SearchThemeIcon(PlaceIconNames(kind), ["places", "devices", "mimetypes"], sizePx);
            }
            catch
            {
                resolved = null;
            }

            _cache[cacheKey] = resolved;
            return resolved;
        }
    }

    private ImageSource? TryResolveIcon(string extension, bool isDirectory, int sizePx)
    {
        try
        {
            string mimeType = isDirectory ? "inode/directory" : ResolveMimeType(extension);
            List<string> iconNames = BuildIconNames(mimeType, isDirectory);
            string[] categories = isDirectory
                ? ["places", "mimetypes"]
                : ["mimetypes", "places"];

            return SearchThemeIcon(iconNames, categories, sizePx);
        }
        catch
        {
            return null;
        }
    }

    // Walks themes x sizes x categories x names x base dirs for a matching PNG (scalable/SVG skipped).
    private static ImageSource? SearchThemeIcon(List<string> iconNames, string[] categories, int sizePx)
    {
        List<string> baseDirectories = BuildBaseDirectories();
        int[] candidateSizes = BuildCandidateSizes(sizePx);

        // Per name: prefer a raster PNG >= requested (downscale = crisp); else the scalable SVG rendered at the
        // exact size (modern themes like Adwaita ship icons as SVG-only with just a tiny 16px PNG); else a
        // smaller raster PNG (upscale, last resort).
        var atLeast = candidateSizes.Where(s => s >= sizePx).ToArray();
        var below = candidateSizes.Where(s => s < sizePx).ToArray();

        foreach (string iconName in iconNames)
        {
            string? png = FindRasterPng(iconName, atLeast, categories, baseDirectories);
            if (png != null)
            {
                Logger.Write($"Match png>=req req={sizePx} -> {png}");
                return ImageSource.FromFile(png);
            }

            string? svg = FindScalableSvg(iconName, categories, baseDirectories);
            if (svg != null)
            {
                var rendered = RenderSvgViaPixbuf(svg, sizePx);
                if (rendered != null)
                {
                    Logger.Write($"Match svg req={sizePx} -> {svg}");
                    return rendered;
                }
            }

            png = FindRasterPng(iconName, below, categories, baseDirectories);
            if (png != null)
            {
                Logger.Write($"Match png<req(upscale) req={sizePx} -> {png}");
                return ImageSource.FromFile(png);
            }
        }

        Logger.Write($"NO MATCH req={sizePx} names=[{string.Join(",", iconNames)}] cats=[{string.Join(",", categories)}]");
        return null;
    }

    private static string? FindRasterPng(string iconName, int[] sizes, string[] categories, List<string> baseDirectories)
    {
        foreach (int size in sizes)
        {
            foreach (string theme in _themes)
            {
                foreach (string category in categories)
                {
                    foreach (string baseDirectory in baseDirectories)
                    {
                        string candidate = Path.Combine(baseDirectory, theme, $"{size}x{size}", category, iconName + ".png");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }
        return null;
    }

    private static string? FindScalableSvg(string iconName, string[] categories, List<string> baseDirectories)
    {
        foreach (string theme in _themes)
        {
            foreach (string category in categories)
            {
                foreach (string baseDirectory in baseDirectories)
                {
                    string candidate = Path.Combine(baseDirectory, theme, "scalable", category, iconName + ".svg");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        return null;
    }

    // Renders a theme SVG to the exact pixel size via gdk-pixbuf (uses librsvg under the hood). Returns
    // straight-alpha BGRA. Null if gdk-pixbuf/librsvg is unavailable (falls back to raster).
    private static ImageSource? RenderSvgViaPixbuf(string svgPath, int sizePx)
    {
        try
        {
            IntPtr pixbuf = gdk_pixbuf_new_from_file_at_size(svgPath, sizePx, sizePx, out _);
            if (pixbuf == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                int width = gdk_pixbuf_get_width(pixbuf);
                int height = gdk_pixbuf_get_height(pixbuf);
                int channels = gdk_pixbuf_get_n_channels(pixbuf);
                int stride = gdk_pixbuf_get_rowstride(pixbuf);
                IntPtr pixels = gdk_pixbuf_get_pixels(pixbuf);
                if (width <= 0 || height <= 0 || channels < 3 || pixels == IntPtr.Zero)
                {
                    return null;
                }

                var src = new byte[stride * height];
                Marshal.Copy(pixels, src, 0, src.Length);

                // gdk-pixbuf is straight (non-premultiplied) RGBA; convert to tight BGRA.
                var dst = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * stride;
                    int dstRow = y * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        int sp = srcRow + x * channels;
                        int dp = dstRow + x * 4;
                        dst[dp] = src[sp + 2];     // B
                        dst[dp + 1] = src[sp + 1]; // G
                        dst[dp + 2] = src[sp];     // R
                        dst[dp + 3] = channels >= 4 ? src[sp + 3] : (byte)255;
                    }
                }

                return ImageSource.FromBgraPixels(width, height, dst, hasAlpha: channels >= 4);
            }
            finally
            {
                g_object_unref(pixbuf);
            }
        }
        catch
        {
            return null;
        }
    }

    [LibraryImport("libgdk_pixbuf-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr gdk_pixbuf_new_from_file_at_size(string filename, int width, int height, out IntPtr error);

    [LibraryImport("libgdk_pixbuf-2.0.so.0")]
    private static partial int gdk_pixbuf_get_width(IntPtr pixbuf);

    [LibraryImport("libgdk_pixbuf-2.0.so.0")]
    private static partial int gdk_pixbuf_get_height(IntPtr pixbuf);

    [LibraryImport("libgdk_pixbuf-2.0.so.0")]
    private static partial int gdk_pixbuf_get_rowstride(IntPtr pixbuf);

    [LibraryImport("libgdk_pixbuf-2.0.so.0")]
    private static partial int gdk_pixbuf_get_n_channels(IntPtr pixbuf);

    [LibraryImport("libgdk_pixbuf-2.0.so.0")]
    private static partial IntPtr gdk_pixbuf_get_pixels(IntPtr pixbuf);

    [LibraryImport("libgobject-2.0.so.0")]
    private static partial void g_object_unref(IntPtr obj);

    private static List<string> PlaceIconNames(ShellPlaceKind kind) => kind switch
    {
        ShellPlaceKind.Desktop => new() { "user-desktop", "folder" },
        ShellPlaceKind.Documents => new() { "folder-documents", "folder" },
        ShellPlaceKind.Downloads => new() { "folder-download", "folder-downloads", "folder" },
        ShellPlaceKind.Music => new() { "folder-music", "folder" },
        ShellPlaceKind.Pictures => new() { "folder-pictures", "folder" },
        ShellPlaceKind.Videos => new() { "folder-videos", "folder" },
        ShellPlaceKind.Home => new() { "user-home", "folder-home", "folder" },
        ShellPlaceKind.Applications => new() { "folder-applications", "applications-system", "folder" },
        ShellPlaceKind.Drive => new() { "drive-harddisk", "drive-harddisk-system", "drive-harddisk-root", "media-harddisk", "harddisk", "drive-removable-media" },
        _ => new() { "folder" },
    };

    // TODO(linux): real icon via GIO g_file_query_info / thumbnail. Returns null until then.
    public ImageSource? GetRealIcon(string path, int sizePx) => null;

    private static string ResolveMimeType(string extension)
    {
        if (extension.Length != 0 && _mimeByExtension.TryGetValue(extension, out string? mimeType))
        {
            return mimeType;
        }

        // Unknown extension: generic data so the fallback chain yields a generic document icon.
        return "application/octet-stream";
    }

    private static List<string> BuildIconNames(string mimeType, bool isDirectory)
    {
        var iconNames = new List<string>();

        if (isDirectory)
        {
            iconNames.Add("folder");
            iconNames.Add("inode-directory");
            return iconNames;
        }

        int slashIndex = mimeType.IndexOf('/');
        string media = slashIndex > 0 ? mimeType.Substring(0, slashIndex) : "application";
        string subtype = slashIndex > 0 && slashIndex < mimeType.Length - 1
            ? mimeType.Substring(slashIndex + 1)
            : string.Empty;

        // Exact MIME name: "text/plain" -> "text-plain".
        if (subtype.Length != 0)
        {
            iconNames.Add($"{media}-{subtype.Replace('/', '-')}");
            iconNames.Add($"{media}-x-{subtype.Replace('/', '-')}");
        }

        // Generic per-media fallback: "text-x-generic", "image-x-generic", etc.
        iconNames.Add($"{media}-x-generic");

        // Final catch-all generic document icon.
        if (media != "text")
        {
            iconNames.Add("text-x-generic");
        }

        return iconNames;
    }

    private static List<string> BuildBaseDirectories()
    {
        var baseDirectories = new List<string>();

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string dataHome = string.IsNullOrEmpty(xdgDataHome)
            ? Path.Combine(home, ".local", "share")
            : xdgDataHome;

        baseDirectories.Add(Path.Combine(dataHome, "icons"));
        if (!string.IsNullOrEmpty(home))
        {
            baseDirectories.Add(Path.Combine(home, ".icons"));
        }

        baseDirectories.Add("/usr/share/icons");
        baseDirectories.Add("/usr/local/share/icons");

        return baseDirectories;
    }

    private static readonly int[] _themeSizeLadder = [16, 22, 24, 32, 48, 64, 96, 128, 256, 512];

    private static int[] BuildCandidateSizes(int requestedSize)
    {
        var sizes = new List<int>();
        void AddSize(int size)
        {
            if (size > 0 && !sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }

        // Exact match first, then the smallest standard size >= requested (downscale = crisp),
        // then larger-to-smaller below requested as a last resort (upscale only if nothing bigger exists).
        AddSize(requestedSize);
        foreach (int size in _themeSizeLadder)
        {
            if (size >= requestedSize)
            {
                AddSize(size);
            }
        }
        for (int i = _themeSizeLadder.Length - 1; i >= 0; i--)
        {
            if (_themeSizeLadder[i] < requestedSize)
            {
                AddSize(_themeSizeLadder[i]);
            }
        }
        return sizes.ToArray();
    }
}
