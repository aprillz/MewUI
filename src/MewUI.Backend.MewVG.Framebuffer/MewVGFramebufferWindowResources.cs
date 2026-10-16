using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.Framebuffer;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// GL state of the framebuffer window. A DRM/KMS surface gets an EGL window surface on its GBM surface; a CPU
/// surface gets a framebuffer object the frame is drawn into and read back from.
/// </summary>
internal sealed unsafe class MewVGFramebufferWindowResources : IDisposable, IMewVGWindowCacheMaintenance
{
    private readonly FramebufferEglDevice _device;
    private readonly nint _context;
    private readonly nint _eglSurface;
    private readonly IDrmKmsWindowSurface? _drmSurface;
    private readonly ICpuFramebufferWindowSurface? _cpuSurface;
    private readonly uint _frameTexture;
    private readonly uint _frameFbo;
    private byte[]? _readbackBuffer;
    private static readonly Diagnostics.EnvDebugLogger Logger = new("MEWUI_FB_DEBUG", "[FB][CPU]");
    private long _readTicks;
    private long _copyTicks;
    private long _pixelsCopied;
    private MewVGFramebufferGraphicsContext? _cachedContext;
    private readonly FramebufferFrameCapture? _capture = FramebufferFrameCapture.FromEnvironment();
    private bool _disposed;

    private MewVGFramebufferWindowResources(
        FramebufferEglDevice device,
        nint context,
        nint eglSurface,
        IDrmKmsWindowSurface? drmSurface,
        ICpuFramebufferWindowSurface? cpuSurface,
        uint frameTexture,
        uint frameFbo,
        MewVGGL vg)
    {
        _device = device;
        _context = context;
        _eglSurface = eglSurface;
        _drmSurface = drmSurface;
        _cpuSurface = cpuSurface;
        _frameTexture = frameTexture;
        _frameFbo = frameFbo;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
    }

    public MewVGGL Vg { get; }

    public MewVGTextCache TextCache { get; }

    public nint OpenGLShareGroup => _device.WorkerContext;

    /// <summary>True when frames go to the screen by CPU copy, so the frame buffer object keeps each frame.</summary>
    public bool DrawsInPlace => _cpuSurface != null;

    /// <summary>
    /// True when each swap-chain buffer reports how many presents ago it was drawn (EGL_EXT_buffer_age), so a frame
    /// only copies what changed since. MEWUI_FB_BUFFER_AGE=0 turns it off.
    /// </summary>
    public bool TracksBufferAge { get; private init; }

    /// <summary>How many presents ago the current back buffer was drawn, or 0 when its contents are undefined.</summary>
    public int QueryBufferAge()
        => LibEgl.eglQuerySurface(_device.Display, _eglSurface, LibEgl.EGL_BUFFER_AGE_EXT, out int age) ? age : 0;

    internal MewVGFramebufferGraphicsContext GetOrCreateContext(
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => _cachedContext ??= MewVGFramebufferGraphicsContext.CreateForWindow(this, offscreenProvider, gpuInteropInvalidated);

    internal void InvalidateCachedContext(MewVGFramebufferGraphicsContext context)
    {
        if (ReferenceEquals(_cachedContext, context))
        {
            _cachedContext = null;
        }
    }

    public static MewVGFramebufferWindowResources Create(FramebufferEglDevice device, IWindowSurface surface)
    {
        nint context = LibEgl.eglCreateContext(device.Display, device.Config, device.WorkerContext,
            FramebufferEglDevice.ContextAttributes(device.IsGles));
        if (context == LibEgl.EGL_NO_CONTEXT)
        {
            throw new InvalidOperationException($"EGL: window context creation failed (0x{LibEgl.eglGetError():X}).");
        }

        var drmSurface = surface as IDrmKmsWindowSurface;
        var cpuSurface = surface as ICpuFramebufferWindowSurface;
        nint eglSurface = device.OffscreenSurface;
        if (drmSurface != null)
        {
            eglSurface = LibEgl.eglCreateWindowSurface(device.Display, device.Config, drmSurface.GbmSurface, null);
            if (eglSurface == LibEgl.EGL_NO_SURFACE)
            {
                LibEgl.eglDestroyContext(device.Display, context);
                throw new InvalidOperationException($"EGL: window surface creation on the GBM surface failed (0x{LibEgl.eglGetError():X}).");
            }
        }

        if (!LibEgl.eglMakeCurrent(device.Display, eglSurface, eglSurface, context))
        {
            throw new InvalidOperationException($"EGL: eglMakeCurrent for the window failed (0x{LibEgl.eglGetError():X}).");
        }

        try
        {
            // The platform paces presents with page flips; EGL blocking on top of that would only add latency.
            if (drmSurface != null)
            {
                LibEgl.eglSwapInterval(device.Display, 0);
            }

            MewVGGLBootstrapFramebuffer.EnsureInitialized(device.IsGles);
            Console.Error.WriteLine($"[MewUI.Framebuffer] GL: {GL.GetRendererString()} / {GL.GetVersionString()}");
            uint frameTexture = 0;
            uint frameFbo = 0;
            if (cpuSurface != null)
            {
                (frameTexture, frameFbo) = CreateFrameBuffer(surface.PixelWidth, surface.PixelHeight);
            }

            // MewVG draws without depth or stencil and blends premultiplied colour onto the target.
            GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
            GL.Disable(0x0B44 /* GL_CULL_FACE */);
            GL.Enable(GL.GL_BLEND);
            GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);

            var vg = new MewVGGL();
            bool bufferAge = drmSurface != null
                && LibEgl.HasExtension(device.Display, "EGL_EXT_buffer_age")
                && Environment.GetEnvironmentVariable("MEWUI_FB_BUFFER_AGE") != "0";
            Console.Error.WriteLine($"[MewUI.Framebuffer] buffer age: {(bufferAge ? "on" : "off")}");
            return new MewVGFramebufferWindowResources(device, context, eglSurface, drmSurface, cpuSurface, frameTexture, frameFbo, vg)
            {
                TracksBufferAge = bufferAge,
            };
        }
        finally
        {
            device.ReleaseCurrent();
        }
    }

    private static (uint Texture, uint Fbo) CreateFrameBuffer(int width, int height)
    {
        GL.GenTextures(1, out uint texture);
        GL.BindTexture(GL.GL_TEXTURE_2D, texture);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_NEAREST);
        GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_NEAREST);
        GL.TexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, width, height, 0, GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, 0);

        uint fbo = 0;
        OpenGLExt.GenFramebuffers(1, &fbo);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, fbo);
        OpenGLExt.FramebufferTexture2D(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_COLOR_ATTACHMENT0, GL.GL_TEXTURE_2D, texture, 0);
        uint status = OpenGLExt.CheckFramebufferStatus(OpenGLExt.GL_FRAMEBUFFER);
        if (status != OpenGLExt.GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"The {width}x{height} frame buffer object is incomplete (0x{status:X}).");
        }

        // The buffer starts opaque black, as the screen does, so a first partial frame shows no garbage.
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        return (texture, fbo);
    }

    public void MakeCurrent()
    {
        if (!_disposed)
        {
            LibEgl.eglMakeCurrent(_device.Display, _eglSurface, _eglSurface, _context);
        }
    }

    public void ReleaseCurrent() => _device.ReleaseCurrent();

    /// <summary>Binds the buffer the window frame is drawn into.</summary>
    public void BindFrameTarget() => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _frameFbo);

    /// <summary>
    /// Puts the finished frame on screen: a buffer swap and page flip, or a read back of <paramref name="areas"/>
    /// (pixels, top-down; null for all of it) copied to the device.
    /// </summary>
    public void Present(IReadOnlyList<PixelArea>? areas)
    {
        if (_drmSurface != null)
        {
            if (_capture != null)
            {
                OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
                _capture.TryCapture(_drmSurface.PixelWidth, _drmSurface.PixelHeight, _device.IsGles);
            }

            LibEgl.eglSwapBuffers(_device.Display, _eglSurface);
            _drmSurface.OnBuffersSwapped();
            return;
        }

        if (_cpuSurface == null)
        {
            return;
        }

        int width = _cpuSurface.PixelWidth;
        int height = _cpuSurface.PixelHeight;
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _frameFbo);
        _capture?.TryCapture(width, height, _device.IsGles);
        long waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _cpuSurface.WaitForVerticalBlank();
        long waitTicks = System.Diagnostics.Stopwatch.GetTimestamp() - waitStart;
        _readTicks = 0;
        _copyTicks = 0;
        _pixelsCopied = 0;
        if (areas == null)
        {
            CopyArea(new PixelArea(0, 0, width, height), height);
        }
        else
        {
            for (int index = 0; index < areas.Count; index++)
            {
                CopyArea(areas[index], height);
            }
        }

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        if (Logger.Enabled)
        {
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Logger.Write($"present vsync {waitTicks * toMs:0.00}ms read {_readTicks * toMs:0.00}ms copy {_copyTicks * toMs:0.00}ms pixels {_pixelsCopied}");
        }
    }

    private void CopyArea(PixelArea area, int surfaceHeight)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        int stride = area.Width * 4;
        int length = stride * area.Height;
        if (_readbackBuffer == null || _readbackBuffer.Length < length)
        {
            _readbackBuffer = new byte[Math.Max(length, (_readbackBuffer?.Length ?? 0) * 2)];
        }

        // GL rows run bottom-up; desktop GL reads BGRA directly, GLES only RGBA.
        bool bgra = !_device.IsGles;
        long readStart = System.Diagnostics.Stopwatch.GetTimestamp();
        fixed (byte* buffer = _readbackBuffer)
        {
            GL.ReadPixels(area.X, surfaceHeight - area.Y - area.Height, area.Width, area.Height,
                bgra ? GL.GL_BGRA_EXT : GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)buffer);
        }

        long copyStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _readTicks += copyStart - readStart;
        _pixelsCopied += (long)area.Width * area.Height;
        var pixels = _readbackBuffer.AsSpan(0, length);
        if (!bgra)
        {
            SwapRedBlue(pixels);
        }

        for (int row = 0; row < area.Height; row++)
        {
            int sourceRow = area.Height - 1 - row;
            _cpuSurface!.CopyRows(pixels.Slice(sourceRow * stride, stride), stride, area.X, area.Y + row, area.Width, 1);
        }

        _copyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - copyStart;
    }

    private static void SwapRedBlue(Span<byte> pixels)
    {
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
        }
    }

    public void TrimCaches()
    {
        if (_disposed)
        {
            return;
        }

        MakeCurrent();
        try
        {
            TextCache.Clear();
            TextCache.ReleasePendingDeletes();
        }
        finally
        {
            ReleaseCurrent();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cachedContext?.Dispose();
        _cachedContext = null;

        MakeCurrent();
        TextCache.Dispose();
        if (Vg is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_frameFbo != 0)
        {
            uint fbo = _frameFbo;
            OpenGLExt.DeleteFramebuffers(1, &fbo);
            uint texture = _frameTexture;
            GL.DeleteTextures(1, ref texture);
        }

        _disposed = true;
        ReleaseCurrent();
        if (_drmSurface != null)
        {
            LibEgl.eglDestroySurface(_device.Display, _eglSurface);
        }

        LibEgl.eglDestroyContext(_device.Display, _context);
    }
}

/// <summary>A rectangle of device pixels, top-down.</summary>
internal readonly record struct PixelArea(int X, int Y, int Width, int Height);
