using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI implementation of IBitmapRenderTarget.
/// Manages DIB section and memory DC for offscreen rendering.
/// </summary>
internal sealed class GdiBitmapRenderTarget : IBitmapRenderTarget
{
    private readonly nint _dibSection;
    private readonly nint _oldBitmap;
    private readonly nint _dibBits;
    private readonly object _gate = new();
    private int _version;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiScale { get; }
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;
    public int StrideBytes => PixelWidth * 4;
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Gets the memory device context for rendering.
    /// </summary>
    internal nint Hdc { get; }

    public GdiBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale, GdiPresentationMode presentationMode = GdiPresentationMode.Default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        PresentationMode = presentationMode;

        // Create memory DC
        var screenDc = User32.GetDC(0);
        Hdc = Gdi32.CreateCompatibleDC(screenDc);
        User32.ReleaseDC(0, screenDc);

        if (Hdc == 0)
        {
            throw new InvalidOperationException("Failed to create memory DC for bitmap render target.");
        }

        // Create DIB section
        var bmi = BITMAPINFO.Create32bpp(pixelWidth, pixelHeight);
        _dibSection = Gdi32.CreateDIBSection(Hdc, ref bmi, 0, out _dibBits, 0, 0);

        if (_dibSection == 0 || _dibBits == 0)
        {
            Gdi32.DeleteDC(Hdc);
            throw new InvalidOperationException("Failed to create DIB section for bitmap render target.");
        }

        _oldBitmap = Gdi32.SelectObject(Hdc, _dibSection);
    }

    internal GdiPresentationMode PresentationMode { get; }

    public byte[] CopyPixels()
    {
        if (_disposed || _dibBits == 0)
        {
            return Array.Empty<byte>();
        }

        int byteCount = PixelWidth * PixelHeight * 4;
        var copy = new byte[byteCount];

        unsafe
        {
            fixed (byte* dest = copy)
            {
                Buffer.MemoryCopy((void*)_dibBits, dest, byteCount, byteCount);
            }
        }

        return copy;
    }

    public unsafe Span<byte> GetPixelSpan()
    {
        if (_disposed || _dibBits == 0)
        {
            return Span<byte>.Empty;
        }

        return new Span<byte>((void*)_dibBits, PixelWidth * PixelHeight * 4);
    }

    public void Clear(Color color)
    {
        if (_disposed || _dibBits == 0)
        {
            return;
        }

        byte a = color.A;
        byte r = (byte)((color.R * a + 127) / 255);
        byte g = (byte)((color.G * a + 127) / 255);
        byte b = (byte)((color.B * a + 127) / 255);

        int pixelCount = PixelWidth * PixelHeight;
        unsafe
        {
            byte* p = (byte*)_dibBits;
            for (int i = 0; i < pixelCount; i++)
            {
                *p++ = b;
                *p++ = g;
                *p++ = r;
                *p++ = a;
            }
        }

        IncrementVersion();
    }

    public PixelBufferLock Lock()
    {
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(GdiBitmapRenderTarget));
        }

        // Copy from unmanaged DIB bits to managed array
        var buffer = CopyPixels();
        int v = _version;

        return new PixelBufferLock(
            buffer,
            PixelWidth,
            PixelHeight,
            StrideBytes,
            PixelFormat,
            v,
            dirtyRegion: null,
            release: () => Monitor.Exit(_gate));
    }

    /// <inheritdoc/>
    public void IncrementVersion()
    {
        Interlocked.Increment(ref _version);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_oldBitmap != 0 && Hdc != 0)
        {
            Gdi32.SelectObject(Hdc, _oldBitmap);
        }

        if (_dibSection != 0)
        {
            Gdi32.DeleteObject(_dibSection);
        }

        if (Hdc != 0)
        {
            Gdi32.DeleteDC(Hdc);
        }
    }
}
