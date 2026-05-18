using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Wraps an <see cref="IPixelBufferSource"/> as an <see cref="IExternalRasterSource"/>
/// whose backing GL texture is updated via PBO + fence sync. The producer's CPU pixel
/// write becomes a memcpy into a driver-mapped PBO; the actual texture transfer runs
/// as background DMA. The consuming GL backend waits on the fence (in <see cref="Acquire"/>)
/// to make sure the texture is ready before sampling.
/// </summary>
/// <remarks>
/// <para>
/// This avoids the per-frame <c>glTexSubImage2D</c> stall of the default sync upload
/// path for streaming CPU sources (animated controls, video CPU fallback, etc.). Cost
/// is one extra GL texture + 2 PBO allocations per source; benefit scales with image
/// size — small UI controls (color picker wheel, etc.) probably break even, large
/// or frequently-updated sources see clear win.
/// </para>
/// <para>
/// All GL calls assume the rendering thread's context is current. Construction is
/// deferred to first <see cref="Acquire"/> — the factory creates the wrapper on any
/// thread, but the actual GL resource alloc happens at first frame use, which is on
/// the render thread.
/// </para>
/// </remarks>
internal sealed unsafe class PboFenceUploader : IExternalRasterSource
{
    private IPixelBufferSource _source;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;

    private uint _textureId;
    private uint _pbo0;
    private uint _pbo1;
    private int _currentPboIndex;
    private nint _pendingFence;
    private int _lastUploadedVersion = -1;
    private bool _initialized;
    private bool _disposed;

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public int Version => _lastUploadedVersion;
    public RenderPixelFormat Format => AlphaMode == BitmapAlphaMode.Premultiplied
        ? RenderPixelFormat.Bgra8888Premultiplied
        : RenderPixelFormat.Bgra8888;
    public BitmapAlphaMode AlphaMode => _source.IsPremultiplied
        ? BitmapAlphaMode.Premultiplied
        : BitmapAlphaMode.Straight;

    /// <summary>
    /// CPU bytes are top-down (row 0 = top). NVG expects bottom-up for FBO sources
    /// and top-down for CPU sources — same convention as the default
    /// <c>CreateImageBGRA</c> path, so no FlipY needed.
    /// </summary>
    public bool YFlipped => false;

    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable |
        SurfaceCapabilities.AsyncCompletion |
        (_source.HasAlpha ? SurfaceCapabilities.Alpha : SurfaceCapabilities.None) |
        (_source.IsPremultiplied ? SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None);

    public IReadOnlyList<ExternalRasterPlane> Planes =>
    [
        new ExternalRasterPlane(0, (nint)_textureId, _pixelWidth, _pixelHeight, 0, Format)
    ];

    /// <summary>
    /// Probe whether PBO + fence GL calls are available on the current context.
    /// Call BEFORE constructing — construction throws if not loadable. Caller
    /// should fall back to the default <c>CreateImageBGRA</c> sync upload path on failure.
    /// </summary>
    public static bool IsSupported => OpenGLPboExt.IsAvailable;

    public PboFenceUploader(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!OpenGLPboExt.IsAvailable)
        {
            throw new InvalidOperationException("PBO/fence sync GL extensions not available on this context.");
        }

        _source = source;
        _pixelWidth = source.PixelWidth;
        _pixelHeight = source.PixelHeight;
    }

    /// <summary>
    /// Swap the underlying source. Used by the factory's per-dimension pool so a single
    /// GL texture + PBO ring can be reused across many short-lived
    /// <see cref="IPixelBufferSource"/> instances (e.g. video frame objects recycled per
    /// decoded frame). Resets the version tracker so the next <see cref="Acquire"/>
    /// uploads <paramref name="newSource"/>'s current bytes.
    /// </summary>
    public void Rebind(IPixelBufferSource newSource)
    {
        ArgumentNullException.ThrowIfNull(newSource);
        if (newSource.PixelWidth != _pixelWidth || newSource.PixelHeight != _pixelHeight)
        {
            throw new ArgumentException(
                $"Source dimensions {newSource.PixelWidth}x{newSource.PixelHeight} don't match uploader {_pixelWidth}x{_pixelHeight}.",
                nameof(newSource));
        }

        _source = newSource;
        _lastUploadedVersion = -1;   // force upload on next Acquire
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        uint texId;
        OpenGLPboExt.GenTextures(1, &texId);
        OpenGLPboExt.BindTexture(OpenGLPboExt.GL_TEXTURE_2D, texId);
        OpenGLPboExt.TexParameteri(OpenGLPboExt.GL_TEXTURE_2D, OpenGLPboExt.GL_TEXTURE_MIN_FILTER, (int)OpenGLPboExt.GL_LINEAR);
        OpenGLPboExt.TexParameteri(OpenGLPboExt.GL_TEXTURE_2D, OpenGLPboExt.GL_TEXTURE_MAG_FILTER, (int)OpenGLPboExt.GL_LINEAR);
        OpenGLPboExt.TexParameteri(OpenGLPboExt.GL_TEXTURE_2D, OpenGLPboExt.GL_TEXTURE_WRAP_S, (int)OpenGLPboExt.GL_CLAMP_TO_EDGE);
        OpenGLPboExt.TexParameteri(OpenGLPboExt.GL_TEXTURE_2D, OpenGLPboExt.GL_TEXTURE_WRAP_T, (int)OpenGLPboExt.GL_CLAMP_TO_EDGE);

        // Allocate empty texture storage; first Acquire fills it.
        OpenGLPboExt.TexImage2D(OpenGLPboExt.GL_TEXTURE_2D, 0,
            (int)OpenGLPboExt.GL_RGBA8, _pixelWidth, _pixelHeight, 0,
            OpenGLPboExt.GL_BGRA, OpenGLPboExt.GL_UNSIGNED_INT_8_8_8_8_REV, null);

        // Double-buffered PBO ring. Frame N writes to PBO[N%2] while frame N-1's
        // upload is still in flight on PBO[(N-1)%2] — overlaps producer with DMA.
        uint pbo0, pbo1;
        OpenGLPboExt.GenBuffers(1, &pbo0);
        OpenGLPboExt.GenBuffers(1, &pbo1);

        nint byteSize = checked((nint)((long)_pixelWidth * _pixelHeight * 4));

        OpenGLPboExt.BindBuffer(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, pbo0);
        OpenGLPboExt.BufferData(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, byteSize, null, OpenGLPboExt.GL_STREAM_DRAW);

        OpenGLPboExt.BindBuffer(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, pbo1);
        OpenGLPboExt.BufferData(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, byteSize, null, OpenGLPboExt.GL_STREAM_DRAW);

        OpenGLPboExt.BindBuffer(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, 0);
        OpenGLPboExt.BindTexture(OpenGLPboExt.GL_TEXTURE_2D, 0);

        _textureId = texId;
        _pbo0 = pbo0;
        _pbo1 = pbo1;
        _initialized = true;
    }

    public IExternalRasterLease Acquire()
    {
        AcquireTexture();
        return new GLLease(this);
    }

    private void AcquireTexture()
    {
        if (_disposed) return;
        EnsureInitialized();

        int currentVersion = _source.Version;

        // Push a new upload only when the source bumped its version since last frame.
        // Static sources never re-upload after the first time, so the steady-state
        // cost matches a regular sampled texture.
        if (currentVersion != _lastUploadedVersion)
        {
            UploadCurrentVersion(currentVersion);
        }

        // Even when no new upload was pushed this frame, an in-flight fence from a
        // previous upload may still be pending — block until it signals so the
        // sample reads finished pixels.
        if (_pendingFence != 0)
        {
            // 0 timeout = poll. If not yet signaled, wait up to 1ms — driver-side
            // upload of a frame-sized texture should land well under that budget.
            uint status = OpenGLPboExt.ClientWaitSync(_pendingFence, OpenGLPboExt.GL_SYNC_FLUSH_COMMANDS_BIT, 0);
            if (status != OpenGLPboExt.GL_ALREADY_SIGNALED && status != OpenGLPboExt.GL_CONDITION_SATISFIED)
            {
                OpenGLPboExt.ClientWaitSync(_pendingFence, OpenGLPboExt.GL_SYNC_FLUSH_COMMANDS_BIT, 1_000_000UL);
            }

            OpenGLPboExt.DeleteSync(_pendingFence);
            _pendingFence = 0;
        }
    }

    private void UploadCurrentVersion(int currentVersion)
    {
        using var pixelLock = _source.Lock();
        var bytes = pixelLock.Buffer;
        if (bytes is null || bytes.Length == 0) return;

        // Rotate to the next PBO so the previous one (still being DMA'd) isn't
        // overwritten — driver would otherwise stall waiting for the prior
        // BufferData to finish.
        _currentPboIndex = (_currentPboIndex + 1) & 1;
        uint pbo = _currentPboIndex == 0 ? _pbo0 : _pbo1;

        OpenGLPboExt.BindBuffer(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, pbo);

        nint byteSize = bytes.Length;

        // Orphan + refill: BufferData(NULL) discards the old contents (driver
        // can recycle the storage immediately) — then BufferSubData copies the
        // fresh frame in. Equivalent to glBufferData(size, ptr, STREAM_DRAW)
        // but slightly more explicit about intent.
        OpenGLPboExt.BufferData(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, byteSize, null, OpenGLPboExt.GL_STREAM_DRAW);
        fixed (byte* dataPtr = bytes)
        {
            OpenGLPboExt.BufferSubData(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, 0, byteSize, dataPtr);
        }

        // Trigger DMA from PBO into the texture. NULL pixel pointer means
        // "read from currently-bound PIXEL_UNPACK_BUFFER at offset 0".
        OpenGLPboExt.BindTexture(OpenGLPboExt.GL_TEXTURE_2D, _textureId);
        OpenGLPboExt.PixelStorei(OpenGLPboExt.GL_UNPACK_ROW_LENGTH, 0);
        OpenGLPboExt.TexImage2D(OpenGLPboExt.GL_TEXTURE_2D, 0,
            (int)OpenGLPboExt.GL_RGBA8, _pixelWidth, _pixelHeight, 0,
            OpenGLPboExt.GL_BGRA, OpenGLPboExt.GL_UNSIGNED_INT_8_8_8_8_REV, null);

        OpenGLPboExt.BindTexture(OpenGLPboExt.GL_TEXTURE_2D, 0);
        OpenGLPboExt.BindBuffer(OpenGLPboExt.GL_PIXEL_UNPACK_BUFFER, 0);

        // Replace any previous pending fence — it's stale once a newer upload is
        // queued. (Double-buffering covers in-flight overlap.)
        if (_pendingFence != 0)
        {
            OpenGLPboExt.DeleteSync(_pendingFence);
        }
        _pendingFence = OpenGLPboExt.FenceSync();
        _lastUploadedVersion = currentVersion;
    }

    private void ReleaseTexture()
    {
        // Nothing per-frame to release; the texture stays valid across frames
        // until Dispose. Acquire's fence wait already handled sync.
    }

    private sealed class GLLease : IExternalRasterLease
    {
        private PboFenceUploader? _owner;

        public GLLease(PboFenceUploader owner)
        {
            _owner = owner;
        }

        public nint NativeHandle => (nint)(_owner?._textureId ?? 0);
        public nint NativeAlternateHandle => 0;
        public int PixelWidth => _owner?._pixelWidth ?? 0;
        public int PixelHeight => _owner?._pixelHeight ?? 0;
        public bool YFlipped => _owner?.YFlipped ?? false;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseTexture();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pendingFence != 0)
        {
            OpenGLPboExt.DeleteSync(_pendingFence);
            _pendingFence = 0;
        }

        if (_pbo0 != 0)
        {
            uint id = _pbo0;
            OpenGLPboExt.DeleteBuffers(1, &id);
            _pbo0 = 0;
        }
        if (_pbo1 != 0)
        {
            uint id = _pbo1;
            OpenGLPboExt.DeleteBuffers(1, &id);
            _pbo1 = 0;
        }
        if (_textureId != 0)
        {
            uint id = _textureId;
            OpenGLPboExt.DeleteTextures(1, &id);
            _textureId = 0;
        }
    }
}
