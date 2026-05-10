using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Direct2D implementation of IPixelRenderSurface.
/// Uses DIB section + DC render target for offscreen rendering.
/// </summary>
internal sealed unsafe class Direct2DPixelRenderSurface : IPixelRenderSurface, IWin32HdcSource
{
    private readonly nint _dibSection;
    private readonly nint _oldBitmap;
    private readonly nint _dibBits;
    private readonly object _gate = new();
    private int _version;
    private bool _disposed;

    // The DC render target paired with our HDC + DIB. Lifetime is tied to this object —
    // RAII: we create lazily on first request, hold for as long as the bitmap is alive,
    // and release in Dispose. No factory-side cache (which previously leaked one DC RT
    // per transient filter source layer because each RT has a unique HDC, growing the
    // dict unboundedly until D2D / GPU memory OOMed).
    private nint _dcRenderTarget;
    private int _dcRenderTargetGeneration;
    private static int s_generationCounter;

    private byte[]? _lockBuffer;
    private Action? _releaseAction;

    public Direct2DPixelRenderSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        HasAlpha = hasAlpha;

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

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiScale { get; }
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;
    public int StrideBytes => PixelWidth * 4;
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// D2D internally renders premultiplied, but the DC render target's
    /// readable surface (the GDI DIB section it BindDCs to) ends up with
    /// straight-alpha bytes after AlphaBlend / GDI passes. Consumers that
    /// post-process these pixels (e.g. SVG Gaussian blur) treat them as
    /// straight to avoid double-divide artifacts at semi-transparent edges.
    /// </summary>
    public bool IsPremultiplied => false;

    /// <summary>
    /// Mirrors the alpha-channel hint from construction. Consumers that upload these pixels
    /// into a GPU image (e.g. <see cref="Direct2DImage"/>) read this to skip the alpha scan
    /// and pick <c>ALPHA_MODE.IGNORE</c> for opaque sources (video frames etc.).
    /// </summary>
    public bool HasAlpha { get; }

    nint IWin32HdcSource.Hdc => Hdc;

    /// <summary>
    /// Gets the memory device context for DC render target binding.
    /// </summary>
    internal nint Hdc { get; }
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

        byte b = color.B;
        byte g = color.G;
        byte r = color.R;
        byte a = color.A;

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
            throw new ObjectDisposedException(nameof(Direct2DPixelRenderSurface));
        }

        int size = PixelWidth * PixelHeight * 4;
        if (_lockBuffer == null || _lockBuffer.Length != size)
        {
            _lockBuffer = new byte[size];
        }

        unsafe
        {
            fixed (byte* dest = _lockBuffer)
            {
                Buffer.MemoryCopy((void*)_dibBits, dest, size, size);
            }
        }

        _releaseAction ??= () => Monitor.Exit(_gate);

        return new PixelBufferLock(
            _lockBuffer,
            PixelWidth,
            PixelHeight,
            StrideBytes,
            PixelFormat,
            _version,
            dirtyRegion: null,
            release: _releaseAction);
    }

    /// <inheritdoc/>
    public void IncrementVersion()
    {
        Interlocked.Increment(ref _version);
    }


    /// <summary>Returns the DC render target bound to this bitmap's HDC, creating it on
    /// first request. The DC RT lives for the bitmap's lifetime and is released in
    /// <see cref="Dispose"/>. <paramref name="d2dFactory"/> is the
    /// <c>ID2D1Factory</c> pointer that creates the DC RT (we don't own it; the caller
    /// keeps it alive). The generation field changes only when the underlying handle
    /// changes (currently never within a single bitmap RT lifetime), letting consumers
    /// cache resources keyed against it.</summary>
    internal (nint RenderTarget, int Generation) GetOrCreateDcRenderTarget(nint d2dFactory)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Direct2DPixelRenderSurface));
        }

        if (_dcRenderTarget != 0)
        {
            // BindDC must be called per BeginDraw cycle — the existing pattern. Only the
            // first BindDC binds the surface; subsequent calls simply confirm the binding
            // and clear any prior draw state. Cheap.
            var rebindRect = new RECT(0, 0, PixelWidth, PixelHeight);
            int rebindHr = D2D1VTable.BindDC((ID2D1DCRenderTarget*)_dcRenderTarget, Hdc, ref rebindRect);
            if (rebindHr >= 0)
            {
                return (_dcRenderTarget, _dcRenderTargetGeneration);
            }
            // BindDC failed — release and recreate below.
            ComHelpers.Release(_dcRenderTarget);
            _dcRenderTarget = 0;
        }

        var pixelFormat = new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.PREMULTIPLIED);
        float dpi = (float)(96.0 * DpiScale);
        var rtProps = new D2D1_RENDER_TARGET_PROPERTIES(D2D1_RENDER_TARGET_TYPE.DEFAULT, pixelFormat, dpi, dpi, 0, 0);

        int hr = D2D1VTable.CreateDcRenderTarget((ID2D1Factory*)d2dFactory, ref rtProps, out _dcRenderTarget);
        if (hr < 0 || _dcRenderTarget == 0)
        {
            throw new InvalidOperationException($"CreateDcRenderTarget failed: 0x{hr:X8}");
        }

        var rect = new RECT(0, 0, PixelWidth, PixelHeight);
        hr = D2D1VTable.BindDC((ID2D1DCRenderTarget*)_dcRenderTarget, Hdc, ref rect);
        if (hr < 0)
        {
            ComHelpers.Release(_dcRenderTarget);
            _dcRenderTarget = 0;
            throw new InvalidOperationException($"ID2D1DCRenderTarget::BindDC failed: 0x{hr:X8}");
        }

        _dcRenderTargetGeneration = Interlocked.Increment(ref s_generationCounter);
        return (_dcRenderTarget, _dcRenderTargetGeneration);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // DC RT must be released BEFORE the HDC it's bound to is destroyed; otherwise
        // D2D's later cleanup hits an invalid HDC.
        if (_dcRenderTarget != 0)
        {
            ComHelpers.Release(_dcRenderTarget);
            _dcRenderTarget = 0;
        }

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
