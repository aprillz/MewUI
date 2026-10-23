using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.Framebuffer;
using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GL context and frame target of the framebuffer window. There is no EGL window surface: the context draws into a
/// framebuffer object and hands the finished frame to the platform's surface, by page flip or by CPU copy.
/// </summary>
internal abstract unsafe class FramebufferOpenGLWindowResources : IOpenGLWindowResources
{
    private bool _disposed;

    protected FramebufferOpenGLWindowResources(FramebufferEglDevice device, nint context)
    {
        Device = device;
        NativeContext = context;
    }

    public FramebufferEglDevice Device { get; }

    public nint NativeContext { get; }

    // Desktop GL reads and uploads BGRA directly; GLES takes RGBA only.
    public bool SupportsBgra => !Device.IsGles;

    public bool SupportsNpotTextures => true;

    /// <summary>True when the frame target keeps each frame, so the window repaints it in place.</summary>
    public abstract bool DrawsInPlace { get; }

    /// <summary>True when each frame target says how many presents ago it was drawn, so a frame only copies what changed since.</summary>
    public abstract bool TracksBufferAge { get; }

    /// <summary>True when the frame target's first row is the screen's top line, where GL puts y=0.</summary>
    public abstract bool TargetIsTopDown { get; }

    protected FramebufferFrameCapture? Capture { get; } = FramebufferFrameCapture.FromEnvironment();

    /// <summary>Creates the resources for the platform's window surface, leaving no context current.</summary>
    public static FramebufferOpenGLWindowResources Create(FramebufferEglDevice device, IWindowSurface surface)
        => surface switch
        {
            IDrmKmsWindowSurface drm => DrmScanoutOpenGLWindowResources.Create(device, drm),
            ICpuFramebufferWindowSurface cpu => CpuCopyOpenGLWindowResources.Create(device, cpu),
            _ => throw new ArgumentException("MewVG (framebuffer) requires a framebuffer platform window surface.", nameof(surface)),
        };

    /// <summary>Takes the target this frame draws into.</summary>
    public abstract void AcquireFrameTarget();

    /// <summary>How many presents ago the target of this frame was drawn, or 0 when its contents are undefined.</summary>
    public abstract int QueryBufferAge();

    /// <summary>Binds the target the window frame is drawn into.</summary>
    public abstract void BindFrameTarget();

    /// <summary>Puts the finished frame on screen; <paramref name="areas"/> limits a copy to what changed (null for all of it).</summary>
    public abstract void Present(IReadOnlyList<PixelArea>? areas);

    public void MakeCurrent()
    {
        if (!_disposed)
        {
            LibEgl.eglMakeCurrent(Device.Display, Device.OffscreenSurface, Device.OffscreenSurface, NativeContext);
        }
    }

    void IOpenGLWindowResources.MakeCurrent(nint deviceOrDisplay) => MakeCurrent();

    public void ReleaseCurrent() => Device.ReleaseCurrent();

    void IOpenGLWindowResources.SwapBuffers(nint deviceOrDisplay, nint nativeWindow) => Present(null);

    // The platform paces presents with page flips or vertical blanks, so there is no swap interval to set.
    void IOpenGLWindowResources.SetSwapInterval(int interval)
    {
    }

    public void TrackTexture(uint textureId)
    {
    }

    /// <summary>Creates a context sharing with the device's worker context and makes it current without a surface.</summary>
    protected static nint CreateCurrentContext(FramebufferEglDevice device)
    {
        nint context = LibEgl.eglCreateContext(device.Display, device.Config, device.WorkerContext,
            FramebufferEglDevice.ContextAttributes(device.IsGles));
        if (context == LibEgl.EGL_NO_CONTEXT)
        {
            throw new InvalidOperationException($"EGL: window context creation failed (0x{LibEgl.eglGetError():X}).");
        }

        if (!LibEgl.eglMakeCurrent(device.Display, device.OffscreenSurface, device.OffscreenSurface, context))
        {
            int error = LibEgl.eglGetError();
            LibEgl.eglDestroyContext(device.Display, context);
            throw new InvalidOperationException($"EGL: eglMakeCurrent for the window failed (0x{error:X}).");
        }

        return context;
    }

    protected static void CheckFrameTargetComplete(int width, int height)
    {
        uint status = OpenGLExt.CheckFramebufferStatus(OpenGLExt.GL_FRAMEBUFFER);
        if (status != OpenGLExt.GL_FRAMEBUFFER_COMPLETE)
        {
            throw new InvalidOperationException($"The {width}x{height} frame buffer object is incomplete (0x{status:X}).");
        }
    }

    protected static void ClearFrameTarget()
    {
        // The target starts opaque black, as the screen does, so a first partial frame shows no garbage.
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT);
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
    }

    /// <summary>Deletes the frame targets; the context is current.</summary>
    protected abstract void DeleteFrameTargets();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        MakeCurrent();
        DeleteFrameTargets();
        _disposed = true;
        ReleaseCurrent();
        LibEgl.eglDestroyContext(Device.Display, NativeContext);
    }
}

/// <summary>A rectangle of device pixels, top-down.</summary>
internal readonly record struct PixelArea(int X, int Y, int Width, int Height);
