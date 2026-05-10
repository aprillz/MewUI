using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// GPU-backed pixel render surface for the Metal backend. Holds a
/// shared-storage MTLTexture so an offscreen <see cref="MewVGMetalGraphicsContext"/>
/// can render into it directly, then exposes the rendered pixels through the
/// CPU-readable pixel surface (used by filter /
/// pattern uploads, WriteableBitmap-backed controls, etc.).
/// </summary>
internal sealed unsafe partial class MewVGMetalPixelRenderSurface : IPixelBufferSource, ICpuPixelSurface, IDeferredCpuReadableSurface, IDisposable, IMetalTextureSource
{
    // -[MTLTexture getBytes:bytesPerRow:fromRegion:mipmapLevel:]
    // MTLRegion is 48 bytes (3 NSInteger origin + 3 NSInteger size). On both
    // ARM64 and x86_64 macOS ABIs, composites larger than 16 bytes are passed
    // BY POINTER (caller copies onto its stack and passes the address). The
    // earlier attempt that inlined the six fields as separate args caused a
    // crash inside getBytes because the receiver/selector arguments were
    // shifted out of position.
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static unsafe partial void GetBytesMsgSend(
        nint receiver, nint selector,
        void* bytes, nuint bytesPerRow,
        void* regionPtr,
        nuint mipmapLevel);

    // MTLTextureUsage: ShaderRead = 1<<0, ShaderWrite = 1<<1, RenderTarget = 1<<2.
    // ShaderWrite is required so MPS / compute kernels (e.g. MPSImageGaussianBlur in
    // MetalImageFilterExecutor) can write into the color texture. Without it MPS encode
    // silently fails on production drivers (no error returned), leaving the destination
    // unchanged — visible as stale content / disappearing filtered regions on zoom.
    private const ulong MTLTextureUsageRenderTargetShaderRead = (1ul << 2) | (1ul << 0) | (1ul << 1);
    // MTLStorageMode: Shared = 0 (CPU & GPU both addressable; works on Apple Silicon and Intel iGPU).
    private const ulong MTLStorageModeShared = 0;
    private const ulong MTLStorageModePrivate = 2;

    private static readonly nint ClsMTLTextureDescriptor = ObjCRuntime.GetClass("MTLTextureDescriptor");
    private static readonly nint SelTexture2DDescriptorWithPixelFormat = ObjCRuntime.RegisterSelector("texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
    private static readonly nint SelSetUsage = ObjCRuntime.RegisterSelector("setUsage:");
    private static readonly nint SelSetStorageMode = ObjCRuntime.RegisterSelector("setStorageMode:");
    private static readonly nint SelNewTextureWithDescriptor = ObjCRuntime.RegisterSelector("newTextureWithDescriptor:");
    private static readonly nint SelGetBytes = ObjCRuntime.RegisterSelector("getBytes:bytesPerRow:fromRegion:mipmapLevel:");
    private static readonly nint SelRelease = ObjCRuntime.RegisterSelector("release");

    /// <summary>CPU-side pixel mirror, lazily allocated. Only populated when a CPU consumer
    /// (Lock / CopyPixels / GetPixelSpan / EndFrame readback) actually needs the bytes —
    /// for the Metal pipeline, the source layer / scratch surface round-trip lives entirely on
    /// GPU (MTLTexture → MPS → MTLTexture → NVG sample), so a pure-GPU consumer chain
    /// never allocates this 4 × W × H buffer (32 MB for a 4096 × 2000 RT).</summary>
    private byte[]? _pixels;
    private readonly object _gate = new();
    private int _version;
    private bool _disposed;

    private byte[]? _lockBuffer;
    private Action? _releaseAction;

    /// <summary>Latest committed but un-waited-on commandBuffer that wrote to ColorTexture.
    /// Set by RequestDeferredReadback, consumed by FlushPendingReadback when a CPU consumer
    /// (Lock / CopyPixels / GetPixelSpan) finally needs the bytes. Retained so the underlying
    /// MTLCommandBuffer survives until we waitUntilCompleted on it. Replaced (with old released)
    /// each time a new GPU pass completes — Metal command queue ordering guarantees waiting on
    /// the latest cmdBuf is sufficient (all earlier ones in the queue have already finished).</summary>
    private nint _pendingCommandBuffer;
    private bool _pendingReadback;
    private static readonly nint _selWaitUntilCompleted = ObjCRuntime.RegisterSelector("waitUntilCompleted");
    private static readonly nint _selRetain = ObjCRuntime.RegisterSelector("retain");

    /// <summary>Marks that <paramref name="commandBuffer"/> committed GPU work into ColorTexture
    /// that hasn't been mirrored to the CPU side yet. Defers the actual waitUntilCompleted +
    /// MTLTexture → bytes copy until a CPU consumer asks for the pixels — for the common
    /// pure-GPU consumer chain (NVG samples ColorTexture in a later commandBuffer on the same
    /// queue), this readback never happens. Replaces any earlier pending commandBuffer (queue
    /// ordering ensures waiting on the latest covers all prior writes).</summary>
    internal void RequestDeferredReadback(nint commandBuffer)
    {
        if (_disposed || commandBuffer == 0) return;
        if (_pendingCommandBuffer != 0)
        {
            ObjCRuntime.SendMessageNoReturn(_pendingCommandBuffer, SelRelease);
        }
        // Retain so the cmdBuf object survives autorelease pool drains until we wait on it.
        ObjCRuntime.SendMessageNoReturn(commandBuffer, _selRetain);
        _pendingCommandBuffer = commandBuffer;
        _pendingReadback = true;
        IncrementVersion();
    }

    /// <summary>If a deferred readback is pending, block on the saved commandBuffer's GPU
    /// completion and copy the MTLTexture into the CPU pixel mirror. Idempotent — clears the
    /// pending flag after a single execution. Called automatically from Lock / CopyPixels /
    /// GetPixelSpan; manual callers (e.g. cross-thread CPU mirror access) can invoke directly.</summary>
    private void FlushPendingReadbackIfNeeded()
    {
        if (!_pendingReadback || _disposed) return;
        if (_pendingCommandBuffer != 0)
        {
            ObjCRuntime.SendMessageNoReturn(_pendingCommandBuffer, _selWaitUntilCompleted);
            ObjCRuntime.SendMessageNoReturn(_pendingCommandBuffer, SelRelease);
            _pendingCommandBuffer = 0;
        }
        if (ColorTexture != 0)
        {
            CopyTextureToPixelsCore(ColorTexture);
        }
        _pendingReadback = false;
    }

    /// <summary>
    /// Color render-target MTLTexture. Lazily created on first
    /// <see cref="EnsureGpuTextures"/> call so a target that's only used as a
    /// CPU pixel buffer (e.g. WriteableBitmap upload path) doesn't allocate GPU
    /// memory it never uses.
    /// </summary>
    public nint ColorTexture { get; private set; }

    /// <summary>Depth/stencil texture matching the color size; private storage.</summary>
    public nint StencilTexture { get; private set; }

    public MewVGMetalPixelRenderSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        HasAlpha = hasAlpha;
        // _pixels left null — lazily allocated by EnsurePixelBuffer when a CPU consumer
        // (Lock / GetPixelSpan / EndFrame readback) actually needs the bytes. For pure-GPU
        // filter pipelines (MTLTexture → MPS → MTLTexture → NVG sample), this allocation
        // never happens.
    }

    private byte[] EnsurePixelBuffer()
        => _pixels ??= new byte[PixelWidth * PixelHeight * 4];

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiScale { get; }
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;
    public int StrideBytes => PixelWidth * 4;
    public int Version => Volatile.Read(ref _version);

    /// <summary>NanoVG (Metal backend) renders with premultiplied blending.</summary>
    public bool IsPremultiplied => true;

    /// <summary>MTLTexture — <see cref="Lock"/> waits any pending GPU write then
    /// [texture getBytes:] into the CPU mirror.</summary>
    public LockMode LockMode => LockMode.Readback;

    /// <summary>
    /// Mirrors the alpha-channel hint from construction. Consumers reading these pixels via
    /// <see cref="IPixelBufferSource"/> use this to skip alpha scans for opaque RTs.
    /// </summary>
    public bool HasAlpha { get; }

    RenderPixelFormat IRenderSurface.Format => RenderSurfaceDefaults.GetBgraFormat(IsPremultiplied);

    SurfaceUsage IRenderSurface.Usage => RenderSurfaceDefaults.PixelSurfaceUsage;

    SurfaceCapabilities IRenderSurface.Capabilities =>
        RenderSurfaceDefaults.GetPixelSurfaceCapabilities(
            IsPremultiplied,
            LockMode == LockMode.Readback,
            this is IGpuTextureSource);

    ulong IRenderSurface.Version => (ulong)Math.Max(0, Version);

    bool IRenderSurface.IsDisposed => _disposed;

    ReadOnlySpan<byte> ICpuPixelSurface.GetReadOnlyPixelSpan() => GetPixelSpan();

    Span<byte> ICpuPixelSurface.GetWritablePixelSpan() => GetPixelSpan();

    bool IDeferredCpuReadableSurface.HasPendingReadback => LockMode == LockMode.Readback;

    IRenderOperation IDeferredCpuReadableSurface.RequestReadback()
        => RenderSurfaceDefaults.RequestReadback(LockMode == LockMode.Readback, CopyPixels);

    bool IDeferredCpuReadableSurface.TryFlushReadback()
        => RenderSurfaceDefaults.TryFlushReadback(LockMode == LockMode.Readback, CopyPixels);

    // MTLDevice the textures were allocated on. Captured by EnsureGpuTextures so
    // IMetalTextureSource consumers can verify device match before zero-copy sampling.
    private nint _device;

    /// <summary>
    /// Allocates the color and stencil MTLTextures on the supplied device if
    /// they don't already exist. Idempotent across multiple offscreen passes
    /// targeting the same bitmap.
    /// </summary>
    internal void EnsureGpuTextures(nint device)
    {
        if (_disposed || device == 0) return;
        if (ColorTexture != 0 && StencilTexture != 0) return;

        _device = device;

        if (ColorTexture == 0)
        {
            ColorTexture = CreateTexture(device, MTLPixelFormat.BGRA8Unorm, MTLStorageModeShared);
        }
        if (StencilTexture == 0)
        {
            StencilTexture = CreateTexture(device, MTLPixelFormat.Depth32Float_Stencil8, MTLStorageModePrivate);
        }
    }

    // IMetalTextureSource — exposes the color MTLTexture for zero-copy NoDelete wrapping.
    nint IMetalTextureSource.MtlTexture => _disposed ? 0 : ColorTexture;
    nint IMetalTextureSource.MtlDevice => _disposed ? 0 : _device;

    private nint CreateTexture(nint device, MTLPixelFormat format, ulong storageMode)
    {
        if (ClsMTLTextureDescriptor == 0 || SelTexture2DDescriptorWithPixelFormat == 0) return 0;

        nint desc = ObjCRuntime.SendMessage(
            ClsMTLTextureDescriptor,
            SelTexture2DDescriptorWithPixelFormat,
            (uint)format,
            (UIntPtr)(uint)PixelWidth,
            (UIntPtr)(uint)PixelHeight,
            false);
        if (desc == 0) return 0;

        if (SelSetUsage != 0)
        {
            ObjCRuntime.SendMessageNoReturn(desc, SelSetUsage, (UInt64)MTLTextureUsageRenderTargetShaderRead);
        }
        if (SelSetStorageMode != 0)
        {
            ObjCRuntime.SendMessageNoReturn(desc, SelSetStorageMode, storageMode);
        }

        return ObjCRuntime.SendMessage(device, SelNewTextureWithDescriptor, desc);
    }

    /// <summary>
    /// Copies the GPU color texture's contents into the CPU pixel buffer.
    /// Caller must ensure GPU work writing to the texture has completed
    /// (e.g. by calling <c>commandBuffer.waitUntilCompleted</c>).
    /// </summary>
    internal void CopyTextureToPixels()
    {
        if (_disposed || ColorTexture == 0) return;
        CopyTextureToPixelsCore(ColorTexture);
    }

    /// <summary>
    /// Copies the contents of an externally-owned MTLTexture into this target's CPU pixel
    /// buffer. Used by GPU filter passes (e.g. <see cref="MetalGaussianBlur"/>) that write
    /// into a temporary shared-storage texture and need the result mirrored on the CPU side
    /// without touching this target's <see cref="ColorTexture"/> — leaving ColorTexture as 0
    /// keeps <see cref="MewVGImage"/>'s zero-copy path disengaged so the consumer's NanoVG
    /// instance uploads from <c>_pixels</c> instead of wrapping the externally-owned texture
    /// (whose lifetime is controlled by the caller).
    /// </summary>
    internal void CopyExternalTextureToPixels(nint texture)
    {
        if (_disposed || texture == 0) return;
        CopyTextureToPixelsCore(texture);
    }

    private void CopyTextureToPixelsCore(nint texture)
    {
        // MTLRegion = { origin{x,y,z}, size{w,h,d} } — 6 nint slots on 64-bit.
        Span<nint> region = stackalloc nint[6]
        {
            0, 0, 0,
            (nint)PixelWidth, (nint)PixelHeight, 1
        };

        var pixels = EnsurePixelBuffer();
        fixed (byte* p = pixels)
        fixed (nint* r = region)
        {
            GetBytesMsgSend(
                texture, SelGetBytes,
                p,
                (nuint)(uint)StrideBytes,
                r,
                mipmapLevel: 0);
        }

        IncrementVersion();
    }

    public byte[] CopyPixels()
    {
        if (_disposed) return Array.Empty<byte>();
        FlushPendingReadbackIfNeeded();
        var pixels = EnsurePixelBuffer();
        var copy = new byte[pixels.Length];
        Buffer.BlockCopy(pixels, 0, copy, 0, pixels.Length);
        return copy;
    }

    public Span<byte> GetPixelSpan()
    {
        if (_disposed) return Span<byte>.Empty;
        FlushPendingReadbackIfNeeded();
        return EnsurePixelBuffer().AsSpan();
    }

    public void Clear(Color color)
    {
        // No-op — Metal RT's pixel state lives in the GPU MTLTexture. NVG BeginFrame
        // clears it via glClear / Metal pass loadAction at the start of each render pass;
        // any CPU mirror (_pixels) gets repopulated from the GPU on EndFrame readback,
        // so clearing it pre-render would be overwritten anyway. Just bump the version
        // so consumers re-poll.
        if (_disposed) return;
        IncrementVersion();
    }

    /// <summary>Exposes the GPU MTLTexture so MewVG-Metal consumers can wrap it
    /// directly (zero-copy filter result reuse) without round-tripping through CPU.
    /// Caller must use <c>NVGimageFlags.NoDelete</c> — the texture is owned here.</summary>
    public nint GetTextureHandle() => _disposed ? 0 : ColorTexture;

    /// <inheritdoc cref="IGpuTextureSource.RetainGpuHandle"/>
    public bool RetainGpuHandle(nint handle)
    {
        if (handle == 0) return false;
        // objc_retain is null-safe and idempotent w.r.t. the Cocoa retain count; we don't
        // need to gate on _disposed here because the texture's reference count is independent
        // of this wrapper's lifecycle once handed out — that is the entire point of Retain.
        ObjCRuntime.Retain(handle);
        return true;
    }

    /// <inheritdoc cref="IGpuTextureSource.ReleaseGpuHandle"/>
    public void ReleaseGpuHandle(nint handle)
    {
        if (handle == 0) return;
        ObjCRuntime.Release(handle);
    }

    public PixelBufferLock Lock()
    {
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(MewVGMetalPixelRenderSurface));
        }

        FlushPendingReadbackIfNeeded();
        var pixels = EnsurePixelBuffer();
        int size = pixels.Length;
        if (_lockBuffer == null || _lockBuffer.Length != size)
        {
            _lockBuffer = new byte[size];
        }
        Buffer.BlockCopy(pixels, 0, _lockBuffer, 0, size);
        _releaseAction ??= () => Monitor.Exit(_gate);
        return new PixelBufferLock(
            _lockBuffer, PixelWidth, PixelHeight, StrideBytes, PixelFormat,
            _version, dirtyRegion: null, release: _releaseAction);
    }

    public void IncrementVersion() => Interlocked.Increment(ref _version);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Release any pending commandBuffer reference — we don't need its result anymore,
        // and not releasing leaks the MTLCommandBuffer until autorelease pool drain (which
        // for offscreen filter passes can be delayed indefinitely on the worker thread).
        if (_pendingCommandBuffer != 0)
        {
            ObjCRuntime.SendMessageNoReturn(_pendingCommandBuffer, SelRelease);
            _pendingCommandBuffer = 0;
        }
        _pendingReadback = false;
        if (ColorTexture != 0) { ObjCRuntime.SendMessageNoReturn(ColorTexture, SelRelease); ColorTexture = 0; }
        if (StencilTexture != 0) { ObjCRuntime.SendMessageNoReturn(StencilTexture, SelRelease); StencilTexture = 0; }
    }
}
