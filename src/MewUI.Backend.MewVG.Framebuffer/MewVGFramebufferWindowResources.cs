using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.Framebuffer;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// GL state of the framebuffer window. A DRM/KMS surface hands out scan-out buffers that are imported as EGL
/// images and drawn through framebuffer objects, so no EGL window surface and no swap stands between the frame
/// and the page flip; a CPU surface gets a framebuffer object the frame is drawn into and read back from.
/// </summary>
internal sealed unsafe class MewVGFramebufferWindowResources : IDisposable, IMewVGWindowCacheMaintenance
{
    private static delegate* unmanaged<uint, nint, void> _glEGLImageTargetRenderbufferStorage;

    private readonly FramebufferEglDevice _device;
    private readonly nint _context;
    private readonly nint _eglSurface;
    private readonly IDrmKmsWindowSurface? _drmSurface;
    private readonly ICpuFramebufferWindowSurface? _cpuSurface;
    private readonly bool _importsModifiers;
    private readonly Dictionary<nint, ScanoutTarget> _scanoutTargets = [];
    private readonly uint _frameTexture;
    private readonly uint _frameFbo;
    private nint _frameBuffer;
    private byte[]? _readbackBuffer;
    private MewVGFramebufferGraphicsContext? _cachedContext;
    private readonly FramebufferFrameCapture? _capture = FramebufferFrameCapture.FromEnvironment();
    private bool _disposed;

    private MewVGFramebufferWindowResources(
        FramebufferEglDevice device,
        nint context,
        nint eglSurface,
        IDrmKmsWindowSurface? drmSurface,
        ICpuFramebufferWindowSurface? cpuSurface,
        bool importsModifiers,
        uint frameTexture,
        uint frameFbo,
        MewVGGL vg)
    {
        _device = device;
        _context = context;
        _eglSurface = eglSurface;
        _drmSurface = drmSurface;
        _cpuSurface = cpuSurface;
        _importsModifiers = importsModifiers;
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
    /// True when the output says how many presents ago each scan-out buffer was drawn, so a frame only copies
    /// what changed since. MEWUI_FB_BUFFER_AGE=0 turns it off.
    /// </summary>
    public bool TracksBufferAge { get; private init; }

    /// <summary>How many presents ago the buffer of this frame was drawn, or 0 when its contents are undefined.</summary>
    public int QueryBufferAge()
        => _drmSurface == null || _frameBuffer == 0 ? 0 : _drmSurface.GetBufferAge(_frameBuffer);

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
        var drmSurface = surface as IDrmKmsWindowSurface;
        var cpuSurface = surface as ICpuFramebufferWindowSurface;
        bool importsModifiers = false;
        if (drmSurface != null)
        {
            if (!device.SupportsSurfaceless)
            {
                throw new InvalidOperationException("EGL: the GBM display lacks EGL_KHR_surfaceless_context, which drawing into scan-out buffers needs.");
            }

            if (!LibEgl.HasExtension(device.Display, "EGL_KHR_image_base") || !LibEgl.HasExtension(device.Display, "EGL_EXT_image_dma_buf_import"))
            {
                throw new InvalidOperationException("EGL: the GBM display cannot import dma-buf images (EGL_KHR_image_base and EGL_EXT_image_dma_buf_import).");
            }

            importsModifiers = LibEgl.HasExtension(device.Display, "EGL_EXT_image_dma_buf_import_modifiers");
        }

        nint context = LibEgl.eglCreateContext(device.Display, device.Config, device.WorkerContext,
            FramebufferEglDevice.ContextAttributes(device.IsGles));
        if (context == LibEgl.EGL_NO_CONTEXT)
        {
            throw new InvalidOperationException($"EGL: window context creation failed (0x{LibEgl.eglGetError():X}).");
        }

        nint eglSurface = device.OffscreenSurface;
        if (!LibEgl.eglMakeCurrent(device.Display, eglSurface, eglSurface, context))
        {
            throw new InvalidOperationException($"EGL: eglMakeCurrent for the window failed (0x{LibEgl.eglGetError():X}).");
        }

        try
        {
            MewVGGLBootstrapFramebuffer.EnsureInitialized(device.IsGles);
            Console.Error.WriteLine($"[MewUI.Framebuffer] GL: {GL.GetRendererString()} / {GL.GetVersionString()}");
            uint frameTexture = 0;
            uint frameFbo = 0;
            if (cpuSurface != null)
            {
                (frameTexture, frameFbo) = CreateFrameBuffer(surface.PixelWidth, surface.PixelHeight);
            }

            if (drmSurface != null)
            {
                _glEGLImageTargetRenderbufferStorage = (delegate* unmanaged<uint, nint, void>)EglGLLoader.Resolve("glEGLImageTargetRenderbufferStorageOES");
                if (_glEGLImageTargetRenderbufferStorage == null)
                {
                    throw new InvalidOperationException("GL: glEGLImageTargetRenderbufferStorageOES is missing, so scan-out buffers cannot be drawn into.");
                }
            }

            // MewVG draws without depth or stencil and blends premultiplied colour onto the target.
            GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
            GL.Disable(0x0B44 /* GL_CULL_FACE */);
            GL.Enable(GL.GL_BLEND);
            GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);

            var vg = new MewVGGL();
            bool bufferAge = drmSurface != null && Environment.GetEnvironmentVariable("MEWUI_FB_BUFFER_AGE") != "0";
            Console.Error.WriteLine($"[MewUI.Framebuffer] buffer age: {(bufferAge ? "on" : "off")}");
            return new MewVGFramebufferWindowResources(device, context, eglSurface, drmSurface, cpuSurface, importsModifiers, frameTexture, frameFbo, vg)
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
        CheckFrameTargetComplete(width, height);
        ClearFrameTarget();
        return (texture, fbo);
    }

    /// <summary>Wraps a scan-out buffer in an EGL image and a framebuffer object that renders into it.</summary>
    private ScanoutTarget CreateScanoutTarget(nint bo)
    {
        int width = _drmSurface!.PixelWidth;
        int height = _drmSurface.PixelHeight;
        var export = _drmSurface.ExportBuffer(bo);
        nint image;
        try
        {
            var attributes = new List<int>
            {
                LibEgl.EGL_WIDTH, width,
                LibEgl.EGL_HEIGHT, height,
                LibEgl.EGL_LINUX_DRM_FOURCC_EXT, (int)_drmSurface.GbmFormat,
                LibEgl.EGL_DMA_BUF_PLANE0_FD_EXT, export.DmaBufFd,
                LibEgl.EGL_DMA_BUF_PLANE0_OFFSET_EXT, (int)export.Offset,
                LibEgl.EGL_DMA_BUF_PLANE0_PITCH_EXT, (int)export.Pitch,
            };
            if (_importsModifiers && export.HasModifier)
            {
                attributes.AddRange([
                    LibEgl.EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT, (int)(uint)(export.Modifier & 0xFFFFFFFF),
                    LibEgl.EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT, (int)(uint)(export.Modifier >> 32),
                ]);
            }

            attributes.Add(LibEgl.EGL_NONE);
            image = LibEgl.CreateImage(_device.Display, LibEgl.EGL_LINUX_DMA_BUF_EXT, [.. attributes]);
        }
        finally
        {
            LibC.Close(export.DmaBufFd);
        }

        if (image == 0)
        {
            throw new InvalidOperationException($"EGL: importing a scan-out buffer as an image failed (0x{LibEgl.eglGetError():X}).");
        }

        uint renderbuffer = 0;
        OpenGLExt.GenRenderbuffers(1, &renderbuffer);
        OpenGLExt.BindRenderbuffer(OpenGLExt.GL_RENDERBUFFER, renderbuffer);
        _glEGLImageTargetRenderbufferStorage(OpenGLExt.GL_RENDERBUFFER, image);
        uint fbo = 0;
        OpenGLExt.GenFramebuffers(1, &fbo);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, fbo);
        OpenGLExt.FramebufferRenderbuffer(OpenGLExt.GL_FRAMEBUFFER, OpenGLExt.GL_COLOR_ATTACHMENT0, OpenGLExt.GL_RENDERBUFFER, renderbuffer);
        CheckFrameTargetComplete(width, height);
        ClearFrameTarget();
        return new ScanoutTarget(image, renderbuffer, fbo);
    }

    private static void CheckFrameTargetComplete(int width, int height)
    {
        uint status = OpenGLExt.CheckFramebufferStatus(OpenGLExt.GL_FRAMEBUFFER);
        if (status != OpenGLExt.GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"The {width}x{height} frame buffer object is incomplete (0x{status:X}).");
        }
    }

    private static void ClearFrameTarget()
    {
        // The buffer starts opaque black, as the screen does, so a first partial frame shows no garbage.
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
    }

    public void MakeCurrent()
    {
        if (!_disposed)
        {
            LibEgl.eglMakeCurrent(_device.Display, _eglSurface, _eglSurface, _context);
        }
    }

    public void ReleaseCurrent() => _device.ReleaseCurrent();

    /// <summary>Takes the scan-out buffer this frame draws into; the CPU path always draws into its own frame buffer object.</summary>
    public void AcquireFrameTarget()
    {
        if (_drmSurface == null)
        {
            return;
        }

        _frameBuffer = _drmSurface.AcquireBuffer();
        if (_frameBuffer == 0)
        {
            throw new InvalidOperationException("The DRM output has no free scan-out buffer; the host only draws while it reports it can take a frame.");
        }

        if (!_scanoutTargets.ContainsKey(_frameBuffer))
        {
            _scanoutTargets[_frameBuffer] = CreateScanoutTarget(_frameBuffer);
        }
    }

    /// <summary>Binds the buffer the window frame is drawn into.</summary>
    public void BindFrameTarget() => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, FrameTargetFbo);

    private uint FrameTargetFbo => _drmSurface != null ? _scanoutTargets[_frameBuffer].Fbo : _frameFbo;

    /// <summary>
    /// Puts the finished frame on screen: a page flip of the scan-out buffer it was drawn into, or a read back of
    /// <paramref name="areas"/> (pixels, top-down; null for all of it) copied to the device.
    /// </summary>
    public void Present(IReadOnlyList<PixelArea>? areas)
    {
        if (_drmSurface != null)
        {
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, FrameTargetFbo);
            _capture?.TryCapture(_drmSurface.PixelWidth, _drmSurface.PixelHeight, _device.IsGles);
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);

            // The flip waits for the buffer's GPU work through the kernel's fence on it, so the commands only have
            // to be submitted here, never finished: the CPU goes on to the next frame while the GPU draws this one.
            GL.Flush();
            _drmSurface.Present(_frameBuffer);
            _frameBuffer = 0;
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
        _cpuSurface.WaitForVerticalBlank();
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
        fixed (byte* buffer = _readbackBuffer)
        {
            GL.ReadPixels(area.X, surfaceHeight - area.Y - area.Height, area.Width, area.Height,
                bgra ? GL.GL_BGRA_EXT : GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, (nint)buffer);
        }

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

        foreach (var target in _scanoutTargets.Values)
        {
            uint fbo = target.Fbo;
            OpenGLExt.DeleteFramebuffers(1, &fbo);
            uint renderbuffer = target.Renderbuffer;
            OpenGLExt.DeleteRenderbuffers(1, &renderbuffer);
            LibEgl.DestroyImage(_device.Display, target.Image);
        }

        _scanoutTargets.Clear();
        _disposed = true;
        ReleaseCurrent();
        LibEgl.eglDestroyContext(_device.Display, _context);
    }

    private readonly record struct ScanoutTarget(nint Image, uint Renderbuffer, uint Fbo);
}

/// <summary>A rectangle of device pixels, top-down.</summary>
internal readonly record struct PixelArea(int X, int Y, int Width, int Height);
