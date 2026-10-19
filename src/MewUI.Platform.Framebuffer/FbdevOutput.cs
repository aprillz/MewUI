using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// A Linux fbdev device mapped into memory. Frames are copied in by the CPU, so nothing is pending between
/// frames. There is no cursor plane, so the pointer is drawn into the mapped memory over the frame.
/// </summary>
internal sealed unsafe class FbdevOutput : IFramebufferOutput
{
    private const uint FALLBACK_REFRESH_HZ = 60;
    private const int MAX_CURSOR_SIZE = 64;

    private readonly int _fd;
    private readonly byte* _mappedBase;
    private readonly byte* _memory;
    private readonly nuint _mappedLength;
    private readonly int _lineLength;
    private readonly FbdevPixelLayout _layout;
    private readonly FbdevWindowSurface _surface;
    private readonly ConsoleGraphicsMode _consoleMode = ConsoleGraphicsMode.Enter();
    private readonly double _dpiScale;
    private readonly int _bytesPerPixel;
    private readonly Dictionary<CursorType, FramebufferCursorBitmap> _cursorImages = [];
    private FramebufferCursorBitmap? _cursorImage;
    private int _cursorX;
    private int _cursorY;
    private bool _waitForVSyncSupported = true;
    private bool _disposed;

    // The screen rectangle the pointer is drawn over, and the device pixels it covers there.
    private bool _cursorDrawn;
    private int _cursorLeft;
    private int _cursorTop;
    private int _cursorRight;
    private int _cursorBottom;
    private byte[] _cursorBackground = [];

    private FbdevOutput(string devicePath, int fd, byte* mappedBase, byte* memory, nuint mappedLength, int width, int height, int lineLength,
        FbdevPixelLayout layout, double dpiScale)
    {
        _fd = fd;
        _mappedBase = mappedBase;
        _memory = memory;
        _mappedLength = mappedLength;
        _lineLength = lineLength;
        _layout = layout;
        _dpiScale = dpiScale;
        _bytesPerPixel = layout == FbdevPixelLayout.Rgb565 ? 2 : 4;
        PixelWidth = width;
        PixelHeight = height;
        Description = $"fbdev {devicePath} {width}x{height} {layout} stride={lineLength}";
        _surface = new FbdevWindowSurface(this, dpiScale);
    }

    public string Description { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int RefreshHz => (int)FALLBACK_REFRESH_HZ;

    public bool FlipPending => false;

    public bool CanTakeFrame => true;

    public int EventFd => -1;

    public IWindowSurface WindowSurface => _surface;

    public static FbdevOutput? TryOpen(FramebufferPlatformOptions options, out string failure)
    {
        string path = options.FbDevicePath ?? Environment.GetEnvironmentVariable("FRAMEBUFFER") ?? "/dev/fb0";
        int fd = LibC.Open(path, LibC.O_RDWR | LibC.O_CLOEXEC);
        if (fd < 0)
        {
            failure = $"{path}: open failed (errno {Marshal.GetLastPInvokeError()})";
            return null;
        }

        FbVarScreenInfo variable;
        FbFixScreenInfo fixedInfo;
        if (LibC.Ioctl(fd, Fbdev.FBIOGET_VSCREENINFO, &variable) != 0)
        {
            LibC.Close(fd);
            failure = $"{path}: FBIOGET_VSCREENINFO failed";
            return null;
        }

        var layout = GetLayout(variable);
        if (layout == FbdevPixelLayout.Unsupported)
        {
            // Ask for 32 bpp; drivers that cannot switch keep their format.
            variable.BitsPerPixel = 32;
            LibC.Ioctl(fd, Fbdev.FBIOPUT_VSCREENINFO, &variable);
            LibC.Ioctl(fd, Fbdev.FBIOGET_VSCREENINFO, &variable);
            layout = GetLayout(variable);
            if (layout == FbdevPixelLayout.Unsupported)
            {
                LibC.Close(fd);
                failure = $"{path}: {variable.BitsPerPixel} bpp (red@{variable.Red.Offset}) is not BGRX, RGBX or RGB565";
                return null;
            }
        }

        if (LibC.Ioctl(fd, Fbdev.FBIOGET_FSCREENINFO, &fixedInfo) != 0)
        {
            LibC.Close(fd);
            failure = $"{path}: FBIOGET_FSCREENINFO failed";
            return null;
        }

        int width = (int)variable.XRes;
        int height = (int)variable.YRes;
        int lineLength = (int)fixedInfo.LineLength;
        nuint length = fixedInfo.SmemLen > 0
            ? fixedInfo.SmemLen
            : (nuint)((long)lineLength * Math.Max(variable.YResVirtual, variable.YRes));
        void* memory = LibC.Mmap(null, length, LibC.PROT_READ | LibC.PROT_WRITE, LibC.MAP_SHARED, fd, 0);
        if (memory == (void*)-1 || memory == null)
        {
            LibC.Close(fd);
            failure = $"{path}: mmap failed (errno {Marshal.GetLastPInvokeError()})";
            return null;
        }

        failure = string.Empty;
        byte* visible = (byte*)memory + (long)variable.YOffset * lineLength + variable.XOffset * (variable.BitsPerPixel / 8);
        return new FbdevOutput(path, fd, (byte*)memory, visible, length, width, height, lineLength, layout,
            options.DpiScale > 0 ? options.DpiScale : 1.0);
    }

    private static FbdevPixelLayout GetLayout(in FbVarScreenInfo variable)
    {
        if (variable.BitsPerPixel == 32 && variable.Green.Offset == 8)
        {
            if (variable.Red.Offset == 16 && variable.Blue.Offset == 0)
            {
                return FbdevPixelLayout.Bgrx;
            }

            if (variable.Red.Offset == 0 && variable.Blue.Offset == 16)
            {
                return FbdevPixelLayout.Rgbx;
            }
        }

        if (variable.BitsPerPixel == 16 && variable.Red.Offset == 11 && variable.Green.Offset == 5 && variable.Green.Length == 6
            && variable.Blue.Offset == 0)
        {
            return FbdevPixelLayout.Rgb565;
        }

        return FbdevPixelLayout.Unsupported;
    }

    public void DispatchEvents() { }

    public void SetCursor(CursorType? shape)
    {
        FramebufferCursorBitmap? image = null;
        if (shape != null && !_cursorImages.TryGetValue(shape.Value, out image))
        {
            image = FramebufferCursorImage.Create(shape.Value, _dpiScale, MAX_CURSOR_SIZE);
            _cursorImages[shape.Value] = image;
        }

        if (ReferenceEquals(image, _cursorImage))
        {
            return;
        }

        EraseCursor();
        _cursorImage = image;
        DrawCursor();
    }

    public void MoveCursor(int x, int y)
    {
        if (x == _cursorX && y == _cursorY)
        {
            return;
        }

        EraseCursor();
        _cursorX = x;
        _cursorY = y;
        DrawCursor();
    }

    private void EraseCursor()
    {
        if (!_cursorDrawn)
        {
            return;
        }

        _cursorDrawn = false;
        int width = _cursorRight - _cursorLeft;
        int rowBytes = width * _bytesPerPixel;
        for (int row = _cursorTop; row < _cursorBottom; row++)
        {
            _cursorBackground.AsSpan((row - _cursorTop) * rowBytes, rowBytes).CopyTo(DeviceSpan(_cursorLeft, row, width));
        }
    }

    private void DrawCursor()
    {
        var image = _cursorImage;
        if (image == null || _disposed)
        {
            return;
        }

        _cursorLeft = Math.Clamp(_cursorX - image.HotX, 0, PixelWidth);
        _cursorTop = Math.Clamp(_cursorY - image.HotY, 0, PixelHeight);
        _cursorRight = Math.Clamp(_cursorX - image.HotX + image.Width, 0, PixelWidth);
        _cursorBottom = Math.Clamp(_cursorY - image.HotY + image.Height, 0, PixelHeight);
        if (_cursorRight <= _cursorLeft || _cursorBottom <= _cursorTop)
        {
            return;
        }

        int length = (_cursorRight - _cursorLeft) * _bytesPerPixel * (_cursorBottom - _cursorTop);
        if (_cursorBackground.Length < length)
        {
            _cursorBackground = new byte[length];
        }

        _cursorDrawn = true;
        RefreshCursor(_cursorLeft, _cursorTop, _cursorRight, _cursorBottom);
    }

    /// <summary>
    /// Keeps what the screen now shows inside the given bounds as the background of the pointer there, then draws
    /// the pointer over it again.
    /// </summary>
    private void RefreshCursor(int left, int top, int right, int bottom)
    {
        var image = _cursorImage;
        left = Math.Max(left, _cursorLeft);
        top = Math.Max(top, _cursorTop);
        right = Math.Min(right, _cursorRight);
        bottom = Math.Min(bottom, _cursorBottom);
        if (!_cursorDrawn || image == null || right <= left || bottom <= top)
        {
            return;
        }

        int rowBytes = (_cursorRight - _cursorLeft) * _bytesPerPixel;
        int imageLeft = _cursorX - image.HotX;
        int imageTop = _cursorY - image.HotY;
        Span<byte> bgra = stackalloc byte[4];
        Span<byte> devicePixel = stackalloc byte[4];
        for (int row = top; row < bottom; row++)
        {
            var screen = DeviceSpan(left, row, right - left);
            screen.CopyTo(_cursorBackground.AsSpan((row - _cursorTop) * rowBytes + (left - _cursorLeft) * _bytesPerPixel));
            for (int column = left; column < right; column++)
            {
                uint argb = image.Pixels[(row - imageTop) * image.Width + column - imageLeft];
                uint alpha = argb >> 24;
                if (alpha == 0)
                {
                    continue;
                }

                var target = screen.Slice((column - left) * _bytesPerPixel, _bytesPerPixel);
                if (alpha < 255)
                {
                    argb = BlendOver(argb, alpha, ReadPixel(target));
                }

                BinaryPrimitives.WriteUInt32LittleEndian(bgra, argb);
                WriteRow(bgra, devicePixel);
                devicePixel[.._bytesPerPixel].CopyTo(target);
            }
        }
    }

    /// <summary>Premultiplied <paramref name="source"/> over an opaque BGRX pixel.</summary>
    private static uint BlendOver(uint source, uint alpha, uint destination)
    {
        uint remaining = 255 - alpha;
        uint blue = (source & 0xFF) + ((destination & 0xFF) * remaining + 127) / 255;
        uint green = ((source >> 8) & 0xFF) + (((destination >> 8) & 0xFF) * remaining + 127) / 255;
        uint red = ((source >> 16) & 0xFF) + (((destination >> 16) & 0xFF) * remaining + 127) / 255;
        return 0xFF000000u | Math.Min(red, 255) << 16 | Math.Min(green, 255) << 8 | Math.Min(blue, 255);
    }

    /// <summary>One device pixel as BGRX.</summary>
    private uint ReadPixel(ReadOnlySpan<byte> device)
    {
        switch (_layout)
        {
            case FbdevPixelLayout.Rgbx:
                return (uint)device[2] | (uint)device[1] << 8 | (uint)device[0] << 16;
            case FbdevPixelLayout.Rgb565:
                uint packed = BinaryPrimitives.ReadUInt16LittleEndian(device);
                uint red = packed >> 11;
                uint green = (packed >> 5) & 0x3F;
                uint blue = packed & 0x1F;
                return (blue << 3 | blue >> 2) | (green << 2 | green >> 4) << 8 | (red << 3 | red >> 2) << 16;
            default:
                return (uint)device[0] | (uint)device[1] << 8 | (uint)device[2] << 16;
        }
    }

    private Span<byte> DeviceSpan(int x, int y, int width)
        => new(_memory + (long)y * _lineLength + x * _bytesPerPixel, width * _bytesPerPixel);

    private void WriteRow(ReadOnlySpan<byte> bgra, Span<byte> destination)
    {
        switch (_layout)
        {
            case FbdevPixelLayout.Rgbx:
                FramebufferPixelCopy.SwapRedBlue(bgra, destination);
                break;
            case FbdevPixelLayout.Rgb565:
                FramebufferPixelCopy.ToRgb565(bgra, destination);
                break;
            default:
                bgra.CopyTo(destination);
                break;
        }
    }

    private void CopyRows(ReadOnlySpan<byte> bgra, int strideBytes, int x, int y, int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        int left = Math.Clamp(x, 0, PixelWidth);
        int top = Math.Clamp(y, 0, PixelHeight);
        int right = Math.Clamp(x + width, 0, PixelWidth);
        int bottom = Math.Clamp(y + height, 0, PixelHeight);
        int copyWidth = right - left;
        if (copyWidth <= 0 || bottom <= top)
        {
            return;
        }

        int sourceColumnOffset = (left - x) * 4;
        for (int row = top; row < bottom; row++)
        {
            var source = bgra.Slice((row - y) * strideBytes + sourceColumnOffset, copyWidth * 4);
            WriteRow(source, DeviceSpan(left, row, copyWidth));
        }

        // The rows just written replaced both the pointer and what was under it.
        RefreshCursor(left, top, right, bottom);
    }

    private void WaitForVerticalBlank()
    {
        if (!_waitForVSyncSupported || _disposed)
        {
            return;
        }

        uint screen = 0;
        if (LibC.Ioctl(_fd, Fbdev.FBIO_WAITFORVSYNC, &screen) != 0)
        {
            _waitForVSyncSupported = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        EraseCursor();
        _disposed = true;
        _consoleMode.Dispose();
        LibC.Munmap(_mappedBase, _mappedLength);
        LibC.Close(_fd);
    }

    private sealed class FbdevWindowSurface : ICpuFramebufferWindowSurface
    {
        private readonly FbdevOutput _output;

        public FbdevWindowSurface(FbdevOutput output, double dpiScale)
        {
            _output = output;
            DpiScale = dpiScale;
        }

        public nint Handle => FramebufferWindowBackend.WINDOW_HANDLE;

        public int PixelWidth => _output.PixelWidth;

        public int PixelHeight => _output.PixelHeight;

        public double DpiScale { get; }

        public PlatformDisplayIdentity DisplayIdentity => new(0, 0, _output._fd);

        public void CopyRows(ReadOnlySpan<byte> bgra, int strideBytes, int x, int y, int width, int height)
            => _output.CopyRows(bgra, strideBytes, x, y, width, height);

        public void WaitForVerticalBlank() => _output.WaitForVerticalBlank();
    }
}

/// <summary>Pixel layouts of an fbdev device the output can write.</summary>
internal enum FbdevPixelLayout
{
    Unsupported,
    Bgrx,
    Rgbx,
    Rgb565,
}

/// <summary>
/// Switches the virtual terminal to graphics mode so the text console stops drawing its cursor and messages over
/// the framebuffer, and back on dispose. Best effort: without access to a terminal nothing changes.
/// </summary>
internal sealed unsafe class ConsoleGraphicsMode : IDisposable
{
    private const nuint KDSETMODE = 0x4B3A;
    private const int KD_TEXT = 0;
    private const int KD_GRAPHICS = 1;

    private int _fd;

    private ConsoleGraphicsMode(int fd) => _fd = fd;

    public static ConsoleGraphicsMode Enter()
    {
        int fd = LibC.Open("/dev/tty0", LibC.O_RDWR | LibC.O_CLOEXEC);
        if (fd >= 0 && LibC.Ioctl(fd, KDSETMODE, (void*)KD_GRAPHICS) != 0)
        {
            LibC.Close(fd);
            fd = -1;
        }

        return new ConsoleGraphicsMode(fd);
    }

    public void Dispose()
    {
        if (_fd < 0)
        {
            return;
        }

        LibC.Ioctl(_fd, KDSETMODE, (void*)KD_TEXT);
        LibC.Close(_fd);
        _fd = -1;
    }
}
