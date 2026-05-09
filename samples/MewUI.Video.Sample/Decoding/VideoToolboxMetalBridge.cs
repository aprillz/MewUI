using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;
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

        nint destBuffer = CreateIoSurfaceBackedBgraBuffer(width, height);
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
                CoreVideoInterop.CVPixelBufferRelease(destBuffer);
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
                CoreVideoInterop.CVPixelBufferRelease(destBuffer);
                return null;
            }

            nint mtlTexture = CoreVideoInterop.CVMetalTextureGetTexture(cvMetalTexture);
            if (mtlTexture == 0)
            {
                CoreVideoInterop.CFRelease(cvMetalTexture);
                CoreVideoInterop.CVPixelBufferRelease(destBuffer);
                return null;
            }

            // destBuffer is owned by this wrapper now (we did the create, we hold the
            // sole retain); no extra retain needed.
            return new VideoToolboxFrameTexture(
                cvMetalTexture: cvMetalTexture,
                cvPixelBuffer: destBuffer,
                mtlTexture: mtlTexture,
                mtlDevice: _metalDevice,
                (int)width,
                (int)height);
        }
        catch
        {
            CoreVideoInterop.CVPixelBufferRelease(destBuffer);
            throw;
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
/// <see cref="IExternalLockedTexture"/> so the render device's external sample path
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
public sealed class VideoToolboxFrameTexture : IExternalLockedTexture
{
    private nint _cvMetalTexture;
    private nint _cvPixelBuffer;
    private nint _mtlTexture;
    private nint _mtlDevice;
    private bool _disposed;

    public nint MtlTexture => _mtlTexture;
    public nint MtlDevice => _mtlDevice;

    public nint NativeHandle => _disposed ? 0 : _mtlTexture;
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public BitmapAlphaMode AlphaMode => BitmapAlphaMode.Ignore;
    public bool YFlipped => false;

    internal VideoToolboxFrameTexture(nint cvMetalTexture, nint cvPixelBuffer, nint mtlTexture, nint mtlDevice, int width, int height)
    {
        _cvMetalTexture = cvMetalTexture;
        _cvPixelBuffer = cvPixelBuffer;
        _mtlTexture = mtlTexture;
        _mtlDevice = mtlDevice;
        PixelWidth = width;
        PixelHeight = height;
    }

    public void Acquire() { /* no-op — IOSurface is always GPU-resident while CVMetalTexture lives */ }

    public void Release() { /* no-op */ }

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
            CoreVideoInterop.CVPixelBufferRelease(_cvPixelBuffer);
            _cvPixelBuffer = 0;
        }

        _mtlTexture = 0;
        _mtlDevice = 0;
    }
}
