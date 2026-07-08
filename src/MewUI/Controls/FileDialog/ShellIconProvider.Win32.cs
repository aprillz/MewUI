using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace Aprillz.MewUI.Platform;

/// <summary>
/// Windows: <c>SHGetFileInfo</c> with <c>SHGFI_USEFILEATTRIBUTES</c> resolves the icon from the extension
/// alone (registry lookup, no disk access), so it is non-blocking. The returned HICON is converted to a
/// straight-alpha BGRA32 buffer via GetIconInfo + GetDIBits.
/// </summary>
internal sealed unsafe partial class WindowsShellIconProvider : IShellIconProvider
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);

    public ImageSource? GetIcon(string path, bool isDirectory, int sizePx)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string extension = isDirectory ? "<dir>" : Path.GetExtension(path).ToLowerInvariant();
        string key = $"{extension}|{isDirectory}|{sizePx}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ImageSource? result = null;
        try
        {
            result = Resolve(extension, isDirectory, sizePx);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"[win] GetIcon EX ext='{extension}' size={sizePx}: {ex}");
        }

        DiagLog.Write($"[win] GetIcon ext='{extension}' isDir={isDirectory} size={sizePx} -> {(result == null ? "NULL" : $"{result.PixelWidth}x{result.PixelHeight}")}");
        _cache[key] = result;
        return result;
    }

    public ImageSource? GetPlaceIcon(ShellPlaceKind kind, int sizePx)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        bool wantLarge = sizePx > 16;
        string key = $"place:{kind}|{(wantLarge ? "L" : "S")}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ImageSource? result = null;
        try
        {
            result = ResolvePlace(kind, wantLarge);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"[win] GetPlaceIcon EX kind={kind} size={sizePx}: {ex}");
        }

        DiagLog.Write($"[win] GetPlaceIcon kind={kind} size={sizePx} -> {(result == null ? "NULL" : $"{result.PixelWidth}x{result.PixelHeight}")}");
        _cache[key] = result;
        return result;
    }

    public ImageSource? GetRealIcon(string path, int sizePx)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            // Real path query (actual embedded/volume icon) - no SHGFI_USEFILEATTRIBUTES. This can touch the
            // file / a network share, so the async upgrade layer only calls it on a background thread.
            if (sizePx > 32)
            {
                int imageList = sizePx <= 48 ? SHIL_EXTRALARGE : SHIL_JUMBO;
                var fromList = RealFromImageList(path, imageList);
                if (fromList != null)
                {
                    return fromList;
                }
            }

            uint sizeFlag = sizePx <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON;
            var info = new SHFILEINFO();
            IntPtr ok = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | sizeFlag);
            if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return IconToImageSource(info.hIcon);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"[win] GetRealIcon EX path='{path}' size={sizePx}: {ex}");
            return null;
        }
    }

    private static ImageSource? RealFromImageList(string path, int imageList)
    {
        var info = new SHFILEINFO();
        IntPtr res = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
        if (res == IntPtr.Zero)
        {
            return null;
        }

        var iid = IID_IImageList;
        if (SHGetImageList(imageList, ref iid, out IImageList? list) != 0 || list == null)
        {
            return null;
        }

        if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out IntPtr hIcon) != 0 || hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return IconToImageSource(hIcon);
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static ImageSource? ResolvePlace(ShellPlaceKind kind, bool wantLarge)
    {
        if (kind == ShellPlaceKind.Drive)
        {
            return StockIcon(SIID_DRIVEFIXED, wantLarge);
        }

        string? guid = KnownFolderGuid(kind);
        if (guid == null)
        {
            return StockIcon(SIID_FOLDER, wantLarge);
        }

        // Known-folder default icon spec, e.g. "%SystemRoot%\system32\imageres.dll,-184".
        string subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FolderDescriptions\{guid}";
        string? iconSpec = ReadRegistryString(subKey, "Icon");
        if (string.IsNullOrEmpty(iconSpec))
        {
            return StockIcon(SIID_FOLDER, wantLarge);
        }

        int comma = iconSpec.LastIndexOf(',');
        string filePart = comma >= 0 ? iconSpec.Substring(0, comma) : iconSpec;
        int iconIndex = 0;
        if (comma >= 0)
        {
            int.TryParse(iconSpec.AsSpan(comma + 1), out iconIndex);
        }
        string filePath = Environment.ExpandEnvironmentVariables(filePart.Trim('"', ' '));

        var handles = new IntPtr[1];
        int extracted = wantLarge
            ? ExtractIconEx(filePath, iconIndex, handles, null, 1)
            : ExtractIconEx(filePath, iconIndex, null, handles, 1);
        if (extracted <= 0 || handles[0] == IntPtr.Zero)
        {
            return StockIcon(SIID_FOLDER, wantLarge);
        }

        try
        {
            return IconToImageSource(handles[0]);
        }
        finally
        {
            DestroyIcon(handles[0]);
        }
    }

    private static ImageSource? StockIcon(uint stockId, bool wantLarge)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        uint flags = SHGSI_ICON | (wantLarge ? SHGSI_LARGEICON : SHGSI_SMALLICON);
        if (SHGetStockIconInfo(stockId, flags, ref info) != 0 || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return IconToImageSource(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static string? KnownFolderGuid(ShellPlaceKind kind) => kind switch
    {
        ShellPlaceKind.Desktop => "{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}",
        ShellPlaceKind.Documents => "{FDD39AD0-238F-46AF-ADB4-6C85480369C7}",
        ShellPlaceKind.Downloads => "{374DE290-123F-4565-9164-39C4925E467B}",
        ShellPlaceKind.Music => "{4BD8D571-6D19-48D3-BE97-422220080E43}",
        ShellPlaceKind.Pictures => "{33E28130-4E1E-4676-835A-98395C3BC476}",
        ShellPlaceKind.Videos => "{18989B1D-99B5-455B-841C-AB7C74E4DDFC}",
        ShellPlaceKind.Home => "{5E6C858F-0E22-4760-9AFE-EA3317B67173}",
        _ => null,
    };

    private static string? ReadRegistryString(string subKey, string valueName)
    {
        uint flags = RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ;
        uint size = 0;
        if (RegGetValue(HKEY_LOCAL_MACHINE, subKey, valueName, flags, out _, null, ref size) != 0 || size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        if (RegGetValue(HKEY_LOCAL_MACHINE, subKey, valueName, flags, out _, buffer, ref size) != 0)
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    private static ImageSource? Resolve(string extension, bool isDirectory, int requestPx)
    {
        // A bare name carrying the extension; with SHGFI_USEFILEATTRIBUTES the file need not exist (no disk access).
        string pseudoName = isDirectory ? "folder" : "file" + extension;
        uint fileAttributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        // SHGetFileInfo only yields 16 (small) and 32 (large). For larger targets, pull the matching system
        // image list (48 = extra-large, 256 = jumbo) by system icon index.
        if (requestPx > 32)
        {
            int imageList = requestPx <= 48 ? SHIL_EXTRALARGE : SHIL_JUMBO;
            var fromList = ResolveFromImageList(pseudoName, fileAttributes, imageList);
            if (fromList != null)
            {
                return fromList;
            }
        }

        uint sizeFlag = requestPx <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON;
        uint flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | sizeFlag;

        var info = new SHFILEINFO();
        IntPtr ok = SHGetFileInfo(pseudoName, fileAttributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return IconToImageSource(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static ImageSource? ResolveFromImageList(string pseudoName, uint fileAttributes, int imageList)
    {
        // SHGFI_SYSICONINDEX returns the system image list index without creating an HICON.
        var info = new SHFILEINFO();
        IntPtr res = SHGetFileInfo(pseudoName, fileAttributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES);
        if (res == IntPtr.Zero)
        {
            return null;
        }

        var iid = IID_IImageList;
        if (SHGetImageList(imageList, ref iid, out IImageList? list) != 0 || list == null)
        {
            return null;
        }

        if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out IntPtr hIcon) != 0 || hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return IconToImageSource(hIcon);
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static ImageSource? IconToImageSource(IntPtr hIcon)
    {
        if (GetIconInfo(hIcon, out ICONINFO iconInfo) == 0)
        {
            return null;
        }

        IntPtr colorBitmap = iconInfo.hbmColor;
        IntPtr maskBitmap = iconInfo.hbmMask;
        IntPtr deviceContext = IntPtr.Zero;
        try
        {
            if (colorBitmap == IntPtr.Zero)
            {
                return null;
            }

            var bitmap = new BITMAP();
            if (GetObject(colorBitmap, Marshal.SizeOf<BITMAP>(), ref bitmap) == 0)
            {
                return null;
            }

            int width = bitmap.bmWidth;
            int height = bitmap.bmHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var header = new BITMAPINFO();
            header.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            header.bmiHeader.biWidth = width;
            header.bmiHeader.biHeight = -height; // top-down rows
            header.bmiHeader.biPlanes = 1;
            header.bmiHeader.biBitCount = 32;
            header.bmiHeader.biCompression = BI_RGB;

            var pixels = new byte[width * height * 4];
            deviceContext = CreateCompatibleDC(IntPtr.Zero);
            int scanned = GetDIBits(deviceContext, colorBitmap, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS);
            if (scanned == 0)
            {
                return null;
            }

            // GetDIBits yields BGRA in memory. Legacy icons may carry an all-zero alpha channel; treat
            // those as opaque so they do not render fully transparent.
            bool hasAnyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAnyAlpha = true;
                    break;
                }
            }
            if (!hasAnyAlpha)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }
            return ImageSource.FromBgraPixels(width, height, pixels, hasAlpha: true);
        }
        finally
        {
            if (deviceContext != IntPtr.Zero)
            {
                DeleteDC(deviceContext);
            }
            if (colorBitmap != IntPtr.Zero)
            {
                DeleteObject(colorBitmap);
            }
            if (maskBitmap != IntPtr.Zero)
            {
                DeleteObject(maskBitmap);
            }
        }
    }
     
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const int SHIL_EXTRALARGE = 2;
    private const int SHIL_JUMBO = 4;
    private const uint ILD_TRANSPARENT = 0x00000001;
    private static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
    private const uint SHGSI_ICON = 0x000000100;
    private const uint SHGSI_LARGEICON = 0x000000000;
    private const uint SHGSI_SMALLICON = 0x000000001;
    private const uint SIID_FOLDER = 3;
    private const uint SIID_DRIVEFIXED = 8;
    private const uint RRF_RT_REG_SZ = 0x00000002;
    private const uint RRF_RT_REG_EXPAND_SZ = 0x00000004;
    private static readonly IntPtr HKEY_LOCAL_MACHINE = unchecked((IntPtr)(int)0x80000002);

    // Blittable layouts (fixed-byte string buffers, int BOOL) so the LibraryImport source generator can
    // marshal them with no runtime IL - NativeAOT friendly. The string buffers are unused here.
    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed byte szDisplayName[260 * 2];
        public fixed byte szTypeName[80 * 2];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        public fixed byte szPath[260 * 2];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon; // BOOL
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColorsPlaceholder;
    }

    // LibraryImport (unlike DllImport) does NOT auto-probe the A/W suffix, so name the exact Unicode exports.
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    private static partial int GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    private static partial int DestroyIcon(IntPtr hIcon);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObject(IntPtr handle, int count, ref BITMAP outBitmap);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr hdc, IntPtr hbmp, uint startScan, uint scanLines, byte[] bits, ref BITMAPINFO info, uint usage);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetImageList(int imageList, ref Guid riid, out IImageList? ppv);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ExtractIconEx(string file, int index, IntPtr[]? largeIcons, IntPtr[]? smallIcons, int iconCount);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetStockIconInfo(uint stockId, uint flags, ref SHSTOCKICONINFO info);

    [LibraryImport("advapi32.dll", EntryPoint = "RegGetValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegGetValue(IntPtr hkey, string subKey, string valueName, uint flags, out uint type, byte[]? data, ref uint dataLength);

    // Vtable order matters: GetIcon must sit after the 7 preceding IImageList methods.
    [GeneratedComInterface]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    internal partial interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int index);
        [PreserveSig] int ReplaceIcon(int index, IntPtr hicon, out int newIndex);
        [PreserveSig] int SetOverlayImage(int imageIndex, int overlayIndex);
        [PreserveSig] int Replace(int index, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, uint colorMask, out int index);
        [PreserveSig] int Draw(IntPtr drawParams);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, uint flags, out IntPtr hicon);
    }
}
