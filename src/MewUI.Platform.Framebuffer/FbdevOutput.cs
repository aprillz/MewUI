using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// A Linux fbdev device mapped into memory. Frames are copied in by the CPU, so nothing is pending between
/// frames and there is no cursor plane.
/// </summary>
internal sealed unsafe class FbdevOutput : IFramebufferOutput
{
    private const uint FALLBACK_REFRESH_HZ = 60;

    private readonly int _fd;
    private readonly byte* _mappedBase;
    private readonly byte* _memory;
    private readonly nuint _mappedLength;
    private readonly int _lineLength;
    private readonly FbdevPixelLayout _layout;
    private readonly FbdevWindowSurface _surface;
    private readonly ConsoleGraphicsMode _consoleMode = ConsoleGraphicsMode.Enter();
    private bool _waitForVSyncSupported = true;
    private bool _disposed;

    private FbdevOutput(string devicePath, int fd, byte* mappedBase, byte* memory, nuint mappedLength, int width, int height, int lineLength,
        FbdevPixelLayout layout, double dpiScale)
    {
        _fd = fd;
        _mappedBase = mappedBase;
        _memory = memory;
        _mappedLength = mappedLength;
        _lineLength = lineLength;
        _layout = layout;
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

    public void SetCursorVisible(bool visible) { }

    public void MoveCursor(int x, int y) { }

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
        int bytesPerPixel = _layout == FbdevPixelLayout.Rgb565 ? 2 : 4;
        for (int row = top; row < bottom; row++)
        {
            var source = bgra.Slice((row - y) * strideBytes + sourceColumnOffset, copyWidth * 4);
            var destination = new Span<byte>(_memory + (long)row * _lineLength + left * bytesPerPixel, copyWidth * bytesPerPixel);
            switch (_layout)
            {
                case FbdevPixelLayout.Rgbx:
                    FramebufferPixelCopy.SwapRedBlue(source, destination);
                    break;
                case FbdevPixelLayout.Rgb565:
                    FramebufferPixelCopy.ToRgb565(source, destination);
                    break;
                default:
                    source.CopyTo(destination);
                    break;
            }
        }
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
