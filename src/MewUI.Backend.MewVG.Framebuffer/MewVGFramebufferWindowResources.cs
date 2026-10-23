using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// MewVG state of the framebuffer window on top of its GL resources, which own the context and the frame target
/// (scan-out buffers or a CPU-copied framebuffer object).
/// </summary>
internal sealed class MewVGFramebufferWindowResources : IDisposable, IMewVGWindowCacheMaintenance
{
    private readonly FramebufferOpenGLWindowResources _gl;
    private MewVGFramebufferGraphicsContext? _cachedContext;
    private bool _disposed;

    private MewVGFramebufferWindowResources(FramebufferOpenGLWindowResources gl, MewVGGL vg)
    {
        _gl = gl;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
    }

    public MewVGGL Vg { get; }

    public MewVGTextCache TextCache { get; }

    public nint OpenGLShareGroup => _gl.Device.WorkerContext;

    public bool DrawsInPlace => _gl.DrawsInPlace;

    public bool TracksBufferAge => _gl.TracksBufferAge;

    public bool TargetIsTopDown => _gl.TargetIsTopDown;

    public int QueryBufferAge() => _gl.QueryBufferAge();

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
        var gl = FramebufferOpenGLWindowResources.Create(device, surface);
        gl.MakeCurrent();
        try
        {
            MewVGGLBootstrapFramebuffer.EnsureInitialized(device.IsGles);
            Console.Error.WriteLine($"[MewUI.Framebuffer] GL: {GL.GetRendererString()} / {GL.GetVersionString()}");

            // MewVG draws without depth or stencil and blends premultiplied colour onto the target.
            GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
            GL.Disable(0x0B44 /* GL_CULL_FACE */);
            GL.Enable(GL.GL_BLEND);
            GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);

            var vg = new MewVGGL();
            Console.Error.WriteLine($"[MewUI.Framebuffer] buffer age: {(gl.TracksBufferAge ? "on" : "off")}");
            return new MewVGFramebufferWindowResources(gl, vg);
        }
        catch
        {
            gl.Dispose();
            throw;
        }
        finally
        {
            gl.ReleaseCurrent();
        }
    }

    public void MakeCurrent() => _gl.MakeCurrent();

    public void ReleaseCurrent() => _gl.ReleaseCurrent();

    /// <summary>Takes the target this frame draws into.</summary>
    public void AcquireFrameTarget() => _gl.AcquireFrameTarget();

    /// <summary>Binds the target the window frame is drawn into.</summary>
    public void BindFrameTarget() => _gl.BindFrameTarget();

    /// <summary>Puts the finished frame on screen; <paramref name="areas"/> limits a copy to what changed (null for all of it).</summary>
    public void Present(IReadOnlyList<PixelArea>? areas) => _gl.Present(areas);

    public void TrimCaches()
    {
        if (_disposed)
        {
            return;
        }

        _gl.MakeCurrent();
        try
        {
            TextCache.Clear();
            TextCache.ReleasePendingDeletes();
        }
        finally
        {
            _gl.ReleaseCurrent();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cachedContext?.Dispose();
        _cachedContext = null;

        _gl.MakeCurrent();
        TextCache.Dispose();
        if (Vg is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _gl.ReleaseCurrent();
        _gl.Dispose();
    }
}
