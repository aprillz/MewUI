using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.Framebuffer;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Draws into the DRM output's scan-out buffers: each one is imported as an EGL image and wrapped in a framebuffer
/// object, and the finished frame goes to the output's page flip without a swap in between.
/// </summary>
internal sealed unsafe class DrmScanoutOpenGLWindowResources : FramebufferOpenGLWindowResources
{
    private static delegate* unmanaged<uint, nint, void> _glEGLImageTargetRenderbufferStorage;

    private readonly IDrmKmsWindowSurface _surface;
    private readonly bool _importsModifiers;
    private readonly Dictionary<nint, ScanoutTarget> _targets = [];
    private nint _frameBuffer;

    private DrmScanoutOpenGLWindowResources(FramebufferEglDevice device, nint context, IDrmKmsWindowSurface surface, bool importsModifiers)
        : base(device, context)
    {
        _surface = surface;
        _importsModifiers = importsModifiers;
        TracksBufferAge = Environment.GetEnvironmentVariable("MEWUI_FB_BUFFER_AGE") != "0";
    }

    public override bool DrawsInPlace => false;

    /// <summary>True unless MEWUI_FB_BUFFER_AGE=0 turns it off; the output counts presents for every buffer.</summary>
    public override bool TracksBufferAge { get; }

    public override bool TargetIsTopDown => true;

    public static DrmScanoutOpenGLWindowResources Create(FramebufferEglDevice device, IDrmKmsWindowSurface surface)
    {
        if (!device.SupportsSurfaceless)
        {
            throw new InvalidOperationException("EGL: the GBM display lacks EGL_KHR_surfaceless_context, which drawing into scan-out buffers needs.");
        }

        if (!LibEgl.HasExtension(device.Display, "EGL_KHR_image_base") || !LibEgl.HasExtension(device.Display, "EGL_EXT_image_dma_buf_import"))
        {
            throw new InvalidOperationException("EGL: the GBM display cannot import dma-buf images (EGL_KHR_image_base and EGL_EXT_image_dma_buf_import).");
        }

        bool importsModifiers = LibEgl.HasExtension(device.Display, "EGL_EXT_image_dma_buf_import_modifiers");
        nint context = CreateCurrentContext(device);
        try
        {
            _glEGLImageTargetRenderbufferStorage = (delegate* unmanaged<uint, nint, void>)EglGLLoader.Resolve("glEGLImageTargetRenderbufferStorageOES");
            if (_glEGLImageTargetRenderbufferStorage == null)
            {
                LibEgl.eglDestroyContext(device.Display, context);
                throw new InvalidOperationException("GL: glEGLImageTargetRenderbufferStorageOES is missing, so scan-out buffers cannot be drawn into.");
            }

            return new DrmScanoutOpenGLWindowResources(device, context, surface, importsModifiers);
        }
        finally
        {
            device.ReleaseCurrent();
        }
    }

    public override void AcquireFrameTarget()
    {
        _frameBuffer = _surface.AcquireBuffer();
        if (_frameBuffer == 0)
        {
            throw new InvalidOperationException("The DRM output has no free scan-out buffer; the host only draws while it reports it can take a frame.");
        }

        if (!_targets.ContainsKey(_frameBuffer))
        {
            _targets[_frameBuffer] = CreateTarget(_frameBuffer);
        }
    }

    public override int QueryBufferAge() => _frameBuffer == 0 ? 0 : _surface.GetBufferAge(_frameBuffer);

    public override void BindFrameTarget() => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _targets[_frameBuffer].Fbo);

    public override void Present(IReadOnlyList<PixelArea>? areas)
    {
        if (Capture != null)
        {
            BindFrameTarget();
            Capture.TryCapture(_surface.PixelWidth, _surface.PixelHeight, Device.IsGles, topDown: true);
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        }

        // The flip waits for the buffer's GPU work through the kernel's fence on it, so the commands only have to be
        // submitted here, never finished: the CPU goes on to the next frame while the GPU draws this one.
        GL.Flush();
        _surface.Present(_frameBuffer);
        _frameBuffer = 0;
    }

    /// <summary>Wraps a scan-out buffer in an EGL image and a framebuffer object that renders into it.</summary>
    private ScanoutTarget CreateTarget(nint bo)
    {
        int width = _surface.PixelWidth;
        int height = _surface.PixelHeight;
        var export = _surface.ExportBuffer(bo);
        nint image;
        try
        {
            var attributes = new List<int>
            {
                LibEgl.EGL_WIDTH, width,
                LibEgl.EGL_HEIGHT, height,
                LibEgl.EGL_LINUX_DRM_FOURCC_EXT, (int)_surface.GbmFormat,
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
            image = LibEgl.CreateImage(Device.Display, LibEgl.EGL_LINUX_DMA_BUF_EXT, [.. attributes]);
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

    protected override void DeleteFrameTargets()
    {
        foreach (var target in _targets.Values)
        {
            uint fbo = target.Fbo;
            OpenGLExt.DeleteFramebuffers(1, &fbo);
            uint renderbuffer = target.Renderbuffer;
            OpenGLExt.DeleteRenderbuffers(1, &renderbuffer);
            LibEgl.DestroyImage(Device.Display, target.Image);
        }

        _targets.Clear();
    }

    private readonly record struct ScanoutTarget(nint Image, uint Renderbuffer, uint Fbo);
}
