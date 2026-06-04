using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.Framebuffer;

public sealed unsafe class LinuxFramebuffer : IDisposable
{
    private readonly int _fd;
    private FbFixScreenInfo _fixedInfo;
    private FbVarScreenInfo _screenInfo;
    private nint _mappedLength;
    private nint _mappedAddress;
    private bool _disposed;

    private LinuxFramebuffer(int fd, string devicePath)
    {
        _fd = fd;
        DevicePath = devicePath;
    }

    public string DevicePath { get; }

    public int PixelWidth => checked((int)_screenInfo.xres);

    public int PixelHeight => checked((int)_screenInfo.yres);

    public int StrideBytes => checked((int)_fixedInfo.line_length);

    public int BitsPerPixel => checked((int)_screenInfo.bits_per_pixel);

    public FramebufferColorOrder ColorOrder =>
        _screenInfo.red.offset == 16 && _screenInfo.green.offset == 8 && _screenInfo.blue.offset == 0
            ? FramebufferColorOrder.Bgra
            : _screenInfo.red.offset == 0 && _screenInfo.green.offset == 8 && _screenInfo.blue.offset == 16
                ? FramebufferColorOrder.Rgba
                : FramebufferColorOrder.Unknown;

    public static LinuxFramebuffer Open(FramebufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        int fd = Libc.open(options.DevicePath, Libc.O_RDWR, 0);
        if (fd < 0)
        {
            throw new InvalidOperationException($"Failed to open framebuffer device '{options.DevicePath}' ({Marshal.GetLastPInvokeError()}).");
        }

        var framebuffer = new LinuxFramebuffer(fd, options.DevicePath);
        try
        {
            framebuffer.Initialize(options.Force32BitsPerPixel);
            return framebuffer;
        }
        catch
        {
            framebuffer.Dispose();
            throw;
        }
    }

    public void Present(FramebufferRenderSurface surface, bool waitForVSync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);

        if (surface.PixelWidth != PixelWidth || surface.PixelHeight != PixelHeight)
        {
            throw new InvalidOperationException($"Surface size {surface.PixelWidth}x{surface.PixelHeight} does not match framebuffer size {PixelWidth}x{PixelHeight}.");
        }

        if (waitForVSync)
        {
            VSync();
        }

        var source = surface.GetReadOnlyPixelSpan();
        fixed (byte* sourcePtr = source)
        {
            CopyToMappedFramebuffer((nint)sourcePtr, surface.StrideBytes, surface.PixelWidth, surface.PixelHeight);
        }
    }

    public void VSync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int crtc = 0;
        _ = Libc.ioctl(_fd, FbIoCtl.FBIO_WAITFORVSYNC, &crtc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mappedAddress != 0 && _mappedAddress != -1)
        {
            _ = Libc.munmap(_mappedAddress, _mappedLength);
            _mappedAddress = 0;
        }

        if (_fd >= 0)
        {
            _ = Libc.close(_fd);
        }
    }

    private void Initialize(bool force32BitsPerPixel)
    {
        fixed (FbVarScreenInfo* pScreenInfo = &_screenInfo)
        {
            if (Libc.ioctl(_fd, FbIoCtl.FBIOGET_VSCREENINFO, pScreenInfo) == -1)
            {
                throw new InvalidOperationException($"FBIOGET_VSCREENINFO failed ({Marshal.GetLastPInvokeError()}).");
            }

            if (force32BitsPerPixel)
            {
                Set32BitsPixelFormat();
                _ = Libc.ioctl(_fd, FbIoCtl.FBIOPUT_VSCREENINFO, pScreenInfo);
                if (Libc.ioctl(_fd, FbIoCtl.FBIOGET_VSCREENINFO, pScreenInfo) == -1)
                {
                    throw new InvalidOperationException($"FBIOGET_VSCREENINFO failed after mode set ({Marshal.GetLastPInvokeError()}).");
                }
            }

            if (_screenInfo.bits_per_pixel != 32)
            {
                throw new NotSupportedException($"Only 32bpp framebuffer mode is supported by this backend. Current bpp: {_screenInfo.bits_per_pixel}.");
            }
        }

        fixed (FbFixScreenInfo* pFixedInfo = &_fixedInfo)
        {
            if (Libc.ioctl(_fd, FbIoCtl.FBIOGET_FSCREENINFO, pFixedInfo) == -1)
            {
                throw new InvalidOperationException($"FBIOGET_FSCREENINFO failed ({Marshal.GetLastPInvokeError()}).");
            }
        }

        ulong mappedLength = (ulong)_fixedInfo.line_length * _screenInfo.yres;
        if (mappedLength > nuint.MaxValue)
        {
            throw new InvalidOperationException($"Framebuffer mapping is too large for this process ({mappedLength} bytes).");
        }

        _mappedLength = (nint)(nuint)mappedLength;
        var mappedAddress = Libc.mmap(0, _mappedLength, Libc.PROT_READ | Libc.PROT_WRITE, Libc.MAP_SHARED, _fd, 0);
        if (mappedAddress == -1)
        {
            throw new InvalidOperationException($"mmap failed for framebuffer ({Marshal.GetLastPInvokeError()}).");
        }

        _mappedAddress = mappedAddress;
    }

    private void Set32BitsPixelFormat()
    {
        _screenInfo.bits_per_pixel = 32;
        _screenInfo.grayscale = 0;
        _screenInfo.red = new FbBitfield { offset = 16, length = 8 };
        _screenInfo.green = new FbBitfield { offset = 8, length = 8 };
        _screenInfo.blue = new FbBitfield { offset = 0, length = 8 };
        _screenInfo.transp = new FbBitfield { offset = 24, length = 8 };
    }

    private void CopyToMappedFramebuffer(nint source, int sourceStride, int width, int height)
    {
        int rowBytes = checked(width * 4);
        if (ColorOrder == FramebufferColorOrder.Bgra)
        {
            if (StrideBytes == sourceStride && rowBytes == sourceStride)
            {
                _ = Libc.memcpy(_mappedAddress, source, (nint)(rowBytes * height));
                return;
            }

            for (int y = 0; y < height; y++)
            {
                _ = Libc.memcpy(_mappedAddress + y * StrideBytes, source + y * sourceStride, (nint)rowBytes);
            }

            return;
        }

        ConvertBgraToFramebuffer(source, sourceStride, width, height);
    }

    private void ConvertBgraToFramebuffer(nint source, int sourceStride, int width, int height)
    {
        byte* src = (byte*)source;
        byte* dst = (byte*)_mappedAddress;
        for (int y = 0; y < height; y++)
        {
            byte* s = src + y * sourceStride;
            byte* d = dst + y * StrideBytes;
            for (int x = 0; x < width; x++)
            {
                byte b = s[x * 4 + 0];
                byte g = s[x * 4 + 1];
                byte r = s[x * 4 + 2];
                byte a = s[x * 4 + 3];

                if (ColorOrder == FramebufferColorOrder.Rgba)
                {
                    d[x * 4 + 0] = r;
                    d[x * 4 + 1] = g;
                    d[x * 4 + 2] = b;
                    d[x * 4 + 3] = a;
                }
                else
                {
                    WriteByBitfields(d + x * 4, r, g, b, a);
                }
            }
        }
    }

    private void WriteByBitfields(byte* pixel, byte r, byte g, byte b, byte a)
    {
        uint value = 0;
        value |= ((uint)r << (int)_screenInfo.red.offset);
        value |= ((uint)g << (int)_screenInfo.green.offset);
        value |= ((uint)b << (int)_screenInfo.blue.offset);
        if (_screenInfo.transp.length > 0)
        {
            value |= ((uint)a << (int)_screenInfo.transp.offset);
        }

        *(uint*)pixel = value;
    }
}

public enum FramebufferColorOrder
{
    Unknown,
    Bgra,
    Rgba,
}

internal enum FbIoCtl : uint
{
    FBIOGET_VSCREENINFO = 0x4600,
    FBIOPUT_VSCREENINFO = 0x4601,
    FBIOGET_FSCREENINFO = 0x4602,
    FBIO_WAITFORVSYNC = 0x40044620,
}

[StructLayout(LayoutKind.Sequential)]
internal struct FbBitfield
{
    public uint offset;
    public uint length;
    public uint msb_right;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FbVarScreenInfo
{
    public uint xres;
    public uint yres;
    public uint xres_virtual;
    public uint yres_virtual;
    public uint xoffset;
    public uint yoffset;
    public uint bits_per_pixel;
    public uint grayscale;
    public FbBitfield red;
    public FbBitfield green;
    public FbBitfield blue;
    public FbBitfield transp;
    public uint nonstd;
    public uint activate;
    public uint height;
    public uint width;
    public uint accel_flags;
    public uint pixclock;
    public uint left_margin;
    public uint right_margin;
    public uint upper_margin;
    public uint lower_margin;
    public uint hsync_len;
    public uint vsync_len;
    public uint sync;
    public uint vmode;
    public uint rotate;
    public uint colorspace;
    public fixed uint reserved[4];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct FbFixScreenInfo
{
    public fixed byte id[16];
    public nuint smem_start;
    public uint smem_len;
    public uint type;
    public uint type_aux;
    public uint visual;
    public ushort xpanstep;
    public ushort ypanstep;
    public ushort ywrapstep;
    public uint line_length;
    public nuint mmio_start;
    public uint mmio_len;
    public uint accel;
    public ushort capabilities;
    public fixed ushort reserved[2];
}

internal static unsafe partial class Libc
{
    public const int O_RDWR = 0x0002;
    public const int PROT_READ = 0x1;
    public const int PROT_WRITE = 0x2;
    public const int MAP_SHARED = 0x01;

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int open(string pathname, int flags, int mode);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int ioctl(int fd, FbIoCtl code, void* arg);

    [LibraryImport("libc", SetLastError = true)]
    public static partial nint mmap(nint addr, nint length, int prot, int flags, int fd, nint offset);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int munmap(nint addr, nint length);

    [LibraryImport("libc", SetLastError = true)]
    public static partial nint memcpy(nint dest, nint src, nint length);
}
