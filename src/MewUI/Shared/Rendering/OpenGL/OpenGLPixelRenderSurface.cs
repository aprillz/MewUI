using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// OpenGL pixel render surface using FBO (Framebuffer Object).
/// Provides offscreen rendering with CPU-side pixel buffer access.
/// </summary>
internal sealed class OpenGLPixelRenderSurface : IPixelBufferSource, ICpuPixelSurface, IDeferredCpuReadableSurface, IDisposable, IGLTextureSource, IExternalWritableGpuSurface, IGpuResourceAffinityProvider
{
    // Lazily allocated — only when a CPU consumer (Lock / CopyPixels / GetPixelSpan)
    // actually requests pixel bytes. The pure GPU-only path (MewVGImage zero-copy via
    // CreateImageFromHandle) never touches this. At 100 source layers × ~5 MB each per
    // frame, eager allocation here was ~500 MB of GC churn for memory that nothing read.
    private byte[]? _pixels;

    private readonly object _gate = new();
    private readonly Action<OpenGLPixelRenderSurface>? _glDisposeRequested;
    private readonly Func<nint>? _currentContextProvider;
    private int _version;
    private bool _disposed;

    // FBO resources (created lazily when GL context is available)
    private uint _fbo;

    private uint _texture;
    private uint _stencilRenderbuffer;
    private bool _fboInitialized;
    private bool _hasStencil;

    // HGLRC / GLXContext that created the FBO + texture + RB. Required by the
    // background-rebuild path because FBOs and renderbuffers are NOT shared across
    // contexts via wglShareLists / glXCreateContext (only textures, buffers, shaders
    // are). When the offscreen provider drains pending disposals it must release
    // these GL handles under the same context that created them; doing it under a
    // sibling context (e.g. worker FBO drained by UI's window context) makes the
    // glDelete* call a silent no-op, leaking the FBO. The provider's drain filters
    // by this field.
    private nint _creationContext;

    // Set by GPU writers (blur shader, NVG render) to signal that the FBO contents are
    // newer than the CPU-side _pixels mirror. Cleared by the next readback. Used to defer
    // glReadPixels until something actually requests the CPU bytes — folding 100 sync
    // points (one per filter) into 0 or 1 across a render pass with N filtered elements.
    private bool _fboNewerThanCpu;

    private byte[]? _lockBuffer;
    private byte[]? _uploadBuffer;
    private Action? _releaseAction;

    // External retain count for the FBO color texture, used by zero-copy scratch-surface paths
    // (via IGpuTextureSource.RetainGpuHandle). MewVGImage takes a retain when it wraps our
    // texture zero-copy with NVG's NoDelete flag, so the texture stays alive through the
    // consumer's NVG flush even if Dispose runs first. ReleaseGpuHandle decrements; when it
    // reaches 0 and Dispose was already called, the GL resource release fires.
    private int _externalRetainCount;

    private bool _disposeDeferredForRetain;

    public OpenGLPixelRenderSurface(int pixelWidth, int pixelHeight, double dpiScale,
        Action<OpenGLPixelRenderSurface>? glDisposeRequested = null,
        Func<nint>? currentContextProvider = null,
        bool hasAlpha = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        HasAlpha = hasAlpha;
        _glDisposeRequested = glDisposeRequested;
        _currentContextProvider = currentContextProvider;
        // _pixels left null — see EnsurePixelBuffer.
    }

    private byte[] EnsurePixelBuffer()
    {
        return _pixels ??= new byte[PixelWidth * PixelHeight * 4];
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public double DpiScale { get; }

    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;

    public int StrideBytes => PixelWidth * 4;

    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Gets the FBO ID. Returns 0 if not initialized or disposed.
    /// </summary>
    internal uint Fbo => _fbo;

    /// <summary>
    /// Gets the texture ID attached to the FBO. Returns 0 if not initialized or disposed.
    /// </summary>
    internal uint Texture => _texture;

    /// <summary>
    /// Gets whether FBO resources have been initialized.
    /// </summary>
    internal bool IsFboInitialized => _fboInitialized;

    internal bool HasStencil => _hasStencil;

    /// <summary>HGLRC / GLXContext that owns the FBO + RB handles. The offscreen
    /// provider's deferred-disposal drain uses this to skip targets whose owning
    /// context is not currently active (FBOs/RBs are NOT shared across contexts
    /// even with wglShareLists). 0 if FBO not yet initialized.</summary>
    internal nint CreationContext => _creationContext;

    /// <summary>Records the GL context handle that was current when
    /// <see cref="InitializeFbo"/> ran. Called by the platform's PreparePixelSurface
    /// (Win32: wglGetCurrentContext; X11: glXGetCurrentContext) after
    /// <see cref="InitializeFbo"/> succeeds.</summary>
    internal void RecordCreationContext(nint context) => _creationContext = context;

    public GpuResourceAffinity? Affinity => _creationContext == 0
        ? null
        : new GpuResourceAffinity(Display: null, new GpuDeviceIdentity((ulong)_creationContext, 0, _creationContext));

    /// <summary>NanoVG renders with SRC_ALPHA / ONE_MINUS_SRC_ALPHA blending into
    /// the FBO color attachment, producing premultiplied output.</summary>
    public bool IsPremultiplied => true;

    /// <summary>FBO color attachment — <see cref="Lock"/> issues glReadPixels (sync barrier)
    /// to populate the CPU mirror.</summary>
    public LockMode LockMode => LockMode.Readback;

    /// <summary>
    /// Mirrors the alpha-channel hint from construction. Consumers reading these pixels via
    /// <see cref="IPixelBufferSource"/> use this to skip alpha scans for opaque RTs (video
    /// frame target etc.).
    /// </summary>
    public bool HasAlpha { get; }

    RenderPixelFormat IRenderSurface.Format => RenderSurfaceDefaults.GetBgraFormat(IsPremultiplied);

    SurfaceUsage IRenderSurface.Usage => RenderSurfaceDefaults.PixelSurfaceUsage;

    SurfaceCapabilities IRenderSurface.Capabilities =>
        RenderSurfaceDefaults.GetPixelSurfaceCapabilities(
            IsPremultiplied,
            LockMode == LockMode.Readback,
            this is IGpuTextureSource) |
        SurfaceCapabilities.ExternalGpuWritable;

    ulong IRenderSurface.Version => (ulong)Math.Max(0, Version);

    bool IRenderSurface.IsDisposed => _disposed;

    ReadOnlySpan<byte> ICpuPixelSurface.GetReadOnlyPixelSpan() => GetPixelSpan();

    Span<byte> ICpuPixelSurface.GetWritablePixelSpan() => GetPixelSpan();

    bool IDeferredCpuReadableSurface.HasPendingReadback => LockMode == LockMode.Readback;

    IRenderOperation IDeferredCpuReadableSurface.RequestReadback()
        => RenderSurfaceDefaults.RequestReadback(LockMode == LockMode.Readback, CopyPixels);

    bool IDeferredCpuReadableSurface.TryFlushReadback()
        => RenderSurfaceDefaults.TryFlushReadback(LockMode == LockMode.Readback, CopyPixels);

    // IGLTextureSource — exposes the FBO color texture for zero-copy NoDelete wrapping.
    uint IGLTextureSource.TextureId => _disposed || !_fboInitialized ? 0u : _texture;

    nint IGLTextureSource.ShareGroup => _creationContext;

    void IGLTextureSource.ConfigureWrap(bool repeatX, bool repeatY)
        => ConfigureGpuTextureWrap((nint)_texture, repeatX, repeatY);

    // IGpuTextureSource — GL FBOs store row 0 at the bottom of the image (bottom-up).
    // Consumers that mix FBO output with top-down sources flip V at sample time.
    bool IGpuTextureSource.YFlipped => true;

    public byte[] CopyPixels()
    {
        if (_disposed)
        {
            return Array.Empty<byte>();
        }

        FlushFboReadbackIfNeeded();
        var pixels = EnsurePixelBuffer();
        var copy = new byte[pixels.Length];
        Buffer.BlockCopy(pixels, 0, copy, 0, pixels.Length);
        return copy;
    }

    public Span<byte> GetPixelSpan()
    {
        if (_disposed)
        {
            return Span<byte>.Empty;
        }

        FlushFboReadbackIfNeeded();
        return EnsurePixelBuffer().AsSpan();
    }

    public void Clear(Color color)
    {
        if (_disposed)
        {
            return;
        }

        // No CPU-side state to clear when no one has touched it yet. The FBO is cleared
        // separately by the GL pipeline (PreparePixelSurface -> glClear). Skipping allocation
        // here is the main GC win.
        if (_pixels is null)
        {
            IncrementVersion();
            return;
        }

        // Raw byte write (no premultiply). The premultiply variant was tried for the
        // X11 ColorPicker white-halo issue but suspected to break AllowsTransparency
        // layered windows on Win32. Reverted pending root-cause analysis on both fronts.
        byte b = color.B;
        byte g = color.G;
        byte r = color.R;
        byte a = color.A;

        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i + 0] = b;
            _pixels[i + 1] = g;
            _pixels[i + 2] = r;
            _pixels[i + 3] = a;
        }

        IncrementVersion();
    }

    /// <inheritdoc cref="IGpuTextureSource.GetTextureHandle"/>
    public nint GetTextureHandle()
    {
        // Only valid after InitializeFbo has been called and FBO is healthy. Consumers
        // (MewVGImage) use this to skip the readback + re-upload round-trip.
        return _disposed || !_fboInitialized ? 0 : (nint)_texture;
    }

    /// <inheritdoc cref="IGpuTextureSource.ConfigureGpuTextureWrap"/>
    public void ConfigureGpuTextureWrap(nint handle, bool repeatX, bool repeatY)
    {
        // FBO color attachment is created with GL_CLAMP_TO_EDGE (right for filter sampling,
        // wrong for tiled image-brush use). Force the wrap mode here so the next sampler that
        // binds this texture (NVG image-brush draw) actually tiles. Persistent state mutation
        // is OK — other consumers (filter executor) bind their own samplers and don't depend
        // on a specific wrap default.
        if (handle == 0 || handle != (nint)_texture || _disposed) return;
        int wrapS = repeatX ? (int)GL_REPEAT : (int)GL.GL_CLAMP_TO_EDGE;
        int wrapT = repeatY ? (int)GL_REPEAT : (int)GL.GL_CLAMP_TO_EDGE;
        GL.BindTexture(GL.GL_TEXTURE_2D, _texture);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, wrapS);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, wrapT);
        GL.BindTexture(GL.GL_TEXTURE_2D, 0);
    }

    private const uint GL_REPEAT = 0x2901;

    private sealed class ExternalWriteScope : IExternalGpuWriteScope, IGpuResourceAffinityProvider
    {
        private readonly OpenGLPixelRenderSurface _surface;
        private readonly int _previousFramebuffer;
        private readonly int[] _previousViewport = new int[4];
        private bool _disposed;

        public ExternalWriteScope(OpenGLPixelRenderSurface surface)
        {
            _surface = surface;
            _previousFramebuffer = GL.GetInteger(OpenGLExt.GL_FRAMEBUFFER_BINDING);
            GL.GetIntegers(OpenGLExt.GL_VIEWPORT, _previousViewport);
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, surface._fbo);
            GL.Viewport(0, 0, surface.PixelWidth, surface.PixelHeight);
        }

        public int PixelWidth => _surface.PixelWidth;

        public int PixelHeight => _surface.PixelHeight;

        public bool YFlipped => true;

        public nint NativeHandle => (nint)_surface._texture;

        public nint NativeAlternateHandle => (nint)_surface._fbo;

        public nint NativeDeviceHandle => 0;

        public GpuResourceAffinity? Affinity => _surface.Affinity;

        public void Flush() => GL.Flush();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, (uint)Math.Max(0, _previousFramebuffer));
            GL.Viewport(_previousViewport[0], _previousViewport[1], _previousViewport[2], _previousViewport[3]);
        }
    }

    /// <inheritdoc cref="IGpuTextureSource.RetainGpuHandle"/>
    public bool RetainGpuHandle(nint handle)
    {
        // GL textures aren't ARC-counted by the driver; we maintain our own count to
        // delay GL resource release until every NoDelete-wrap consumer has flushed.
        if (handle == 0 || handle != (nint)_texture) return false;
        Interlocked.Increment(ref _externalRetainCount);
        return true;
    }

    /// <inheritdoc cref="IGpuTextureSource.ReleaseGpuHandle"/>
    public void ReleaseGpuHandle(nint handle)
    {
        if (handle == 0 || handle != (nint)_texture) return;
        int remaining = Interlocked.Decrement(ref _externalRetainCount);
        if (remaining < 0)
        {
            // Defensive: extra release without a matching retain.
            Interlocked.Increment(ref _externalRetainCount);
            return;
        }
        if (remaining == 0 && _disposeDeferredForRetain)
        {
            // Deferred Dispose was waiting on the last retain — finish it now via the
            // queue so the GL release runs under the owning context.
            _disposeDeferredForRetain = false;
            if (_glDisposeRequested is { } hook)
            {
                hook(this);
            }
            else
            {
                ReleaseGLResources();
            }
        }
    }

    public PixelBufferLock Lock()
    {
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(OpenGLPixelRenderSurface));
        }

        FlushFboReadbackIfNeeded();
        var pixels = EnsurePixelBuffer();
        int size = pixels.Length;
        if (_lockBuffer == null || _lockBuffer.Length != size)
        {
            _lockBuffer = new byte[size];
        }

        Buffer.BlockCopy(pixels, 0, _lockBuffer, 0, size);

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

    public IExternalGpuWriteScope BeginExternalWrite()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OpenGLPixelRenderSurface));
        }

        InitializeFbo();
        if (!_fboInitialized || _fbo == 0 || _texture == 0)
        {
            throw new InvalidOperationException("OpenGL external write target could not initialize its FBO.");
        }

        if (_creationContext == 0 && _currentContextProvider is not null)
        {
            RecordCreationContext(_currentContextProvider());
        }

        return new ExternalWriteScope(this);
    }

    public void MarkExternalContentChanged()
    {
        RequestDeferredReadback();
        IncrementVersion();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        int retainSnapshot = Volatile.Read(ref _externalRetainCount);

        // If the FBO color texture is still externally retained (e.g. a MewVGImage wrapped
        // it zero-copy with NoDelete and the consumer's NVG flush hasn't run yet), defer
        // GL release until the last ReleaseGpuHandle drops the count to zero. Releasing
        // here would delete a texture NVG is about to bind for setFragmentTexture.
        if (retainSnapshot > 0)
        {
            _disposeDeferredForRetain = true;
            return;
        }

        // GL resources (FBO, texture, renderbuffer) live in whichever context
        // created them — typically the offscreen context on Win32. Deleting
        // them against the wrong current context (e.g., the main window's)
        // silently fails AND corrupts that context's object namespace. Queue
        // the release so the owning context can drain it under its own
        // wglMakeCurrent scope; if no queuing hook is attached, fall back to
        // releasing under whatever context is current (best-effort only — may
        // leak if none is active).
        if (_fboInitialized)
        {
            if (_glDisposeRequested is { } hook)
            {
                hook(this);
            }
            else
            {
                ReleaseGLResources();
            }
        }
    }

    /// <summary>
    /// Releases the FBO, texture, and renderbuffer. The caller must ensure
    /// the GL context that created these resources is current.
    /// </summary>
    internal unsafe void ReleaseGLResources()
    {
        if (!_fboInitialized)
        {
            return;
        }

        if (_fbo != 0)
        {
            uint fbo = _fbo;
            OpenGLExt.DeleteFramebuffers(1, &fbo);
            _fbo = 0;
        }

        if (_stencilRenderbuffer != 0)
        {
            uint rb = _stencilRenderbuffer;
            OpenGLExt.DeleteRenderbuffers(1, &rb);
            _stencilRenderbuffer = 0;
        }

        if (_texture != 0)
        {
            uint tex = _texture;
            GL.DeleteTextures(1, ref tex);
            _texture = 0;
        }

        _hasStencil = false;
        _fboInitialized = false;
    }

    /// <summary>
    /// Initializes FBO resources. Must be called with a valid GL context current.
    /// </summary>
    internal unsafe void InitializeFbo()
    {
        if (_disposed || _fboInitialized)
        {
            return;
        }

        if (!OpenGLExt.IsSupported)
        {
            return;
        }

        // Generate texture
        GL.GenTextures(1, out _texture);
        if (_texture == 0)
        {
            return;
        }

        GL.BindTexture(GL.GL_TEXTURE_2D, _texture);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_LINEAR);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP_TO_EDGE);

        // Allocate texture storage
        GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, PixelWidth, PixelHeight, 0,
            GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, 0);

        // Generate FBO
        uint fbo = 0;
        OpenGLExt.GenFramebuffers(1, &fbo);
        if (fbo == 0)
        {
            uint tex = _texture;
            GL.DeleteTextures(1, ref tex);
            _texture = 0;
            return;
        }
        _fbo = fbo;

        // Attach texture to FBO
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _fbo);
        OpenGLExt.FramebufferTexture2D(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_COLOR_ATTACHMENT0,
            GL.GL_TEXTURE_2D, _texture, 0);

        int stencilBits = Math.Max(0, GraphicsRuntimeOptions.PreferredMewVGStencilBits);
        if (stencilBits > 0)
        {
            uint renderbuffer = 0;
            OpenGLExt.GenRenderbuffers(1, &renderbuffer);
            if (renderbuffer != 0)
            {
                _stencilRenderbuffer = renderbuffer;
                OpenGLExt.BindRenderbuffer(OpenGLExt.GL_RENDERBUFFER, _stencilRenderbuffer);
                OpenGLExt.RenderbufferStorage(OpenGLExt.GL_RENDERBUFFER, OpenGLExt.GL_DEPTH24_STENCIL8, PixelWidth, PixelHeight);
                OpenGLExt.FramebufferRenderbuffer(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_DEPTH_STENCIL_ATTACHMENT,
                    OpenGLExt.GL_RENDERBUFFER, _stencilRenderbuffer);
                OpenGLExt.BindRenderbuffer(OpenGLExt.GL_RENDERBUFFER, 0);
            }
        }

        // Check completeness
        uint status = OpenGLExt.CheckFramebufferStatus(OpenGLExt.GL_FRAMEBUFFER);
        if (status != OpenGLExt.GL_FRAMEBUFFER_COMPLETE && _stencilRenderbuffer != 0)
        {
            OpenGLExt.FramebufferRenderbuffer(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_DEPTH_STENCIL_ATTACHMENT,
                OpenGLExt.GL_RENDERBUFFER, 0);

            uint rb = _stencilRenderbuffer;
            OpenGLExt.DeleteRenderbuffers(1, &rb);
            _stencilRenderbuffer = 0;

            status = OpenGLExt.CheckFramebufferStatus(OpenGLExt.GL_FRAMEBUFFER);
        }

        if (status != OpenGLExt.GL_FRAMEBUFFER_COMPLETE)
        {
            // Cleanup on failure
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
            OpenGLExt.DeleteFramebuffers(1, &fbo);
            _fbo = 0;
            if (_stencilRenderbuffer != 0)
            {
                uint rb = _stencilRenderbuffer;
                OpenGLExt.DeleteRenderbuffers(1, &rb);
                _stencilRenderbuffer = 0;
            }
            uint tex = _texture;
            GL.DeleteTextures(1, ref tex);
            _texture = 0;
            return;
        }

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        GL.BindTexture(GL.GL_TEXTURE_2D, 0);
        _hasStencil = _stencilRenderbuffer != 0;
        _fboInitialized = true;
    }

    /// <summary>
    /// Marks the FBO contents as newer than the CPU pixel mirror. Use after a GPU write
    /// (e.g. <see cref="OpenGL.OpenGLGaussianBlur"/>) instead of an immediate readback —
    /// the next CPU consumer of <c>_pixels</c> (Lock / CopyPixels / GetPixelSpan) flushes
    /// it via <see cref="FlushFboReadbackIfNeeded"/>. Folds N per-filter sync points into
    /// at most one when many GPU passes feed a single CPU consumer.
    /// </summary>
    internal void RequestDeferredReadback()
    {
        _fboNewerThanCpu = true;
    }

    /// <summary>
    /// If a GPU write has been deferred, performs the readback now. Must be called on the
    /// GL render thread. Called from CPU-pixel consumers below.
    /// </summary>
    private void FlushFboReadbackIfNeeded()
    {
        if (!_fboNewerThanCpu) return;
        if (!_fboInitialized || _fbo == 0)
        {
            _fboNewerThanCpu = false;
            return;
        }
        // ReadbackFromFbo binds GL_READ_FRAMEBUFFER itself; capture-restore not needed here
        // because callers (Lock/CopyPixels/GetPixelSpan) don't promise a stable binding.
        ReadbackFromFbo();
        _fboNewerThanCpu = false;
    }

    /// <summary>
    /// Reads pixels from the FBO back to CPU buffer. Must be called with GL context current
    /// and FBO bound.
    /// </summary>
    internal unsafe void ReadbackFromFbo()
    {
        if (_disposed || !_fboInitialized || _fbo == 0)
        {
            return;
        }

        var pixels = EnsurePixelBuffer();
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_READ_FRAMEBUFFER, _fbo);

        fixed (byte* p = pixels)
        {
            GL.ReadPixels(0, 0, PixelWidth, PixelHeight, GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)p);
        }

        // OpenGL reads with bottom-left origin, flip vertically
        FlipVertical(pixels);

        // Convert RGBA to BGRA
        ImagePixelUtils.ConvertRgbaToBgraInPlace(pixels);

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_READ_FRAMEBUFFER, 0);
    }

    /// <summary>
    /// Uploads CPU buffer to FBO texture. Must be called with GL context current.
    /// </summary>
    internal unsafe void UploadToFbo()
    {
        if (_disposed || !_fboInitialized || _texture == 0 || _pixels is null)
        {
            // No CPU bytes ever populated → nothing to upload (the FBO content is the
            // source of truth for the pure GPU path).
            return;
        }

        var pixels = _pixels;
        // Convert BGRA→RGBA + flip vertically into cached upload buffer
        int size = pixels.Length;
        if (_uploadBuffer == null || _uploadBuffer.Length != size)
        {
            _uploadBuffer = new byte[size];
        }

        int stride = PixelWidth * 4;
        for (int y = 0; y < PixelHeight; y++)
        {
            int srcOffset = y * stride;
            int dstOffset = (PixelHeight - 1 - y) * stride;
            for (int i = 0; i < stride; i += 4)
            {
                _uploadBuffer[dstOffset + i] = pixels[srcOffset + i + 2];     // R
                _uploadBuffer[dstOffset + i + 1] = pixels[srcOffset + i + 1]; // G
                _uploadBuffer[dstOffset + i + 2] = pixels[srcOffset + i];     // B
                _uploadBuffer[dstOffset + i + 3] = pixels[srcOffset + i + 3]; // A
            }
        }

        GL.BindTexture(GL.GL_TEXTURE_2D, _texture);
        fixed (byte* p = _uploadBuffer)
        {
            GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, PixelWidth, PixelHeight, 0,
                GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)p);
        }
        GL.BindTexture(GL.GL_TEXTURE_2D, 0);
    }

    private void FlipVertical(byte[] pixels)
    {
        int stride = PixelWidth * 4;
        var temp = new byte[stride];
        int halfHeight = PixelHeight / 2;

        for (int y = 0; y < halfHeight; y++)
        {
            int topOffset = y * stride;
            int bottomOffset = (PixelHeight - 1 - y) * stride;

            Buffer.BlockCopy(pixels, topOffset, temp, 0, stride);
            Buffer.BlockCopy(pixels, bottomOffset, pixels, topOffset, stride);
            Buffer.BlockCopy(temp, 0, pixels, bottomOffset, stride);
        }
    }
}

