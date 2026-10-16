using Aprillz.MewUI.Native;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGFramebufferGraphicsContext : IPartialPresentContext
{
    private bool _restoringTarget;

    private MewVGFramebufferGraphicsContext(
        IFrameSession frameSession,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
    {
        _frameSession = frameSession;
        _vg = frameSession.Vg;
        _textCache = frameSession.TextCache;
        _gpuInteropInvalidated = gpuInteropInvalidated;
    }

    internal static MewVGFramebufferGraphicsContext CreateForWindow(
        MewVGFramebufferWindowResources resources,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => new(new FramebufferWindowFrameSession(resources, offscreenProvider), gpuInteropInvalidated);

    private static nint GetCurrentGLContext() => LibEgl.eglGetCurrentContext();

    private static nint GetCurrentNativeContext() => LibEgl.eglGetCurrentContext();

    void IPartialPresentContext.LimitPresentTo(IReadOnlyList<Rect> areas)
    {
        if (_frameSession is FramebufferWindowFrameSession windowSession)
        {
            windowSession.LimitPresentTo(areas, DpiScale, _viewportWidthPx, _viewportHeightPx);
        }
    }

    partial void BeforeSurfaceCopy(IRenderSurface source, Rect area, ref bool copied)
    {
        if (_restoringTarget || _frameSession is not FramebufferWindowFrameSession windowSession || !windowSession.TracksBufferAge)
        {
            return;
        }

        // The back buffer holds the frame of a few presents ago. The first copy of the frame brings it up to date
        // in one pass: the areas presented since then plus every area this frame changed, as one bounding box,
        // because each separate copy costs the tiled GPU a load and store of the whole target.
        if (!windowSession.TryTakeFrameCopy(new Rect(0, 0, _viewportWidthDip, _viewportHeightDip), area, out var bounds))
        {
            copied = true;
            return;
        }

        _restoringTarget = true;
        try
        {
            ((ISurfaceCopyContext)this).TryCopySurface(source, bounds);
        }
        finally
        {
            _restoringTarget = false;
        }

        copied = true;
    }

    private sealed class FramebufferWindowFrameSession : IFrameSession
    {
        // Older back buffers than this are restored whole; GBM surfaces cycle through four buffers.
        private const int HISTORY_FRAMES = 4;

        private readonly MewVGFramebufferWindowResources _resources;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private readonly List<PixelArea> _presentAreas = [];
        private readonly List<Rect> _presentAreasDip = [];
        private bool _presentLimited;

        // Areas each recent present wrote to its buffer, newest first; null stands for the whole target.
        private readonly List<Rect[]?> _presentHistory = [];
        private int _bufferAge;
        private bool _restorePending;
        private bool _copiedThisFrame;

        public FramebufferWindowFrameSession(MewVGFramebufferWindowResources resources, IMewVGOffscreenSurfaceProvider offscreenProvider)
        {
            _resources = resources;
            _offscreenProvider = offscreenProvider;
        }

        public MewVGGL Vg => _resources.Vg;

        public MewVGTextCache TextCache => _resources.TextCache;

        public nint OpenGLShareGroup => _resources.OpenGLShareGroup;

        public OpenGL.OpenGLPixelRenderSurface? PixelSurfaceTarget => null;

        public bool TracksBufferAge => _resources.TracksBufferAge;

        // A buffer whose age is unknown holds an older frame and has to be cleared; a kept or aged buffer must not be.
        public bool ClearsTargetBeforeFirstWrite => !_resources.DrawsInPlace && !_resources.TracksBufferAge;

        public void LimitPresentTo(IReadOnlyList<Rect> areas, double dpiScale, int widthPx, int heightPx)
        {
            _presentAreas.Clear();
            _presentAreasDip.Clear();
            _presentLimited = true;
            for (int index = 0; index < areas.Count; index++)
            {
                var area = areas[index];
                _presentAreasDip.Add(area);
                int left = Math.Clamp((int)Math.Floor(area.X * dpiScale), 0, widthPx);
                int top = Math.Clamp((int)Math.Floor(area.Y * dpiScale), 0, heightPx);
                int right = Math.Clamp((int)Math.Ceiling(area.Right * dpiScale), left, widthPx);
                int bottom = Math.Clamp((int)Math.Ceiling(area.Bottom * dpiScale), top, heightPx);
                _presentAreas.Add(new PixelArea(left, top, right - left, bottom - top));
            }
        }

        /// <summary>
        /// For the first copy of a frame, the box to copy instead: what the back buffer is missing relative to the
        /// last present (nothing for a buffer one present old, everything for an unknown age or one older than the
        /// history) united with what this frame changed. False for the later copies, which that box already holds.
        /// </summary>
        public bool TryTakeFrameCopy(Rect whole, Rect requested, out Rect bounds)
        {
            _copiedThisFrame = true;
            bounds = default;
            if (!_restorePending)
            {
                return false;
            }

            _restorePending = false;
            int missing = _bufferAge - 1;
            if (_bufferAge <= 0 || missing > _presentHistory.Count || !_presentLimited)
            {
                bounds = whole;
                return true;
            }

            bounds = requested;
            for (int index = 0; index < _presentAreasDip.Count; index++)
            {
                bounds = bounds.Union(_presentAreasDip[index]);
            }

            for (int index = 0; index < missing; index++)
            {
                var areas = _presentHistory[index];
                if (areas == null)
                {
                    bounds = whole;
                    return true;
                }

                for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
                {
                    bounds = bounds.Union(areas[areaIndex]);
                }
            }

            return true;
        }

        public void BeginFrame()
        {
            _resources.MakeCurrent();
            _offscreenProvider.EnterSession();
            _copiedThisFrame = false;
            _restorePending = _resources.TracksBufferAge;
            _bufferAge = _restorePending ? _resources.QueryBufferAge() : 0;
        }

        public void BindFrameTarget() => _resources.BindFrameTarget();

        public void EndFrame()
        {
            try
            {
                _offscreenProvider.ReleasePendingImagesForVg(_resources.Vg);
                if (_offscreenProvider.ExitSession())
                {
                    _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
                }

                TextCache.ReleasePendingDeletes();
                NvgStrokeHelper.ReleasePendingGradientLutDeletes(_resources.Vg);
                if (_resources.TracksBufferAge)
                {
                    RecordPresent();
                }

                _resources.Present(_presentLimited ? _presentAreas : null);
            }
            finally
            {
                _presentAreas.Clear();
                _presentAreasDip.Clear();
                _presentLimited = false;
                _resources.ReleaseCurrent();
            }
        }

        // A frame drawn without copying the kept frame, or copying all of it, wrote the whole buffer.
        private void RecordPresent()
        {
            Rect[]? written = _copiedThisFrame && _presentLimited ? [.. _presentAreasDip] : null;
            _presentHistory.Insert(0, written);
            if (_presentHistory.Count > HISTORY_FRAMES)
            {
                _presentHistory.RemoveAt(_presentHistory.Count - 1);
            }
        }

        public void AbortFrame()
        {
            _presentAreas.Clear();
            _presentAreasDip.Clear();
            _presentLimited = false;

            // What the aborted frame left in its buffer is unknown, so the next frame restores the whole target.
            _presentHistory.Clear();
            _offscreenProvider.ExitSession();
            _resources.ReleaseCurrent();
        }

        public void DisposeContext(MewVGFramebufferGraphicsContext context)
            => _resources.InvalidateCachedContext(context);
    }
}
