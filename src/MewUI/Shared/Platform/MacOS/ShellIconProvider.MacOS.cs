using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Aprillz.MewUI.Platform.MacOS;
/// <summary>
/// macOS: resolves icons BY FILE EXTENSION via <c>[NSWorkspace iconForFileType:]</c>, never touching the
/// file on disk. The NSImage is rasterized to a straight-alpha BGRA32 buffer. Safe on the UI thread.
/// </summary>
internal sealed unsafe partial class MacShellIconProvider : IShellIconProvider
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private static bool _frameworksLoaded;

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

    private ImageSource? TryResolveIcon(string extension, bool isDirectory, int sizePx)
    {
        try
        {
            EnsureFrameworksLoaded();

            nint workspaceClass = GetClass("NSWorkspace");
            if (workspaceClass == 0)
            {
                return null;
            }

            nint workspace = MsgSend_nint(workspaceClass, Sel("sharedWorkspace"));
            if (workspace == 0)
            {
                return null;
            }

            nint nsImage = isDirectory
                ? ResolveFolderImage(workspace)
                : ResolveFileTypeImage(workspace, extension);
            if (nsImage == 0)
            {
                return null;
            }

            return RenderNSImageToBgra(nsImage, sizePx);
        }
        catch
        {
            return null;
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

            ImageSource? resolved = TryResolvePlace(kind, sizePx);
            _cache[cacheKey] = resolved;
            return resolved;
        }
    }

    // Real per-file/volume icon via [NSWorkspace iconForFile:], rasterized through CoreGraphics
    // (CGImage -> CGBitmapContext) which is thread-safe off the main thread - the async upgrade layer
    // calls this on a background thread (iconForFile may touch a slow volume).
    public ImageSource? GetRealIcon(string path, int sizePx)
    {
        if (sizePx <= 0 || string.IsNullOrEmpty(path))
        {
            return null;
        }

        // This runs on a background thread (no ambient autorelease pool there), so wrap the AppKit calls in
        // one to avoid leaking the autoreleased NSImage / "no pool in place" warnings.
        nint poolClass = GetClass("NSAutoreleasePool");
        nint pool = poolClass != 0 ? MsgSend_nint(poolClass, Sel("new")) : 0;
        try
        {
            EnsureFrameworksLoaded();

            nint workspaceClass = GetClass("NSWorkspace");
            if (workspaceClass == 0)
            {
                return null;
            }

            nint workspace = MsgSend_nint(workspaceClass, Sel("sharedWorkspace"));
            if (workspace == 0)
            {
                return null;
            }

            nint pathStr = CreateNSString(path);
            nint nsImage = MsgSend_nint_nint(workspace, Sel("iconForFile:"), pathStr);
            if (nsImage == 0)
            {
                return null;
            }

            return RasterizeViaCoreGraphics(nsImage, sizePx);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (pool != 0)
            {
                MsgSend_void(pool, Sel("drain"));
            }
        }
    }

    private static ImageSource? RasterizeViaCoreGraphics(nint nsImage, int sizePx)
    {
        int target = sizePx;

        // iconForFile's NSImage defaults to 32pt; without this it returns the 32px representation and upscales
        // (low-res vs Finder). Set the size and pass a proposed rect so CG picks a representation >= target.
        MsgSend_void_size(nsImage, Sel("setSize:"), new NSSize(target, target));
        var proposed = new NSRect(0, 0, target, target);
        nint cgImage = objc_msgSend_nint_nnn(nsImage, Sel("CGImageForProposedRect:context:hints:"), (nint)(&proposed), 0, 0);
        if (cgImage == 0 || CGImageGetWidth(cgImage) == 0)
        {
            return null;
        }

        var pixels = new byte[target * target * 4];
        fixed (byte* buffer = pixels)
        {
            nint colorSpace = CGColorSpaceCreateDeviceRGB();
            if (colorSpace == 0)
            {
                return null;
            }

            nint ctx = CGBitmapContextCreate(buffer, (nuint)target, (nuint)target, 8, (nuint)(target * 4),
                colorSpace, (nuint)(kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little));
            CGColorSpaceRelease(colorSpace);
            if (ctx == 0)
            {
                return null;
            }

            try
            {
                var rect = new NSRect(0, 0, target, target);
                CGContextClearRect(ctx, rect);
                CGContextDrawImage(ctx, rect, cgImage); // scales the source to the target size
            }
            finally
            {
                CGContextRelease(ctx);
            }
        }

        // Context is premultiplied; FromBgraPixels wants straight alpha. (No row flip needed: the data is
        // already top-down as consumed.)
        UnpremultiplyStraight(pixels);
        return ImageSource.FromBgraPixels(target, target, pixels, hasAlpha: true);
    }

    private static void UnpremultiplyStraight(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            if (alpha == 0 || alpha == 255)
            {
                continue;
            }
            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
        }
    }

    private const uint kCGImageAlphaPremultipliedFirst = 2;
    private const uint kCGBitmapByteOrder32Little = 2u << 12;

    private ImageSource? TryResolvePlace(ShellPlaceKind kind, int sizePx)
    {
        try
        {
            EnsureFrameworksLoaded();

            nint nsImage = LoadIcnsImage(kind);
            if (nsImage == 0)
            {
                // Specific .icns missing on this OS version: fall back to a rich (multi-resolution) generic
                // .icns of the right shape, not imageNamed:NSFolder (a small low-res rep that upscales, and
                // wrong shape for drives).
                string generic = kind == ShellPlaceKind.Drive ? "SidebarInternalDisk" : "GenericFolderIcon";
                nsImage = LoadIcnsByName(generic);
            }

            if (nsImage == 0)
            {
                return null;
            }

            return RenderNSImageToBgra(nsImage, sizePx);
        }
        catch
        {
            return null;
        }
    }

    private static nint LoadIcnsImage(ShellPlaceKind kind)
    {
        // Try candidates in order; names differ across macOS versions (e.g. no GenericHardDiskIcon on newer).
        foreach (string name in IcnsCandidates(kind))
        {
            nint image = LoadIcnsByName(name);
            if (image != 0)
            {
                return image;
            }
        }
        return 0;
    }

    private static nint LoadIcnsByName(string icnsName)
    {
        // Local system bundle resource; reading it is non-blocking (no real-path stat).
        string path = $"/System/Library/CoreServices/CoreTypes.bundle/Contents/Resources/{icnsName}.icns";
        if (!File.Exists(path))
        {
            return 0;
        }

        nint imageClass = GetClass("NSImage");
        if (imageClass == 0)
        {
            return 0;
        }

        nint allocated = MsgSend_nint(imageClass, Sel("alloc"));
        if (allocated == 0)
        {
            return 0;
        }

        nint nsPath = CreateNSString(path);
        return MsgSend_nint_nint(allocated, Sel("initWithContentsOfFile:"), nsPath);
    }

    private static string[] IcnsCandidates(ShellPlaceKind kind) => kind switch
    {
        ShellPlaceKind.Desktop => ["DesktopFolderIcon"],
        ShellPlaceKind.Documents => ["DocumentsFolderIcon"],
        ShellPlaceKind.Downloads => ["DownloadsFolderIcon"],
        ShellPlaceKind.Music => ["MusicFolderIcon"],
        ShellPlaceKind.Pictures => ["PicturesFolderIcon"],
        ShellPlaceKind.Videos => ["MovieFolderIcon"],
        ShellPlaceKind.Home => ["HomeFolderIcon"],
        ShellPlaceKind.Applications => ["ApplicationsFolderIcon"],
        ShellPlaceKind.Folder => ["GenericFolderIcon"],
        // No GenericHardDiskIcon on newer macOS; the Finder sidebar internal-disk glyph is the right fit.
        ShellPlaceKind.Drive => ["GenericHardDiskIcon", "SidebarInternalDisk", "HardDriveIcon"],
        _ => [],
    };

    private nint ResolveFileTypeImage(nint workspace, string extension)
    {
        if (extension.Length == 0)
        {
            // Unknown/extensionless files: iconForFileType:"public.data" yields a small low-res generic icon
            // that upscales blurrily. Use the multi-resolution .icns instead (same source as the places icons).
            nint generic = LoadIcnsByName("GenericDocumentIcon");
            if (generic != 0)
            {
                return generic;
            }
            nint genericType = CreateNSString("public.data");
            return MsgSend_nint_nint(workspace, Sel("iconForFileType:"), genericType);
        }

        // iconForFileType: takes the extension WITHOUT the leading dot.
        nint extensionString = CreateNSString(extension);
        return MsgSend_nint_nint(workspace, Sel("iconForFileType:"), extensionString);
    }

    private nint ResolveFolderImage(nint workspace)
    {
        // Multi-resolution generic folder .icns (same source the sidebar places use, which render crisply).
        // iconForFileType: expects a filename extension / HFS code and does not reliably resolve a UTI like
        // "public.folder" on all macOS versions - it can return nil, dropping folders to the vector fallback.
        nint icns = LoadIcnsByName("GenericFolderIcon");
        if (icns != 0)
        {
            return icns;
        }
        nint folderType = CreateNSString("public.folder");
        return MsgSend_nint_nint(workspace, Sel("iconForFileType:"), folderType);
    }

    private ImageSource? RenderNSImageToBgra(nint nsImage, int sizePx)
    {
        nint poolClass = GetClass("NSAutoreleasePool");
        nint pool = poolClass != 0 ? MsgSend_nint(poolClass, Sel("new")) : 0;
        try
        {
            // Force the NSImage to the requested point size before rasterizing.
            var targetSize = new NSSize(sizePx, sizePx);
            MsgSend_void_size(nsImage, Sel("setSize:"), targetSize);

            nint bitmapRep = CreateBitmapRep(nsImage, sizePx);
            if (bitmapRep == 0)
            {
                return null;
            }

            int width = (int)MsgSend_long(bitmapRep, Sel("pixelsWide"));
            int height = (int)MsgSend_long(bitmapRep, Sel("pixelsHigh"));
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            nint pixelData = MsgSend_nint(bitmapRep, Sel("bitmapData"));
            if (pixelData == 0)
            {
                return null;
            }

            int bytesPerRow = (int)MsgSend_long(bitmapRep, Sel("bytesPerRow"));
            int samplesPerPixel = (int)MsgSend_long(bitmapRep, Sel("samplesPerPixel"));
            int bitsPerPixel = (int)MsgSend_long(bitmapRep, Sel("bitsPerPixel"));
            int sourceBytesPerPixel = bitsPerPixel > 0 ? bitsPerPixel / 8 : samplesPerPixel;
            if (sourceBytesPerPixel < 3)
            {
                return null;
            }

            bool hasAlpha = MsgSend_bool(bitmapRep, Sel("hasAlpha")) && sourceBytesPerPixel >= 4;

            // NSBitmapFormatAlphaFirst = 1, NSBitmapFormatAlphaNonPremultiplied = 2.
            long bitmapFormat = MsgSend_long(bitmapRep, Sel("bitmapFormat"));
            bool alphaFirst = (bitmapFormat & 1) != 0;
            bool premultiplied = hasAlpha && (bitmapFormat & 2) == 0;

            byte[] destination = ConvertToTightBgra(
                pixelData,
                width,
                height,
                bytesPerRow,
                sourceBytesPerPixel,
                hasAlpha,
                alphaFirst,
                premultiplied);

            return ImageSource.FromBgraPixels(width, height, destination, hasAlpha);
        }
        finally
        {
            if (pool != 0)
            {
                MsgSend_void(pool, Sel("drain"));
            }
        }
    }

    private nint CreateBitmapRep(nint nsImage, int sizePx)
    {
        // Draw the image into a focused view rect, then snapshot it as an NSBitmapImageRep.
        // initWithFocusedViewRect: yields straight RGBA we can read via bitmapData.
        var drawRect = new NSRect(0, 0, sizePx, sizePx);

        MsgSend_void(nsImage, Sel("lockFocus"));
        try
        {
            nint repClass = GetClass("NSBitmapImageRep");
            if (repClass == 0)
            {
                return 0;
            }

            nint allocated = MsgSend_nint(repClass, Sel("alloc"));
            if (allocated == 0)
            {
                return 0;
            }

            return MsgSend_nint_rect(allocated, Sel("initWithFocusedViewRect:"), drawRect);
        }
        finally
        {
            MsgSend_void(nsImage, Sel("unlockFocus"));
        }
    }

    private static byte[] ConvertToTightBgra(
        nint pixelData,
        int width,
        int height,
        int bytesPerRow,
        int sourceBytesPerPixel,
        bool hasAlpha,
        bool alphaFirst,
        bool premultiplied)
    {
        byte[] destination = new byte[width * height * 4];
        byte* sourceBase = (byte*)pixelData;

        for (int row = 0; row < height; row++)
        {
            byte* sourceRow = sourceBase + (long)row * bytesPerRow;
            int destinationRowStart = row * width * 4;

            for (int column = 0; column < width; column++)
            {
                byte* sourcePixel = sourceRow + column * sourceBytesPerPixel;

                byte red;
                byte green;
                byte blue;
                byte alpha;

                if (hasAlpha && alphaFirst)
                {
                    alpha = sourcePixel[0];
                    red = sourcePixel[1];
                    green = sourcePixel[2];
                    blue = sourcePixel[3];
                }
                else
                {
                    red = sourcePixel[0];
                    green = sourcePixel[1];
                    blue = sourcePixel[2];
                    alpha = hasAlpha ? sourcePixel[3] : (byte)255;
                }

                if (premultiplied && alpha != 0 && alpha != 255)
                {
                    // Recover straight alpha so FromBgraPixels receives non-premultiplied BGRA.
                    red = UnpremultiplyChannel(red, alpha);
                    green = UnpremultiplyChannel(green, alpha);
                    blue = UnpremultiplyChannel(blue, alpha);
                }

                int destinationIndex = destinationRowStart + column * 4;
                destination[destinationIndex + 0] = blue;
                destination[destinationIndex + 1] = green;
                destination[destinationIndex + 2] = red;
                destination[destinationIndex + 3] = alpha;
            }
        }

        return destination;
    }

    private static byte UnpremultiplyChannel(byte channel, byte alpha)
    {
        int straight = channel * 255 / alpha;
        return straight > 255 ? (byte)255 : (byte)straight;
    }

    private static void EnsureFrameworksLoaded()
    {
        if (_frameworksLoaded)
        {
            return;
        }

        // objc_getClass returns 0 for AppKit/Foundation classes until the frameworks are linked.
        NativeLibrary.TryLoad("/System/Library/Frameworks/Foundation.framework/Foundation", out _);
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
        _frameworksLoaded = true;
    }

    // Coordinate structs mirror Aprillz.MewUI.Platform.MacOS interop layout (double precision Cocoa points).
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NSSize
    {
        public readonly double width;
        public readonly double height;

        public NSSize(double width, double height)
        {
            this.width = width;
            this.height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NSPoint
    {
        public readonly double x;
        public readonly double y;

        public NSPoint(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NSRect
    {
        public readonly NSPoint origin;
        public readonly NSSize size;

        public NSRect(double x, double y, double width, double height)
        {
            origin = new NSPoint(x, y);
            size = new NSSize(width, height);
        }
    }

    // P/Invoke surface mirrors the repo's ObjC helper (libobjc.A.dylib, byte* string marshaling).
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static partial nint objc_getClass(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static partial nint sel_registerName(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_nint(nint receiver, nint selector, nint a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_bytePtr(nint receiver, nint selector, byte* a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_rect(nint receiver, nint selector, NSRect rect);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_size(nint receiver, nint selector, NSSize size);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial long objc_msgSend_long(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte objc_msgSend_byte(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint_nnn(nint receiver, nint selector, nint a0, nint a1, nint a2);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial nuint CGImageGetWidth(nint image);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial nint CGColorSpaceCreateDeviceRGB();

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGColorSpaceRelease(nint colorSpace);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint colorSpace, nuint bitmapInfo);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextDrawImage(nint context, NSRect rect, nint image);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextClearRect(nint context, NSRect rect);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGContextRelease(nint context);

    private static nint GetClass(string name)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* namePtr = utf8)
        {
            return objc_getClass(namePtr);
        }
    }

    private static nint Sel(string name)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* namePtr = utf8)
        {
            return sel_registerName(namePtr);
        }
    }

    private static nint CreateNSString(string value)
    {
        nint stringClass = GetClass("NSString");
        nint selector = Sel("stringWithUTF8String:");
        byte[] utf8 = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* valuePtr = utf8)
        {
            return objc_msgSend_nint_bytePtr(stringClass, selector, valuePtr);
        }
    }

    private static nint MsgSend_nint(nint receiver, nint selector)
        => objc_msgSend_nint(receiver, selector);

    private static nint MsgSend_nint_nint(nint receiver, nint selector, nint a0)
        => objc_msgSend_nint_nint(receiver, selector, a0);

    private static nint MsgSend_nint_rect(nint receiver, nint selector, NSRect rect)
        => objc_msgSend_nint_rect(receiver, selector, rect);

    private static void MsgSend_void(nint receiver, nint selector)
        => objc_msgSend_void(receiver, selector);

    private static void MsgSend_void_size(nint receiver, nint selector, NSSize size)
        => objc_msgSend_void_size(receiver, selector, size);

    private static long MsgSend_long(nint receiver, nint selector)
        => objc_msgSend_long(receiver, selector);

    private static bool MsgSend_bool(nint receiver, nint selector)
        => objc_msgSend_byte(receiver, selector) != 0;
}
