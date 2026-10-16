using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGX11GraphicsContext
{
    private MewVGX11GraphicsContext(
        IFrameSession frameSession,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
    {
        _frameSession = frameSession;
        _vg = frameSession.Vg;
        _textCache = frameSession.TextCache;
        _gpuInteropInvalidated = gpuInteropInvalidated;
    }

    internal static MewVGX11GraphicsContext CreateForWindow(
        MewVGX11WindowResources resources,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => new(new X11WindowFrameSession(resources, offscreenProvider), gpuInteropInvalidated);

    internal void SetTarget(nint display, nint window, bool preferImmediatePresent)
    {
        if (_frameSession is X11WindowFrameSession windowSession)
        {
            windowSession.SetTarget(display, window, preferImmediatePresent);
        }
    }

    private static nint GetCurrentGLContext() => MewVGX11GraphicsFactory.GetCurrentGLContextStatic();

    private static nint GetCurrentNativeContext() => LibGL.glXGetCurrentContext();

    private static int GetSwapInterval()
    {
        if (!Application.IsRunning)
        {
            return 1;
        }

        return Application.Current.RenderLoopSettings.VSyncEnabled ? 1 : 0;
    }


    private sealed class X11WindowFrameSession : IFrameSession
    {
        private readonly MewVGX11WindowResources _resources;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private nint _display;
        private nint _window;
        private bool _preferImmediatePresent;

        public X11WindowFrameSession(MewVGX11WindowResources resources, IMewVGOffscreenSurfaceProvider offscreenProvider)
        {
            _resources = resources;
            _offscreenProvider = offscreenProvider;
        }

        public MewVGGL Vg => _resources.Vg;

        public MewVGTextCache TextCache => _resources.TextCache;

        public nint OpenGLShareGroup => _resources.OpenGLShareGroup;

        public OpenGLPixelRenderSurface? PixelSurfaceTarget => null;

        public bool ClearsTargetBeforeFirstWrite => true;

        public void SetTarget(nint display, nint window, bool preferImmediatePresent)
        {
            _display = display;
            _window = window;
            _preferImmediatePresent = preferImmediatePresent;
        }

        public void BeginFrame()
        {
            _resources.MakeCurrent(_display);
            _offscreenProvider.EnterSession();
        }

        public void BindFrameTarget()
            => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);

        public void EndFrame()
        {
            _offscreenProvider.ReleasePendingImagesForVg(_resources.Vg);
            // Outermost session - drain FBO disposals only after every NVG (window + nested
            // offscreen) has flushed; nested sessions wrap scratch FBO textures via
            // CreateImageFromHandle and the outer's queued draws still reference them.
            if (_offscreenProvider.ExitSession())
            {
                _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
            }
            TextCache.ReleasePendingDeletes();
            NvgStrokeHelper.ReleasePendingGradientLutDeletes(_resources.Vg);
            // Resize frames skip vsync: the WM already paces the resize via the frame-sync
            // protocol, so blocking on vblank here only delays the next resize step.
            _resources.SetSwapInterval(_preferImmediatePresent ? 0 : GetSwapInterval());
            _resources.SwapBuffers(_display, _window);
            _resources.ReleaseCurrent();
        }

        public void AbortFrame()
        {
            _offscreenProvider.ExitSession();
            _resources.ReleaseCurrent();
        }

        public void DisposeContext(MewVGX11GraphicsContext context)
            => _resources.InvalidateCachedContext(context);
    }
}
