using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Owns the long-lived <c>CVMetalTextureCacheRef</c> used to wrap VideoToolbox decoder
/// outputs (CVPixelBuffers) as MTLTextures without a CPU copy. One bridge per decoder.
/// </summary>
/// <remarks>
/// The cache binds CVPixelBuffer's IOSurface to a Metal device. Recommended pattern:
/// keep a single cache for the lifetime of the decode session and call
/// <see cref="Flush"/> periodically (every N frames) to drop stale entries.
/// </remarks>
internal sealed class VideoToolboxMetalBridge : IDisposable
{
    private nint _textureCache;
    private nint _pixelTransferSession;
    private nint _ioSurfacePropertiesKey;       // CFString "IOSurfaceProperties"
    private nint _metalCompatibilityKey;        // CFString "MetalCompatibility"
    private nint _cfTrueNumber;                 // kCFBooleanTrue singleton
    private nint _bgraDestAttrs;                // long-lived attrs dict reused per CVPixelBufferCreate call
    private readonly nint _metalDevice;
    private bool _disposed;
    private bool _transferSessionLogged;

    // BGRA destination-buffer pool — concurrent because Rent runs on the decoder thread
    // (FFmpeg/VT) and Return runs on the render thread (FrameTexture.Dispose during
    // PresentFrame). Plain Queue<T> races and produced intermittent stutter.
    private readonly System.Collections.Concurrent.ConcurrentQueue<nint> _bgraPool = new();
    private int _pooledWidth;
    private int _pooledHeight;
    // Pool capacity tuned to cover concurrent frame ownership: decode-in-flight,
    // queued-for-render, currently-presenting, plus one slack frame for IImage dispose
    // running behind. 3 was too tight on 4K 60 → render 120 fps mismatch and caused
    // periodic fallback to fresh CreateIoSurfaceBackedBgraBuffer allocations (~33 MB each).
    private const int BgraPoolCapacity = 6;

    public nint MetalDevice => _metalDevice;

    public VideoToolboxMetalBridge(nint metalDevice)
    {
        if (metalDevice == 0)
        {
            throw new ArgumentException("metalDevice must be non-null.", nameof(metalDevice));
        }

        _metalDevice = metalDevice;

        // Lazy one-time load of CFBoolean / CFType dict-callback globals — required for
        // building the pixel-buffer attribute dicts handed to CVPixelBufferCreate.
        CoreVideoInterop.EnsureGlobalsLoaded();

        int result = CoreVideoInterop.CVMetalTextureCacheCreate(
            allocator: 0,
            cacheAttributes: 0,
            metalDevice: metalDevice,
            textureAttributes: 0,
            out _textureCache);

        if (result != 0 || _textureCache == 0)
        {
            throw new InvalidOperationException($"CVMetalTextureCacheCreate failed (status {result}).");
        }

        // Cache the CFString keys + kCFBooleanTrue once. IOSurfaceProperties (empty dict)
        // requests IOSurface backing; MetalCompatibility=true is checked strictly via
        // CFEqual against kCFBooleanTrue (a CFNumber(1) fails silently → buffer is non-
        // compatible → CVMetalTextureCacheCreateTextureFromImage returns -6660).
        _ioSurfacePropertiesKey = CoreVideoInterop.CFStringCreateWithCString(
            0, "IOSurfaceProperties", CoreVideoInterop.kCFStringEncodingUTF8);
        _metalCompatibilityKey = CoreVideoInterop.CFStringCreateWithCString(
            0, "MetalCompatibility", CoreVideoInterop.kCFStringEncodingUTF8);

        // kCFBooleanTrue is a process-wide singleton owned by CoreFoundation — no retain
        // needed and we must NOT release it.
        _cfTrueNumber = CoreVideoInterop.CFBooleanTrue;

        // Build the BGRA destination-buffer attribute dictionary once. CVPixelBufferCreate
        // doesn't retain a reference to the dict beyond the call, but rebuilding it per
        // frame allocates 2 CFDictionaries + holds short-lived CFRefs every wrap — at
        // 60 fps that's a measurable CPU/GC cost on the decode thread. Reusing the same
        // dict drops it to zero allocations per frame.
        _bgraDestAttrs = BuildBgraDestAttributes();

        SampleLog.Write($"VideoToolboxMetalBridge: cache created on device 0x{metalDevice:X}.");
    }

    private nint BuildBgraDestAttributes()
    {
        nint emptyIoSurfaceProps = CoreVideoInterop.CFDictionaryCreateMutable(
            allocator: 0,
            capacity: 0,
            keyCallBacks: CoreVideoInterop.CFTypeDictionaryKeyCallBacks,
            valueCallBacks: CoreVideoInterop.CFTypeDictionaryValueCallBacks);

        nint attrs = CoreVideoInterop.CFDictionaryCreateMutable(
            allocator: 0,
            capacity: 2,
            keyCallBacks: CoreVideoInterop.CFTypeDictionaryKeyCallBacks,
            valueCallBacks: CoreVideoInterop.CFTypeDictionaryValueCallBacks);

        if (attrs != 0)
        {
            CoreVideoInterop.CFDictionarySetValue(attrs, _ioSurfacePropertiesKey, emptyIoSurfaceProps);
            CoreVideoInterop.CFDictionarySetValue(attrs, _metalCompatibilityKey, _cfTrueNumber);
        }

        // attrs (mutable, type-aware) retained the inner dict via CFTypeDictionaryValueCallBacks;
        // we can drop our local ref now without dangling.
        if (emptyIoSurfaceProps != 0) CoreVideoInterop.CFRelease(emptyIoSurfaceProps);
        return attrs;
    }

    /// <summary>
    /// Wrap a CVPixelBuffer as a Metal-sampleable BGRA texture. Handles two cases:
    /// <list type="bullet">
    ///   <item>Source is BGRA → wrap directly via CVMetalTextureCache (true zero-copy).</item>
    ///   <item>Source is NV12 (420v / 420f) → allocate a BGRA destination CVPixelBuffer
    ///         and run a GPU-side <c>VTPixelTransferSessionTransferImage</c> to convert.
    ///         Result is wrapped via the same cache path (still GPU-only — no CPU
    ///         readback in either branch).</item>
    /// </list>
    /// Returns null on any failure; caller falls back to CPU readback.
    /// </summary>
    public VideoToolboxFrameTexture? TryWrap(nint cvPixelBuffer)
    {
        if (_disposed || _textureCache == 0 || cvPixelBuffer == 0)
        {
            return null;
        }

        uint sourceFormat = CoreVideoInterop.CVPixelBufferGetPixelFormatType(cvPixelBuffer);

        if (sourceFormat == CoreVideoInterop.kCVPixelFormatType_32BGRA)
        {
            return WrapBgraDirect(cvPixelBuffer);
        }

        if (sourceFormat == CoreVideoInterop.kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
            || sourceFormat == CoreVideoInterop.kCVPixelFormatType_420YpCbCr8BiPlanarFullRange)
        {
            return WrapNv12ViaTransfer(cvPixelBuffer);
        }

        SampleLog.Write($"VideoToolboxMetalBridge: unsupported source format 0x{sourceFormat:X8}; only BGRA and NV12 are wrappable.");
        return null;
    }

    private VideoToolboxFrameTexture? WrapBgraDirect(nint cvPixelBuffer)
    {
        nuint width = CoreVideoInterop.CVPixelBufferGetWidth(cvPixelBuffer);
        nuint height = CoreVideoInterop.CVPixelBufferGetHeight(cvPixelBuffer);

        int result = CoreVideoInterop.CVMetalTextureCacheCreateTextureFromImage(
            allocator: 0,
            textureCache: _textureCache,
            sourceImage: cvPixelBuffer,
            textureAttributes: 0,
            pixelFormat: CoreVideoInterop.MTLPixelFormat.BGRA8Unorm,
            width: width,
            height: height,
            planeIndex: 0,
            textureOut: out var cvMetalTexture);

        if (result != 0 || cvMetalTexture == 0)
        {
            SampleLog.Write($"CVMetalTextureCacheCreateTextureFromImage(BGRA) failed (status {result}).");
            return null;
        }

        nint mtlTexture = CoreVideoInterop.CVMetalTextureGetTexture(cvMetalTexture);
        if (mtlTexture == 0)
        {
            CoreVideoInterop.CFRelease(cvMetalTexture);
            return null;
        }

        nint retainedPixelBuffer = CoreVideoInterop.CVPixelBufferRetain(cvPixelBuffer);

        return new VideoToolboxFrameTexture(
            cvMetalTexture: cvMetalTexture,
            cvPixelBuffer: retainedPixelBuffer,
            mtlTexture: mtlTexture,
            mtlDevice: _metalDevice,
            (int)width,
            (int)height);
    }

    /// <summary>
    /// NV12 → BGRA on GPU via VTPixelTransferSession. Allocates a fresh BGRA
    /// IOSurface-backed CVPixelBuffer per frame as the destination, then wraps that as
    /// the displayable MTLTexture. The transfer session handles colour-space conversion
    /// (BT.601/709 limited range → full-range RGB).
    /// </summary>
    private VideoToolboxFrameTexture? WrapNv12ViaTransfer(nint cvPixelBuffer)
    {
        if (!EnsureTransferSession())
        {
            return null;
        }

        nuint width = CoreVideoInterop.CVPixelBufferGetWidth(cvPixelBuffer);
        nuint height = CoreVideoInterop.CVPixelBufferGetHeight(cvPixelBuffer);

        nint destBuffer = RentBgraDestBuffer(width, height);
        if (destBuffer == 0)
        {
            return null;
        }

        try
        {
            int xferStatus = CoreVideoInterop.VTPixelTransferSessionTransferImage(
                _pixelTransferSession, cvPixelBuffer, destBuffer);
            if (xferStatus != 0)
            {
                SampleLog.Write($"VTPixelTransferSessionTransferImage failed (status {xferStatus}).");
                ReturnBgraDestBuffer(destBuffer);
                return null;
            }

            int wrapStatus = CoreVideoInterop.CVMetalTextureCacheCreateTextureFromImage(
                allocator: 0,
                textureCache: _textureCache,
                sourceImage: destBuffer,
                textureAttributes: 0,
                pixelFormat: CoreVideoInterop.MTLPixelFormat.BGRA8Unorm,
                width: width,
                height: height,
                planeIndex: 0,
                textureOut: out var cvMetalTexture);

            if (wrapStatus != 0 || cvMetalTexture == 0)
            {
                SampleLog.Write($"CVMetalTextureCacheCreateTextureFromImage(post-transfer BGRA) failed (status {wrapStatus}).");
                ReturnBgraDestBuffer(destBuffer);
                return null;
            }

            nint mtlTexture = CoreVideoInterop.CVMetalTextureGetTexture(cvMetalTexture);
            if (mtlTexture == 0)
            {
                CoreVideoInterop.CFRelease(cvMetalTexture);
                ReturnBgraDestBuffer(destBuffer);
                return null;
            }

            // Pool-returned destBuffer: the FrameTexture borrows it for the wrap lifetime.
            // Its Dispose calls back into ReturnBgraDestBuffer instead of releasing — keeps
            // the IOSurface alive for the next frame's transfer.
            return new VideoToolboxFrameTexture(
                cvMetalTexture: cvMetalTexture,
                cvPixelBuffer: destBuffer,
                mtlTexture: mtlTexture,
                mtlDevice: _metalDevice,
                (int)width,
                (int)height,
                pixelBufferReleaseCallback: ReturnBgraDestBuffer);
        }
        catch
        {
            ReturnBgraDestBuffer(destBuffer);
            throw;
        }
    }

    private nint RentBgraDestBuffer(nuint width, nuint height)
    {
        int w = (int)width;
        int h = (int)height;
        if (_pooledWidth != w || _pooledHeight != h)
        {
            DrainBgraPool();
            _pooledWidth = w;
            _pooledHeight = h;
            // Prewarm: pay the IOSurface alloc cost up front (one batch on the first frame
            // of a stream) instead of letting the first ~6 frames fall through to
            // CreateIoSurfaceBackedBgraBuffer one-by-one and stutter the playback start.
            for (int i = 0; i < BgraPoolCapacity; i++)
            {
                nint pre = CreateIoSurfaceBackedBgraBuffer(width, height);
                if (pre == 0) break;
                _bgraPool.Enqueue(pre);
            }
        }
        if (_bgraPool.TryDequeue(out var buffer))
        {
            return buffer;
        }
        return CreateIoSurfaceBackedBgraBuffer(width, height);
    }

    private void ReturnBgraDestBuffer(nint buffer)
    {
        if (buffer == 0) return;
        if (_disposed || _bgraPool.Count >= BgraPoolCapacity)
        {
            CoreVideoInterop.CVPixelBufferRelease(buffer);
            return;
        }
        _bgraPool.Enqueue(buffer);
    }

    private void DrainBgraPool()
    {
        while (_bgraPool.TryDequeue(out var buffer))
        {
            CoreVideoInterop.CVPixelBufferRelease(buffer);
        }
    }

    private bool EnsureTransferSession()
    {
        if (_pixelTransferSession != 0) return true;

        int status = CoreVideoInterop.VTPixelTransferSessionCreate(0, out _pixelTransferSession);
        if (status != 0 || _pixelTransferSession == 0)
        {
            SampleLog.Write($"VTPixelTransferSessionCreate failed (status {status}). NV12 → BGRA conversion unavailable.");
            return false;
        }

        if (!_transferSessionLogged)
        {
            _transferSessionLogged = true;
            SampleLog.Write("VideoToolboxMetalBridge: VTPixelTransferSession created (NV12 → BGRA on GPU).");
        }
        return true;
    }

    /// <summary>
    /// Build a fresh IOSurface-backed BGRA CVPixelBuffer of the given dimensions. The
    /// IOSurface backing is required for Metal sampling — heap-only buffers cannot be
    /// wrapped via CVMetalTextureCache.
    /// </summary>
    private nint CreateIoSurfaceBackedBgraBuffer(nuint width, nuint height)
    {
        if (_bgraDestAttrs == 0)
        {
            return 0;
        }

        int status = CoreVideoInterop.CVPixelBufferCreate(
            allocator: 0,
            width: width,
            height: height,
            pixelFormatType: CoreVideoInterop.kCVPixelFormatType_32BGRA,
            pixelBufferAttributes: _bgraDestAttrs,
            pixelBufferOut: out var dest);

        if (status != 0 || dest == 0)
        {
            SampleLog.Write($"CVPixelBufferCreate(BGRA, IOSurface, Metal-compatible) failed (status {status}).");
            return 0;
        }

        return dest;
    }

    public void Flush()
    {
        if (_disposed || _textureCache == 0) return;
        CoreVideoInterop.CVMetalTextureCacheFlush(_textureCache, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DrainBgraPool();

        if (_pixelTransferSession != 0)
        {
            CoreVideoInterop.CFRelease(_pixelTransferSession);
            _pixelTransferSession = 0;
        }

        if (_ioSurfacePropertiesKey != 0)
        {
            CoreVideoInterop.CFRelease(_ioSurfacePropertiesKey);
            _ioSurfacePropertiesKey = 0;
        }

        if (_metalCompatibilityKey != 0)
        {
            CoreVideoInterop.CFRelease(_metalCompatibilityKey);
            _metalCompatibilityKey = 0;
        }

        if (_bgraDestAttrs != 0)
        {
            CoreVideoInterop.CFRelease(_bgraDestAttrs);
            _bgraDestAttrs = 0;
        }

        // _cfTrueNumber is the kCFBooleanTrue singleton — owned by CoreFoundation, never released.
        _cfTrueNumber = 0;

        if (_textureCache != 0)
        {
            CoreVideoInterop.CFRelease(_textureCache);
            _textureCache = 0;
        }
    }
}

/// <summary>
/// One frame's worth of CoreVideo→Metal wrapper state. Implements
/// <see cref="IExternalRasterSource"/> so the render device's external raster path
/// can wrap the underlying MTLTexture as an
/// <c>IImage</c> with NoDelete semantics — zero-copy display from VideoToolbox decode
/// to NanoVG sampling.
/// </summary>
/// <remarks>
/// Encapsulates three CoreFoundation/CoreVideo refcounted resources:
/// <list type="bullet">
///   <item><c>CVMetalTextureRef</c> — keeps the IOSurface mapped into the Metal device's
///         resource table. Released last on Dispose.</item>
///   <item><c>CVPixelBufferRef</c> — explicit retain so the IOSurface page stays
///         resident even if the AVFrame slot is recycled before the GPU draws.</item>
///   <item><c>id&lt;MTLTexture&gt;</c> — borrowed pointer owned by the CVMetalTextureRef.
///         No separate retain.</item>
/// </list>
/// Acquire/Release are no-ops: the underlying texture is always GPU-resident from
/// construction onward (no fence to wait, no software lock to take). The lifetime is
/// pinned by <c>_cvMetalTexture</c> and the explicit CVPixelBuffer retain.
/// </remarks>
public sealed class VideoToolboxFrameTexture : IExternalRasterSource, IGpuResourceAffinityProvider
{
    private nint _cvMetalTexture;
    private nint _cvPixelBuffer;
    private nint _mtlTexture;
    private nint _mtlDevice;
    private bool _disposed;

    // When set, Dispose hands _cvPixelBuffer back to the owner's pool instead of releasing
    // it. Used by the NV12 → BGRA transfer path so destination IOSurface buffers are
    // recycled across frames.
    private readonly Action<nint>? _pixelBufferReleaseCallback;

    public nint MtlTexture => _mtlTexture;
    public nint MtlDevice => _mtlDevice;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int Version => 0;
    public RenderPixelFormat Format => RenderPixelFormat.Bgra8888;
    public BitmapAlphaMode AlphaMode => BitmapAlphaMode.Ignore;
    public bool YFlipped => false;
    public GpuResourceAffinity? Affinity =>
        _mtlDevice == 0
            ? null
            : new GpuResourceAffinity(Display: null, new GpuDeviceIdentity((ulong)_mtlDevice, 0, _mtlDevice));
    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable;
    public IReadOnlyList<ExternalRasterPlane> Planes =>
    [
        new ExternalRasterPlane(0, _disposed ? 0 : _mtlTexture, PixelWidth, PixelHeight, 0, Format)
    ];

    internal VideoToolboxFrameTexture(nint cvMetalTexture, nint cvPixelBuffer, nint mtlTexture, nint mtlDevice, int width, int height,
        Action<nint>? pixelBufferReleaseCallback = null)
    {
        _cvMetalTexture = cvMetalTexture;
        _cvPixelBuffer = cvPixelBuffer;
        _mtlTexture = mtlTexture;
        _mtlDevice = mtlDevice;
        PixelWidth = width;
        PixelHeight = height;
        _pixelBufferReleaseCallback = pixelBufferReleaseCallback;
    }

    public IExternalRasterLease Acquire()
        => new MetalLease(this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_cvMetalTexture != 0)
        {
            CoreVideoInterop.CFRelease(_cvMetalTexture);
            _cvMetalTexture = 0;
        }

        if (_cvPixelBuffer != 0)
        {
            if (_pixelBufferReleaseCallback is not null)
            {
                _pixelBufferReleaseCallback(_cvPixelBuffer);
            }
            else
            {
                CoreVideoInterop.CVPixelBufferRelease(_cvPixelBuffer);
            }
            _cvPixelBuffer = 0;
        }

        _mtlTexture = 0;
        _mtlDevice = 0;
    }

    private sealed class MetalLease : IExternalRasterLease, IGpuResourceAffinityProvider
    {
        private readonly VideoToolboxFrameTexture _source;

        public MetalLease(VideoToolboxFrameTexture source)
        {
            _source = source;
        }

        public nint NativeHandle => _source._disposed ? 0 : _source._mtlTexture;
        public nint NativeAlternateHandle => 0;
        public int PixelWidth => _source.PixelWidth;
        public int PixelHeight => _source.PixelHeight;
        public bool YFlipped => _source.YFlipped;
        public GpuResourceAffinity? Affinity => _source.Affinity;
        public void Dispose() { }
    }
}
