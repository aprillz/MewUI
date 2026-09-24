using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Retained;

namespace Aprillz.MewUI;

public partial class Window
{
    private RenderScene? _renderScene;
    private SceneCapture? _sceneCapture;
    private RenderDirtyRegistry? _renderDirty;
    private bool _retainedSceneReady;
    // Target whose BeginFrame was asked to keep the previous frame; only then may this one repaint part of it.
    private object? _preservingTarget;
    private IRenderSurface? _retainedFrameSurface;

    // The context that draws into the frame surface, kept for as long as that surface is.
    private IGraphicsContext? _retainedFrameContext;

    /// <summary>
    /// Draws straight into the window target instead of going through the frame surface, so a test can
    /// compare what the two ways of presenting the same frame put on screen.
    /// </summary>
    internal static bool PresentWithoutFrameSurface { get; set; }

    /// <summary>Counters of what the scene recorded and replayed, for diagnostics and tests.</summary>
    internal RenderSceneStatistics? RetainedStatistics => _renderScene?.Statistics;

    /// <summary>The time a pass is taken at, in <see cref="System.Diagnostics.Stopwatch"/> ticks. Tests replace it to space passes out.</summary>
    internal Func<long> RetainedClock { get; set; } = System.Diagnostics.Stopwatch.GetTimestamp;

    /// <summary>The invalidations queued for the surface this window draws, by what they changed.</summary>
    internal RenderDirtyRegistry RenderDirtyQueue => _renderDirty ??= new RenderDirtyRegistry();

    private void QueueRenderDirty(in RenderDirtyRequest request)
    {
        // A compatibility subtree is recorded as one drawing, so whatever changed inside it is a change
        // of that drawing.
        if (request.CompatibilityOwner is UIElement owner && !ReferenceEquals(owner, request.Origin))
        {
            RenderDirtyQueue.Add(owner, RenderDirtyKind.Content);
        }
        else
        {
            RenderDirtyQueue.Add(request.Origin, request.Kind);
        }
    }

    // True while a frame is drawn straight from the visuals, leaving the scene as it is.
    private bool _drawingReferenceFrame;

    /// <summary>
    /// Draws a frame straight from the visuals without reading or updating the scene, which is what a
    /// test compares a scene-driven frame against.
    /// </summary>
    internal void RenderReferenceFrameToSurface(IRenderSurface surface)
    {
        _drawingReferenceFrame = true;
        try
        {
            RenderFrameToSurface(surface);
        }
        finally
        {
            _drawingReferenceFrame = false;
        }
    }

    /// <summary>Area the last frame repainted, or null when that frame was drawn whole.</summary>
    internal Rect? LastRetainedDirtyRect { get; private set; }

    /// <summary>How many frames this window drew whole, repainted part of, and found nothing to repaint in.</summary>
    internal readonly record struct RetainedFrameCounts(int Whole, int Partial, int Untouched);

    private int _rejectedSceneUpdates;
    private int _consecutiveRejectedUpdates;
    private const int REJECTIONS_BEFORE_DRAWING_DIRECTLY = 2;

    /// <summary>How many scene updates were rejected and left the previous scene in place.</summary>
    internal int RejectedSceneUpdates => _rejectedSceneUpdates;

    internal const double WHOLE_FRAME_DIRTY_RATIO = 0.85;

    /// <summary>Why the last frame that painted the whole surface did so.</summary>
    internal string? LastWholeFrameReason { get; private set; }

    // The separate areas the current frame repaints; their union is LastRetainedDirtyRect.
    private readonly List<Rect> _frameDirtyRects = [];

    /// <summary>The separate areas the last partial frame repainted.</summary>
    internal IReadOnlyList<Rect> LastRetainedDirtyRects => _frameDirtyRects;

    private int _wholeFrames;
    private int _partialFrames;
    private int _untouchedFrames;

    internal RetainedFrameCounts RetainedFrames => new(_wholeFrames, _partialFrames, _untouchedFrames);

    /// <summary>The most any single partial frame repainted since the counts were reset, in layout units squared.</summary>
    internal double LargestPartialRepaintArea { get; private set; }

    /// <summary>The bounds of that frame's repaint, for telling where it reached.</summary>
    internal Rect LargestPartialRepaint { get; private set; }

    internal void ResetRetainedFrameCounts()
    {
        _wholeFrames = 0;
        _partialFrames = 0;
        _untouchedFrames = 0;
        LargestPartialRepaintArea = 0;
        LargestPartialRepaint = default;
    }

    // Each frame that paints takes the next colour, so a repaint of the same area shows as a change of
    // colour and two frames in a row never look like one.
    private static readonly Color[] _dirtyMarkColors =
    [
        Color.FromArgb(255, 255, 64, 64),
        Color.FromArgb(255, 64, 200, 64),
        Color.FromArgb(255, 64, 128, 255),
        Color.FromArgb(255, 255, 200, 0),
        Color.FromArgb(255, 220, 64, 220),
        Color.FromArgb(255, 0, 200, 200),
    ];

    // The areas the newest painting frame repainted, in that frame's colour.
    private readonly List<Rect> _dirtyMarks = [];
    private bool _dirtyRegionOverlayEnabled;
    private int _dirtyMarkColorIndex;

    // What the scene had counted when the newest painting frame began, and what that frame added.
    private int _visitedAtFrameStart;
    private int _recordedAtFrameStart;
    private int _replayedAtFrameStart;
    private int _frameVisited;
    private int _frameRecorded;
    private int _frameReplayed;

    /// <summary>
    /// Shows what the newest painting frame did: the areas it painted again are tinted, in a colour that
    /// changes from frame to frame, and one line says how many visuals it looked into, recorded again and
    /// replayed. The overlay only reads what a frame did. It asks for no frame, and it draws over the
    /// frame on its way to the screen, never into the surface that keeps the frame.
    /// </summary>
    internal bool DirtyRegionOverlayEnabled => _dirtyRegionOverlayEnabled || (_hostedPortalRoot != null && Owner?.DirtyRegionOverlayEnabled == true);

    private void RegisterRetainedDiagnostics()
    {
        // The overlay draws with ordinary commands, so a release build can show it too.
        InputMap.Map(
            new KeyGesture(Key.D, ModifierKeys.Primary | ModifierKeys.Shift),
            ToggleDirtyRegionOverlay);
    }

    internal void ToggleDirtyRegionOverlay()
    {
        _dirtyRegionOverlayEnabled = !_dirtyRegionOverlayEnabled;
        _dirtyMarks.Clear();
        _presentedFrameLost = true;

        // The frame moves between the target's own buffer and a surface of its own, and neither holds
        // what was drawn into the other since.
        _preservingTarget = null;
        RequestRender();
    }

    // Whether the overlay was on for the last frame. A popup window follows its owner's toggle, so it
    // finds out at its next frame, not when the key is pressed.
    private bool _overlayWasEnabled;

    /// <summary>
    /// A frame handed a surface that keeps its contents (a window presented from a bitmap of its own)
    /// would keep the overlay in it too. While the overlay is on, such a frame is kept in a surface of
    /// this window's own and copied whole onto the one it was handed, with the overlay on top.
    /// </summary>
    private bool TryRenderFrameWithOverlay(IRenderSurface surface, Size clientSize)
    {
        bool enabled = DirtyRegionOverlayEnabled && !_drawingReferenceFrame;
        if (enabled != _overlayWasEnabled)
        {
            // The frame moves between the handed surface and this window's own, and neither holds what
            // was drawn into the other since.
            _overlayWasEnabled = enabled;
            _preservingTarget = null;
        }

        if (!enabled ||
            surface is not IPersistentFrameSurface handed ||
            GraphicsFactory is not IPersistentFrameGraphicsFactory { IsPersistentFrameRenderingVerified: true } persistentFactory)
        {
            return false;
        }

        var frameSurface = AcquireRetainedFrameSurface(surface);
        if (frameSurface == null)
        {
            return false;
        }

        using (persistentFactory.AcquirePersistentFrameRenderScope())
        {
            RenderFrameCore(frameSurface, clientSize);
        }

        var view = GraphicsFactory.CreateImageView(frameSurface);
        try
        {
            handed.PreserveContentsOnBeginFrame = false;
            using var context = GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface);
            try
            {
                context.Clear(AllowsTransparency ? Color.Transparent : EffectiveOpaqueBackground);
                context.DrawImage(view, new Rect(0, 0, clientSize.Width, clientSize.Height));
                DrawDirtyMarks(context);
            }
            finally
            {
                context.EndFrame();
            }
        }
        finally
        {
            view.Dispose();
        }

        return true;
    }

    /// <summary>Notes what the frame being built repaints, to be shown when it reaches the screen.</summary>
    private void NoteDirtyMarks(Rect? dirtyRect, Size clientSize)
    {
        if (_renderScene == null || !DirtyRegionOverlayEnabled)
        {
            _dirtyMarks.Clear();
            return;
        }

        // Only what the newest painting frame did is shown. Marks of earlier frames pile up into a
        // picture nobody can read; the colour changing from frame to frame already tells them apart.
        _dirtyMarkColorIndex = (_dirtyMarkColorIndex + 1) % _dirtyMarkColors.Length;
        _dirtyMarks.Clear();
        if (dirtyRect == null)
        {
            _dirtyMarks.Add(new Rect(0, 0, clientSize.Width, clientSize.Height));
        }
        else
        {
            _dirtyMarks.AddRange(_frameDirtyRects);
        }
    }

    /// <summary>Takes the scene's counters before an update, so the overlay can say what one frame added.</summary>
    private void NoteSceneCountsBeforeUpdate()
    {
        var statistics = _renderScene?.Statistics;
        _visitedAtFrameStart = statistics?.CapturedNodeCount ?? 0;
        _recordedAtFrameStart = statistics?.ContentRecordCount ?? 0;
        _replayedAtFrameStart = statistics?.ContentReplayCount ?? 0;
    }

    /// <summary>Draws the tint and the counts over a frame on its way to the screen.</summary>
    private void DrawDirtyMarks(IGraphicsContext context)
    {
        if (!DirtyRegionOverlayEnabled)
        {
            return;
        }

        var color = WithAlpha(_dirtyMarkColors[_dirtyMarkColorIndex], DIRTY_TINT_ALPHA);
        for (int index = 0; index < _dirtyMarks.Count; index++)
        {
            context.FillRectangle(LayoutRounding.SnapViewportRectToPixels(_dirtyMarks[index], DpiScale), color);
        }

        // A frame that painted nothing leaves the counts of the last one that did, like the tint.
        var statistics = _renderScene?.Statistics;
        if (statistics != null && _dirtyMarks.Count > 0)
        {
            _frameVisited = Math.Max(0, statistics.CapturedNodeCount - _visitedAtFrameStart);
            _frameRecorded = Math.Max(0, statistics.ContentRecordCount - _recordedAtFrameStart);
            _frameReplayed = Math.Max(0, statistics.ContentReplayCount - _replayedAtFrameStart);
        }

        // The counts are text, and a window that draws text carries the text engine whether or not the
        // application shows any. Only a build with the developer tools pays for that; others get the tint.
        if (!DevToolsGate.IsSupported)
        {
            return;
        }

        Span<char> buffer = stackalloc char[64];
        var text = new StackTextFormatter(buffer);
        text.Append("V:");
        text.Append(_frameVisited);
        text.Append("  R:");
        text.Append(_frameRecorded);
        text.Append("  P:");
        text.Append(_frameReplayed);

        const double PAD = 4;
        var size = MeasureEngineText(text.WrittenSpan, transient: true);
        var panel = LayoutRounding.SnapBoundsRectToPixels(
            new Rect(PAD, PAD, size.Width + PAD * 2, size.Height + PAD * 2), DpiScale);
        context.FillRectangle(panel, Color.FromArgb(205, 18, 18, 18));
        DrawEngineText(context, text.WrittenSpan, panel.Deflate(new Thickness(PAD)), Color.White, transient: true);
    }

    private const double DIRTY_TINT_ALPHA = 96;

    private static Color WithAlpha(Color color, double alpha)
        => Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);

    /// <summary>
    /// Brings the retained scene up to date without drawing and returns the area this frame has to
    /// repaint, or null when the frame must be drawn whole. A target that cannot keep the previous
    /// frame, or a context that cannot erase a rectangle, always returns null.
    /// </summary>
    private Rect? UpdateRetainedScene(IGraphicsContext context, IRenderTarget target, UIElement root, bool isPortal)
    {
        _retainedSceneReady = false;
        LastRetainedDirtyRect = null;

        if (_drawingReferenceFrame)
        {
            return null;
        }

        _renderScene ??= new RenderScene();
        _renderScene.Clock = RetainedClock;
        _sceneCapture ??= new SceneCapture();
        _renderDirty ??= new RenderDirtyRegistry();

        // Recorded resources belong to the device they were taken from.
        _renderScene.SetDeviceGeneration(DeviceGeneration);

        // The dirty region of the frame being drawn now, not of every frame since the scene was built.
        _renderScene.ResetDirtyRegion();

        var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };

        try
        {
            _sceneCapture.Capture(_renderScene, root, isPortal ? [] : CollectLayerRoots(), recorder, RenderDirtyQueue);
        }
        catch (InvalidOperationException rejection)
        {
            // A rejected update changed nothing: the scene still is the last one that landed and the
            // queue still holds what this update was meant to serve, so the next frame tries again.
            // Until then the frame shows that last scene. An update rejected again is not going to land
            // by itself, and showing the old scene any longer would freeze the window on it, so from
            // then on, as in a window that has no scene yet, the frame draws its visuals directly.
            _rejectedSceneUpdates++;
            _consecutiveRejectedUpdates++;
            LastWholeFrameReason = rejection.Message;
            _retainedSceneReady = _renderScene.Root != null && _consecutiveRejectedUpdates < REJECTIONS_BEFORE_DRAWING_DIRECTLY;
            _wholeFrames++;
            return null;
        }

        if (_consecutiveRejectedUpdates >= REJECTIONS_BEFORE_DRAWING_DIRECTLY)
        {
            // The frames drawn directly meanwhile are not what the target was keeping for the scene.
            _renderScene.MarkFullyDirty();
        }

        _consecutiveRejectedUpdates = 0;
        _retainedSceneReady = true;

        if (_renderScene.IsFullyDirty || !CanRepaintPartOfTheFrame(context, target))
        {
            LastWholeFrameReason = _renderScene.IsFullyDirty ? "the scene asked for the whole frame" : "the target does not keep its contents";
            _wholeFrames++;
            return null;
        }

        var dirtyRect = _renderScene.DirtyBounds;
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            // Nothing changed anywhere, so the frame the target still holds is already this frame.
            _untouchedFrames++;
            LastRetainedDirtyRect = default(Rect);
            return LastRetainedDirtyRect;
        }

        // Past this share of the surface a frame that repaints areas costs as much as one that repaints
        // everything, which has no erasing and no clipping to do (agent/retained-redesign/dirty-region-cost.md).
        double surfaceArea = (target.PixelWidth / Math.Max(1.0, target.DpiScale)) * (target.PixelHeight / Math.Max(1.0, target.DpiScale));
        if (_renderScene.DirtyRegion.TotalArea >= surfaceArea * WHOLE_FRAME_DIRTY_RATIO)
        {
            LastWholeFrameReason = $"the dirty region {_renderScene.DirtyBounds} covers most of the surface";
            _wholeFrames++;
            return null;
        }

        if (_renderScene.DirtyRegion.TotalArea > LargestPartialRepaintArea)
        {
            LargestPartialRepaintArea = _renderScene.DirtyRegion.TotalArea;
            LargestPartialRepaint = dirtyRect;
        }

        _partialFrames++;
        _frameDirtyRects.Clear();
        var areas = _renderScene.DirtyRegion.Areas;
        for (int index = 0; index < areas.Count; index++)
        {
            _frameDirtyRects.Add(LayoutRounding.SnapViewportRectToPixels(areas[index], DpiScale));
        }

        LastRetainedDirtyRect = LayoutRounding.SnapViewportRectToPixels(dirtyRect, DpiScale);
        return LastRetainedDirtyRect;
    }

    private readonly List<UIElement> _layerRoots = [];

    /// <summary>
    /// Lists what this surface draws over its body, in drawing order: adorners, the popups shown inside
    /// the surface, overlays, and the performance monitor last. They go into the scene as roots, so they
    /// share its update and its dirty region with the body.
    /// </summary>
    private List<UIElement> CollectLayerRoots()
    {
        _layerRoots.Clear();
        UIElement? performanceAdorner = DevToolsGate.IsSupported ? _devTools?.PerformanceAdorner : null;

        for (int index = 0; index < _adorners.Count; index++)
        {
            var adorner = _adorners[index].Element;
            if (!ReferenceEquals(adorner, performanceAdorner))
            {
                _layerRoots.Add(adorner);
            }
        }

        _popupManager.CollectInSurfaceRoots(_layerRoots);

        for (int index = 0; index < OverlayLayer.Count; index++)
        {
            _layerRoots.Add(OverlayLayer.ElementAt(index));
        }

        if (performanceAdorner != null)
        {
            _layerRoots.Add(performanceAdorner);
        }

        SplitRootsOverTheFrame();
        return _layerRoots;
    }

    /// <summary>Whether this frame found nothing to repaint at all.</summary>
    internal static bool RepaintsNothing(Rect? dirtyRect) => dirtyRect is Rect area && (area.Width <= 0 || area.Height <= 0);

    /// <summary>Draws the body from the scene, restricted to the dirty region when the frame has one.</summary>
    private bool TryRenderRetainedBody(IGraphicsContext context, Rect? dirtyRect)
    {
        if (!_retainedSceneReady || _renderScene == null)
        {
            return false;
        }

        _renderScene.GroupFactory = GraphicsFactory;
        FrameRenderer.Replay(_renderScene, context, dirtyRect);
        return true;
    }

    private void EraseRetainedDirtyRect(IGraphicsContext context, Rect dirtyRect, Color clearColor)
    {
        if (AllowsTransparency)
        {
            ((ITransparentDirtyRectContext)context).ClearRectangleToTransparent(dirtyRect);
        }
        else
        {
            ((IOpaqueDirtyRectContext)context).ClearRectangle(dirtyRect, clearColor);
        }
    }

    /// <summary>
    /// Whether the previous frame survives on this target and the dirty box can be erased on it,
    /// which is what a partial repaint needs.
    /// </summary>
    private bool CanRepaintPartOfTheFrame(IGraphicsContext context, IRenderTarget target)
    {
        var persistent = target as IPersistentFrameSurface;
        if ((persistent == null && !DrawsInPlace(target)) ||
            GraphicsFactory is not IPersistentFrameGraphicsFactory { IsPersistentFrameRenderingVerified: true })
        {
            return false;
        }

        bool canErase = AllowsTransparency
            ? context is ITransparentDirtyRectContext
            : context is IOpaqueDirtyRectContext;
        if (!canErase)
        {
            return false;
        }

        // The flag takes effect at the next BeginFrame, so only a target that already carried it into
        // this frame still holds the pixels a partial repaint draws onto.
        bool preservedIntoThisFrame = ReferenceEquals(_preservingTarget, target);
        if (persistent != null)
        {
            persistent.PreserveContentsOnBeginFrame = true;
        }

        _preservingTarget = target;
        return preservedIntoThisFrame;
    }

    /// <summary>
    /// Whether this window's frames are kept in the buffer its own target draws into. Transparent
    /// windows blend onto a target they clear first, and the dirty region overlay draws on the target, so both
    /// keep their frame in a surface of their own instead.
    /// </summary>
    private bool DrawsInPlace(IRenderTarget target)
        => target is WindowRenderTarget &&
           !AllowsTransparency &&
           !DirtyRegionOverlayEnabled &&
           !PresentWithoutFrameSurface &&
           PlatformReportsLostFrames &&
           GraphicsFactory is IPersistentFrameGraphicsFactory { IsPersistentFrameRenderingVerified: true, DrawsWindowFramesInPlace: true };

    /// <summary>Tells a target drawn in place how much of its buffer this frame has to put on screen.</summary>
    private void LimitInPlacePresent(IGraphicsContext context, IRenderTarget target, Rect? dirtyRect)
    {
        if (!DrawsInPlace(target) || context is not IPartialPresentContext partial)
        {
            return;
        }

        _presents++;
        if (_presentedFrameLost || dirtyRect == null)
        {
            // The screen lost the frame, or the whole of it was painted: the default copies everything.
            _presentedFrameLost = false;
            _presentedArea = ClientSize.Width * ClientSize.Height;
        }
        else if (_frameRepaintedNothing)
        {
            // What the last presented frame copied stays on record; this frame copies nothing.
            _presents--;
            _skippedPresents++;
            partial.LimitPresentTo([]);
        }
        else
        {
            partial.LimitPresentTo(_frameDirtyRects);
            _presentedArea = 0;
            for (int index = 0; index < _frameDirtyRects.Count; index++)
            {
                _presentedArea += _frameDirtyRects[index].Width * _frameDirtyRects[index].Height;
            }
        }
    }

    // True when the last frame built had no area to paint.
    private bool _frameRepaintedNothing;

    // True until a frame has been put on screen, and again whenever what the window shows can no longer
    // be taken for the last frame presented.
    private bool _presentedFrameLost = true;

    private int _presents;
    private int _skippedPresents;
    private double _presentedArea;

    /// <summary>How much of the target the last presented frame copied onto it, in layout units squared.</summary>
    internal double LastPresentedArea => _presentedArea;

    /// <summary>
    /// True when the platform calls <see cref="NotePresentedFrameLost"/> whenever the window stops
    /// showing the last frame presented to it. Only then can a frame that changed nothing skip being
    /// presented.
    /// </summary>
    internal bool PlatformReportsLostFrames { get; set; }

    /// <summary>How many frames were put on screen through the frame surface, and how many were not because nothing changed.</summary>
    internal (int Presented, int Skipped) PresentCounts => (_presents, _skippedPresents);

    /// <summary>Tells the window that what it shows is no longer the last frame presented, so the next frame is presented whatever it changed.</summary>
    internal void NotePresentedFrameLost() => _presentedFrameLost = true;

    /// <summary>
    /// Draws the frame into a surface that keeps its contents and copies that surface to
    /// <paramref name="target"/>, which is what lets a window repaint part of its frame even though
    /// its own target does not survive a frame. Returns false when the frame must be drawn straight
    /// into the target instead.
    /// </summary>
    internal bool TryRenderFrameThroughRetainedSurface(IRenderTarget target, Size clientSize)
    {
        // A target that already keeps its contents is repainted in place, and a backend that has not
        // been checked against a surface which keeps them draws straight into the target.
        if (PresentWithoutFrameSurface ||
            target is IPersistentFrameSurface ||
            GraphicsFactory is not IPersistentFrameGraphicsFactory { IsPersistentFrameRenderingVerified: true } persistentFactory)
        {
            return false;
        }

        if (DrawsInPlace(target))
        {
            // The target keeps its own frame, so a second surface of the same size would only be a copy.
            // Releasing also forgets which target was kept, so it is done once, not every frame.
            if (_retainedFrameSurface != null)
            {
                ReleaseRetainedFrameSurface();
            }

            return false;
        }

        // The window's own context comes first even though the frame is drawn elsewhere: creating it
        // is what gives the backend this window's resources, and a backend whose drawing is bound to
        // a per-thread context has none to offer until then.
        _renderContext ??= GraphicsFactory.CreateContext(target);

        var frameSurface = AcquireRetainedFrameSurface(target);
        if (frameSurface == null)
        {
            return false;
        }

        // A backend that binds its drawing to a per-thread context needs that context put on this
        // thread before the frame surface is drawn into.
        _mayDrawRootsOverTheFrame = true;
        _frameDrewEveryRoot = false;
        try
        {
            using (persistentFactory.AcquirePersistentFrameRenderScope())
            {
                RenderFrameCore(frameSurface, clientSize);
            }
        }
        finally
        {
            _mayDrawRootsOverTheFrame = false;
        }

        // A frame drawn without the scene drew every root into the surface itself.
        if (_frameDrewEveryRoot)
        {
            _rootsOverTheFrame.Clear();
        }

        bool rootsOverTheFrameChanged = _rootsOverTheFrame.Count != _rootsOverTheLastFrame;
        _rootsOverTheLastFrame = _rootsOverTheFrame.Count;

        // Nothing was painted, so the window already shows this frame: putting it there again would
        // copy the whole surface for no change on screen. A root drawn over the frame is on no surface,
        // so the screen has to be given it every frame it is there.
        if (_frameRepaintedNothing && PlatformReportsLostFrames && !_presentedFrameLost &&
            _rootsOverTheFrame.Count == 0 && !rootsOverTheFrameChanged)
        {
            _skippedPresents++;
            return true;
        }

        // A target that still holds the last frame needs only what changed copied onto it. The marks
        // of the overlay are drawn on the target itself, and a transparent window blends onto a target
        // it clears first, so both copy the whole frame.
        bool copiesChangedAreasOnly =
            !_presentedFrameLost &&
            PlatformReportsLostFrames &&
            persistentFactory.WindowTargetKeepsPresentedFrame &&
            !AllowsTransparency &&
            !DirtyRegionOverlayEnabled &&
            !_frameRepaintedNothing &&
            _rootsOverTheFrame.Count == 0 &&
            !rootsOverTheFrameChanged &&
            LastRetainedDirtyRect is Rect &&
            _frameDirtyRects.Count > 0;

        _presentedFrameLost = false;
        _presents++;

        var view = GraphicsFactory.CreateImageView(frameSurface);
        try
        {
            // The window's own context presents the frame. A second context made for the same target
            // every frame costs a context each time, and where contexts of one window share its native
            // resources, disposing that second one takes them from under the window's own.
            var context = _renderContext;
            context.BeginFrame(target);
            try
            {
                if (AllowsTransparency)
                {
                    // The frame is blended onto the target, so whatever the target still holds would
                    // show through every translucent pixel and build up frame after frame.
                    context.Clear(Color.Transparent);
                }

                var whole = new Rect(0, 0, clientSize.Width, clientSize.Height);
                if (copiesChangedAreasOnly)
                {
                    // A context that ends its frame by copying a buffer of its own to the window copies
                    // only these areas too.
                    (context as IPartialPresentContext)?.LimitPresentTo(_frameDirtyRects);
                    _presentedArea = 0;
                    for (int index = 0; index < _frameDirtyRects.Count; index++)
                    {
                        var area = _frameDirtyRects[index];
                        context.Save();
                        context.SetClip(area);
                        context.DrawImage(view, whole);
                        context.Restore();
                        _presentedArea += area.Width * area.Height;
                    }
                }
                else
                {
                    context.DrawImage(view, whole);
                    _presentedArea = whole.Width * whole.Height;
                }

                DrawRootsOverTheFrame(context, clientSize);
                DrawDirtyMarks(context);
            }
            finally
            {
                // Closed even when drawing throws, or every frame after this one begins inside it.
                context.EndFrame();
            }
        }
        finally
        {
            view.Dispose();
        }


        return true;
    }

    // Layer roots left out of the kept frame and drawn over it on its way to the screen, in drawing order.
    private readonly List<UIElement> _rootsOverTheFrame = [];

    // Frame in which each of those roots last changed.
    private readonly Dictionary<UIElement, int> _rootOverTheFrameLastChange = new(ReferenceEqualityComparer.Instance);
    private bool _mayDrawRootsOverTheFrame;
    private bool _frameDrewEveryRoot;
    private int _rootsOverTheLastFrame;
    private int _keptFrameNumber;

    /// <summary>
    /// A layer root that draws something else every frame, over most of the window, would have the kept
    /// frame replay everything under it every frame. The roots at the top of the order that change every
    /// frame are therefore left out of the kept frame and drawn straight onto the screen over it. Only
    /// the top of the order qualifies, so nothing that belongs above such a root ends up beneath it.
    /// </summary>
    private void SplitRootsOverTheFrame()
    {
        _rootsOverTheFrame.Clear();
        if (!_mayDrawRootsOverTheFrame)
        {
            _rootOverTheFrameLastChange.Clear();
            return;
        }

        _keptFrameNumber++;
        int first = _layerRoots.Count;
        while (first > 0 && ChangesEveryFrame(_layerRoots[first - 1]))
        {
            first--;
        }

        for (int index = first; index < _layerRoots.Count; index++)
        {
            _rootsOverTheFrame.Add(_layerRoots[index]);
        }

        _layerRoots.RemoveRange(first, _layerRoots.Count - first);

        if (_rootOverTheFrameLastChange.Count > _rootsOverTheFrame.Count)
        {
            foreach (var known in _rootOverTheFrameLastChange.Keys.ToArray())
            {
                if (!_rootsOverTheFrame.Contains(known))
                {
                    _rootOverTheFrameLastChange.Remove(known);
                }
            }
        }
    }

    private bool ChangesEveryFrame(UIElement layerRoot)
    {
        if (_rootOverTheFrameLastChange.TryGetValue(layerRoot, out int lastChange))
        {
            if (IsQueuedUnder(layerRoot))
            {
                lastChange = _keptFrameNumber;
                _rootOverTheFrameLastChange[layerRoot] = lastChange;
            }

            // A frame may go by without it; after that it has settled and goes back into the kept frame.
            return _keptFrameNumber - lastChange <= 1;
        }

        var node = _renderScene?.FindNode(layerRoot);
        if (node != null && node.ChangesEveryPass(_renderScene!.PassId + 1, _renderScene.Clock()))
        {
            _rootOverTheFrameLastChange[layerRoot] = _keptFrameNumber;
            return true;
        }

        return false;
    }

    private bool IsQueuedUnder(UIElement layerRoot)
    {
        foreach (var queued in RenderDirtyQueue.Queued.Keys)
        {
            for (Element? current = queued; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, layerRoot))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void DrawRootsOverTheFrame(IGraphicsContext context, Size clientSize)
    {
        if (_rootsOverTheFrame.Count == 0)
        {
            return;
        }

        var previousCullViewport = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = new Rect(0, 0, clientSize.Width, clientSize.Height);
        context.Save();
        try
        {
            context.SetClip(LayoutRounding.SnapViewportRectToPixels(new Rect(0, 0, clientSize.Width, clientSize.Height), DpiScale));
            for (int index = 0; index < _rootsOverTheFrame.Count; index++)
            {
                _rootsOverTheFrame[index].Render(context);
            }
        }
        finally
        {
            context.Restore();
            UIElement.RenderCullViewport = previousCullViewport;
        }
    }

    /// <summary>Returns the frame surface matching the target, creating it when it does not match.</summary>
    private IRenderSurface? AcquireRetainedFrameSurface(IRenderTarget target)
    {
        if (_retainedFrameSurface != null &&
            _retainedFrameSurface.PixelWidth == target.PixelWidth &&
            _retainedFrameSurface.PixelHeight == target.PixelHeight &&
            Math.Abs(_retainedFrameSurface.DpiScale - target.DpiScale) < 0.0001)
        {
            return _retainedFrameSurface;
        }

        ReleaseRetainedFrameSurface();

        if (target.PixelWidth <= 0 || target.PixelHeight <= 0)
        {
            return null;
        }

        var surface = GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.Offscreen(
            target.PixelWidth,
            target.PixelHeight,
            target.DpiScale,
            hasAlpha: AllowsTransparency));

        if (surface is not IPersistentFrameSurface)
        {
            surface.Dispose();
            return null;
        }

        _retainedFrameSurface = surface;
        return surface;
    }

    private void ReleaseRetainedFrameSurface()
    {
        // The context draws into the surface, so it goes first.
        _retainedFrameContext?.Dispose();
        _retainedFrameContext = null;
        _retainedFrameSurface?.Dispose();
        _retainedFrameSurface = null;
        _preservingTarget = null;
    }

    private void ReleaseRetainedScene()
    {
        _renderScene?.Dispose();
        _renderScene = null;
        _renderDirty?.Clear();
        _retainedSceneReady = false;
        ReleaseRetainedFrameSurface();
    }
}
