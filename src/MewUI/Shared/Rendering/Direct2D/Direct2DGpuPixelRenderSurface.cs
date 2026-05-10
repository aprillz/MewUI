using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// GPU-only D2D bitmap target — wraps an <c>ID2D1Bitmap1</c> created with
/// <c>D2D1_BITMAP_OPTIONS.TARGET</c>. No GDI DIB section is allocated; pixels live
/// exclusively in GPU memory and are sampled by effects / drawn directly via the
/// shared device context. Counterpart of MewVG's <c>OpenGLPixelRenderSurface</c>.
/// </summary>
/// <remarks>
/// Used by <see cref="Direct2DImageFilterExecutor"/> for source layers and scratch
/// buffers when the active backend supports the GPU pipeline. Falls back to the
/// DIB-backed <see cref="Direct2DPixelRenderSurface"/> for layered window compositing
/// (which legitimately needs CPU access to the pixels).
/// <para/>
/// Lifetime: the wrapped <c>ID2D1Bitmap1</c> is created against the factory's shared
/// <see cref="Direct2DGraphicsFactory.SharedFilterDeviceContext"/>. Cross-DC bitmap usage
/// is undefined in D2D, so all filter operations on this target must go through the same
/// shared DC.
/// </remarks>
internal sealed unsafe class Direct2DGpuPixelRenderSurface : IPixelBufferSource, ICpuPixelSurface, IDeferredCpuReadableSurface, IDisposable, ID2DTextureSource
    , IReusableScratchRenderTarget
{
    private readonly Direct2DGraphicsFactory _factory;
    private readonly object _gate = new();
    private nint _bitmap;
    private nint _readbackBitmap;       // CANNOT_DRAW | CPU_READ staging copy, lazy
    private byte[]? _readbackBuffer;    // cached managed buffer for Lock()
    private int _readbackVersion = -1;
    private int _version;
    private bool _deviceLost;
    private bool _disposed;

    public Direct2DGpuPixelRenderSurface(Direct2DGraphicsFactory factory, int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha = true)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        _factory = factory;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        HasAlpha = hasAlpha;

        nint dc = factory.SharedFilterDeviceContext;
        if (dc == 0)
        {
            throw new InvalidOperationException("Shared D2D filter device context is unavailable; D3D11/D2D1 device init failed.");
        }

        // Opaque RTs (video frames, JPEG-backed bitmaps, etc.) get ALPHA_MODE.IGNORE so the
        // GPU skips per-fragment blend math when this RT is later sampled. PREMULTIPLIED is
        // required for alpha-bearing content because D2D effects (and CPU executors that read
        // the bitmap back) assume premultiplied input.
        var alphaMode = hasAlpha ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;
        var pixelFormat = new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode);
        float dpi = (float)(96.0 * dpiScale);
        var props = new D2D1_BITMAP_PROPERTIES1(pixelFormat, dpi, dpi, D2D1_BITMAP_OPTIONS.TARGET, 0);
        var size = new D2D1_SIZE_U((uint)pixelWidth, (uint)pixelHeight);

        int hr = D2D1VTable.CreateBitmap1(
            (ID2D1DeviceContext*)dc,
            size,
            ReadOnlySpan<byte>.Empty,
            0,
            props,
            out _bitmap);
        if (hr < 0 || _bitmap == 0)
        {
            throw new InvalidOperationException($"ID2D1DeviceContext::CreateBitmap (TARGET) failed: 0x{hr:X8}");
        }

        _factory.RegisterGpuBitmapTarget(this);
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiScale { get; }
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;
    public int StrideBytes => PixelWidth * 4;
    public int Version => Volatile.Read(ref _version);

    /// <summary>D2D effects expect premultiplied alpha; the bitmap is created PREMULTIPLIED.</summary>
    public bool IsPremultiplied => true;

    /// <summary>GPU bitmap — <see cref="Lock"/> stages a CPU-readable copy via
    /// CopyFromBitmap and maps the staging texture, blocking on GPU completion.</summary>
    public LockMode LockMode => LockMode.Readback;

    /// <summary>
    /// Mirrors the alpha mode the underlying ID2D1Bitmap1 was created with. Consumers that
    /// upload these pixels into another backend image (e.g. <see cref="Direct2DImage"/>) read
    /// this to skip premultiply scans and select <c>ALPHA_MODE.IGNORE</c> for opaque sources.
    /// </summary>
    public bool HasAlpha { get; }

    RenderPixelFormat IRenderSurface.Format => IsPremultiplied
        ? RenderPixelFormat.Bgra8888Premultiplied
        : RenderPixelFormat.Bgra8888;

    SurfaceUsage IRenderSurface.Usage =>
        SurfaceUsage.Offscreen | SurfaceUsage.ImageSource | SurfaceUsage.ReadbackSource;

    SurfaceCapabilities IRenderSurface.Capabilities
    {
        get
        {
            var capabilities =
                SurfaceCapabilities.Renderable |
                SurfaceCapabilities.CpuReadable |
                SurfaceCapabilities.CpuWritable |
                SurfaceCapabilities.Alpha;

            if (IsPremultiplied)
            {
                capabilities |= SurfaceCapabilities.Premultiplied;
            }

            if (LockMode == LockMode.Readback)
            {
                capabilities |= SurfaceCapabilities.DeferredReadback;
            }

            if (this is IGpuTextureSource)
            {
                capabilities |= SurfaceCapabilities.GpuSampleable;
            }

            return capabilities;
        }
    }

    ulong IRenderSurface.Version => (ulong)Math.Max(0, Version);

    bool IRenderSurface.IsDisposed => _disposed;

    ReadOnlySpan<byte> ICpuPixelSurface.GetReadOnlyPixelSpan() => GetPixelSpan();

    Span<byte> ICpuPixelSurface.GetWritablePixelSpan() => GetPixelSpan();

    bool IDeferredCpuReadableSurface.HasPendingReadback => LockMode == LockMode.Readback;

    IRenderOperation IDeferredCpuReadableSurface.RequestReadback()
    {
        if (LockMode == LockMode.Readback)
        {
            _ = CopyPixels();
        }

        return RenderOperation.Completed;
    }

    bool IDeferredCpuReadableSurface.TryFlushReadback()
    {
        if (LockMode == LockMode.Readback)
        {
            _ = CopyPixels();
        }

        return true;
    }

    internal bool IsDeviceCurrent
        => !_disposed && !_deviceLost && _bitmap != 0;

    bool IReusableScratchRenderTarget.CanReturnToPool => IsDeviceCurrent;

    // ID2DTextureSource — exposes the GPU bitmap via the backend marker so D2D consumers
    // can short-circuit the CPU readback path when source and consumer share a device.
    nint ID2DTextureSource.NativeBitmap => IsDeviceCurrent ? _bitmap : 0;
    nint ID2DTextureSource.OwningDeviceContext => IsDeviceCurrent ? _factory.SharedFilterDeviceContext : 0;
    BitmapAlphaMode ID2DTextureSource.AlphaMode
        => HasAlpha ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore;

    /// <summary>The wrapped <c>ID2D1Bitmap1*</c>. Used by the executor to chain effects without
    /// any CPU readback. Returns 0 when disposed.</summary>
    internal nint Bitmap => IsDeviceCurrent ? _bitmap : 0;

    public void IncrementVersion() => Interlocked.Increment(ref _version);

    public byte[] CopyPixels()
    {
        var bytes = ReadbackToBuffer();
        if (bytes.Length == 0) return Array.Empty<byte>();
        var copy = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
        return copy;
    }

    public Span<byte> GetPixelSpan() => ReadbackToBuffer().AsSpan();

    public PixelBufferLock Lock()
    {
        Monitor.Enter(_gate);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Direct2DGpuPixelRenderSurface));
            ThrowIfStaleDevice();
            var bytes = ReadbackToBufferUnderLock();
            // Read-only release: just drop the gate. We previously did an unconditional
            // CopyFromMemory + IncrementVersion in the release path so CPU mutations to
            // the lock buffer would propagate back to the GPU bitmap, but in practice
            // every Lock caller in the codebase is read-only (Direct2DImage's mip rebuild
            // for instance), and the unconditional version bump created a feedback loop:
            // each draw triggered Lock → version++ → next draw saw version mismatch →
            // re-readback → Lock → ... [SvgView pan with stable cache logged a SLOW mip
            // rebuild every frame]. CPU-side mutations now go through an explicit
            // <see cref="CommitWritesFromBuffer"/> API which the (currently nonexistent)
            // mutating consumers would call. Add it back the day a CPU executor needs it.
            Action release = () => Monitor.Exit(_gate);
            return new PixelBufferLock(
                bytes, PixelWidth, PixelHeight, StrideBytes,
                PixelFormat, _version, dirtyRegion: null, release);
        }
        catch
        {
            Monitor.Exit(_gate);
            throw;
        }
    }

    /// <summary>Pushes the CPU-side <c>_readbackBuffer</c> back into the GPU bitmap and
    /// bumps the version. Intended for the rare CPU executor path that mutates pixels
    /// through <see cref="Lock"/>'s buffer (or <see cref="GetPixelSpan"/>) and needs
    /// the changes visible to subsequent GPU draws. Default <see cref="Lock"/> release
    /// does NOT call this — it's read-only.</summary>
    public void CommitWritesFromBuffer()
    {
        lock (_gate)
        {
            if (_disposed || !IsDeviceCurrent || _readbackBuffer is null) return;
            D2D1VTable.CopyFromMemory(_bitmap, _readbackBuffer, (uint)StrideBytes);
            Interlocked.Increment(ref _version);
            _readbackVersion = _version;
        }
    }

    /// <summary>Public-API readback wrapper — locks, copies, releases.</summary>
    private byte[] ReadbackToBuffer()
    {
        lock (_gate)
        {
            return ReadbackToBufferUnderLock();
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>. Copies the GPU bitmap to a staging
    /// CPU_READ bitmap, maps it, and caches the result in a managed buffer keyed by
    /// version. Subsequent calls without an intervening <see cref="IncrementVersion"/>
    /// reuse the cached bytes (zero re-readback).</summary>
    private byte[] ReadbackToBufferUnderLock()
    {
        if (_disposed || !IsDeviceCurrent) return Array.Empty<byte>();
        int currentVersion = _version;
        if (_readbackBuffer != null && _readbackVersion == currentVersion)
        {
            return _readbackBuffer;
        }

        nint dc = _factory.SharedFilterDeviceContext;
        if (dc == 0) return Array.Empty<byte>();

        if (_readbackBitmap == 0)
        {
            // Staging: CANNOT_DRAW | CPU_READ — usable as a CopyFromBitmap destination and
            // mappable for CPU read. Alpha mode mirrors the source so the staging copy stays
            // straight bytes-through (D2D refuses CopyFromBitmap across mismatched modes).
            var stagingAlphaMode = HasAlpha ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;
            var pixelFormat = new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, stagingAlphaMode);
            float dpi = (float)(96.0 * DpiScale);
            var props = new D2D1_BITMAP_PROPERTIES1(pixelFormat, dpi, dpi,
                D2D1_BITMAP_OPTIONS.CANNOT_DRAW | D2D1_BITMAP_OPTIONS.CPU_READ, 0);
            var size = new D2D1_SIZE_U((uint)PixelWidth, (uint)PixelHeight);
            int hr = D2D1VTable.CreateBitmap1(
                (ID2D1DeviceContext*)dc, size, ReadOnlySpan<byte>.Empty, 0, props, out _readbackBitmap);
            if (hr < 0 || _readbackBitmap == 0) return Array.Empty<byte>();
        }

        // GPU copy main bitmap → staging bitmap (single GPU op + implicit sync inside Map).
        int copyHr = D2D1VTable.CopyFromBitmap(_readbackBitmap, _bitmap);
        if (copyHr < 0) return Array.Empty<byte>();

        int mapHr = D2D1VTable.MapBitmap(_readbackBitmap, D2D1_MAP_OPTIONS.READ, out var mapped);
        if (mapHr < 0 || mapped.bits == 0) return Array.Empty<byte>();

        try
        {
            int byteCount = PixelWidth * PixelHeight * 4;
            if (_readbackBuffer == null || _readbackBuffer.Length != byteCount)
            {
                _readbackBuffer = new byte[byteCount];
            }
            // Source pitch may include padding (mapped.pitch ≥ width*4); copy row-by-row to
            // produce a tightly packed managed buffer regardless.
            int rowBytes = PixelWidth * 4;
            byte* src = (byte*)mapped.bits;
            uint pitch = mapped.pitch;
            fixed (byte* dst = _readbackBuffer)
            {
                if (pitch == (uint)rowBytes)
                {
                    Buffer.MemoryCopy(src, dst, byteCount, byteCount);
                }
                else
                {
                    for (int y = 0; y < PixelHeight; y++)
                    {
                        Buffer.MemoryCopy(src + y * pitch, dst + y * rowBytes, rowBytes, rowBytes);
                    }
                }
            }
            _readbackVersion = currentVersion;
            return _readbackBuffer;
        }
        finally
        {
            D2D1VTable.UnmapBitmap(_readbackBitmap);
        }
    }

    public void Clear(Color color)
    {
        if (!IsDeviceCurrent)
        {
            return;
        }

        // Clear is handled at BeginDraw time by the graphics context (it issues the GL/D2D
        // clear). No CPU side-effect needed here; just bump the version so consumers see
        // the change.
        IncrementVersion();
    }

    private void ThrowIfStaleDevice()
    {
        if (!IsDeviceCurrent)
        {
            throw new InvalidOperationException("GPU bitmap target is stale after Direct2D device loss. Recreate the render target.");
        }
    }

    internal void NotifyDeviceLost()
    {
        lock (_gate)
        {
            _deviceLost = true;
            if (_readbackBitmap != 0)
            {
                ComHelpers.Release(_readbackBitmap);
                _readbackBitmap = 0;
            }
            _readbackBuffer = null;
            _readbackVersion = -1;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bitmap != 0)
        {
            ComHelpers.Release(_bitmap);
            _bitmap = 0;
        }
        if (_readbackBitmap != 0)
        {
            ComHelpers.Release(_readbackBitmap);
            _readbackBitmap = 0;
        }
        _readbackBuffer = null;
    }
}
