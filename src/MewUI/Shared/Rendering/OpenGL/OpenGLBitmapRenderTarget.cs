using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// OpenGL implementation of IBitmapRenderTarget using FBO (Framebuffer Object).
/// Provides offscreen rendering with CPU-side pixel buffer access.
/// </summary>
internal sealed class OpenGLBitmapRenderTarget : IBitmapRenderTarget
{
    private readonly byte[] _pixels;
    private readonly object _gate = new();
    private int _version;
    private bool _disposed;

    // FBO resources (created lazily when GL context is available)
    private uint _fbo;

    private uint _texture;
    private uint _stencilRenderbuffer;
    private bool _fboInitialized;
    private bool _hasStencil;

    private byte[]? _lockBuffer;
    private byte[]? _uploadBuffer;
    private Action? _releaseAction;

    public OpenGLBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        HasAlpha = hasAlpha;
        // Allocate CPU-side pixel buffer
        _pixels = new byte[pixelWidth * pixelHeight * 4];
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

    /// <summary>NanoVG renders with SRC_ALPHA / ONE_MINUS_SRC_ALPHA blending into
    /// the FBO color attachment, producing premultiplied output.</summary>
    public bool IsPremultiplied => true;

    /// <summary>
    /// Mirrors the alpha-channel hint from construction. Consumers reading these pixels via
    /// <see cref="IPixelBufferSource"/> use this to skip alpha scans for opaque RTs (video
    /// frame target etc.).
    /// </summary>
    public bool HasAlpha { get; }

    public byte[] CopyPixels()
    {
        if (_disposed)
        {
            return Array.Empty<byte>();
        }

        var copy = new byte[_pixels.Length];
        Buffer.BlockCopy(_pixels, 0, copy, 0, _pixels.Length);
        return copy;
    }

    public Span<byte> GetPixelSpan()
    {
        if (_disposed)
        {
            return Span<byte>.Empty;
        }

        return _pixels.AsSpan();
    }

    public void Clear(Color color)
    {
        if (_disposed)
        {
            return;
        }

        // This RT reports IsPremultiplied=true (FBO output convention), so consumers
        // sample without re-multiplying RGB by alpha. We must therefore store the
        // pre-multiplied bytes here — otherwise Color.Transparent (= 0x00FFFFFF =
        // straight (R=255,G=255,B=255,A=0)) leaks into NVG src-over blending as
        // saturated white, painting opaque white anywhere the caller intended a
        // fully transparent fill (visible as a white wheel background on X11).
        byte a = color.A;
        byte b, g, r;
        if (a == 0xFF)
        {
            b = color.B;
            g = color.G;
            r = color.R;
        }
        else if (a == 0)
        {
            b = g = r = 0;
        }
        else
        {
            b = (byte)((color.B * a + 127) / 255);
            g = (byte)((color.G * a + 127) / 255);
            r = (byte)((color.R * a + 127) / 255);
        }

        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i + 0] = b;
            _pixels[i + 1] = g;
            _pixels[i + 2] = r;
            _pixels[i + 3] = a;
        }

        IncrementVersion();
    }

    public PixelBufferLock Lock()
    {
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(OpenGLBitmapRenderTarget));
        }

        int size = _pixels.Length;
        if (_lockBuffer == null || _lockBuffer.Length != size)
        {
            _lockBuffer = new byte[size];
        }

        Buffer.BlockCopy(_pixels, 0, _lockBuffer, 0, size);

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

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Note: FBO/texture cleanup requires a GL context to be current.
        // If called without context (e.g., during GC), resources may leak.
        // For proper cleanup, the factory should track and cleanup resources.
        if (_fboInitialized)
        {
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
    /// Reads pixels from the FBO back to CPU buffer. Must be called with GL context current
    /// and FBO bound.
    /// </summary>
    internal unsafe void ReadbackFromFbo()
    {
        if (_disposed || !_fboInitialized || _fbo == 0)
        {
            return;
        }

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_READ_FRAMEBUFFER, _fbo);

        fixed (byte* p = _pixels)
        {
            GL.ReadPixels(0, 0, PixelWidth, PixelHeight, GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)p);
        }

        // OpenGL reads with bottom-left origin, flip vertically
        FlipVertical();

        // Convert RGBA to BGRA
        ConvertRgbaToBgra();

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_READ_FRAMEBUFFER, 0);
    }

    /// <summary>
    /// Uploads CPU buffer to FBO texture. Must be called with GL context current.
    /// </summary>
    internal unsafe void UploadToFbo()
    {
        if (_disposed || !_fboInitialized || _texture == 0)
        {
            return;
        }

        // Convert BGRA→RGBA + flip vertically into cached upload buffer
        int size = _pixels.Length;
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
                _uploadBuffer[dstOffset + i] = _pixels[srcOffset + i + 2];     // R
                _uploadBuffer[dstOffset + i + 1] = _pixels[srcOffset + i + 1]; // G
                _uploadBuffer[dstOffset + i + 2] = _pixels[srcOffset + i];     // B
                _uploadBuffer[dstOffset + i + 3] = _pixels[srcOffset + i + 3]; // A
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

    private void FlipVertical()
    {
        int stride = PixelWidth * 4;
        var temp = new byte[stride];
        int halfHeight = PixelHeight / 2;

        for (int y = 0; y < halfHeight; y++)
        {
            int topOffset = y * stride;
            int bottomOffset = (PixelHeight - 1 - y) * stride;

            Buffer.BlockCopy(_pixels, topOffset, temp, 0, stride);
            Buffer.BlockCopy(_pixels, bottomOffset, _pixels, topOffset, stride);
            Buffer.BlockCopy(temp, 0, _pixels, bottomOffset, stride);
        }
    }

    private void ConvertRgbaToBgra()
        => ImagePixelUtils.ConvertRgbaToBgraInPlace(_pixels);
}
