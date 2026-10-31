using System.Collections.Concurrent;
using System.Numerics;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Retained;

namespace Aprillz.MewUI.Controls;

public abstract partial class UIElement
{
    private static readonly ConcurrentDictionary<Type, bool> _overlayByType = new();

    [ThreadStatic]
    private static Stack<ChildRenderer>? _childRenderers;

    [ThreadStatic]
    private static Stack<ChildPlanner>? _childPlanners;

    [ThreadStatic]
    private static Stack<List<UIElement>>? _hitTestBuffers;

    private int _renderContentVersion;
    private int _subtreeContentVersion;
    // Where this visual stood relative to its parent after the last arrange.
    private Point _originInParent;

    /// <summary>Bumped when this visual's own drawing changes, not when a descendant's does.</summary>
    internal int RenderContentVersion => _renderContentVersion;

    /// <summary>Bumped when this visual or anything under it raises an invalidation.</summary>
    internal int SubtreeContentVersion => _subtreeContentVersion;

    /// <summary>
    /// Says what changed about this visual and queues it on the surface that draws it. The kind is
    /// stated here, where the change happens; nothing above this visual is told its own drawing changed.
    /// </summary>
    internal void RaiseRenderDirty(RenderDirtyKind kind)
    {
        if ((kind & (RenderDirtyKind.Content | RenderDirtyKind.Layout)) != 0)
        {
            _renderContentVersion++;
        }

        _subtreeContentVersion++;

        // A change on a visual the last render pass culled reaches no pixels, so it must not wake the
        // surface. The versions keep it, and the visual records again when it comes back into view.
        if (ChangesOnlyWhatIsDrawn(kind) && StopsAtCulledVisual())
        {
            return;
        }

        var request = new RenderDirtyRequest(this, kind);
        if (IsCompatibilityRoot)
        {
            request.CompatibilityOwner = this;
        }

        Parent?.NotifyDescendantRenderDirty(ref request);
    }

    /// <summary>
    /// A change of what a visual draws reaches no pixels while the visual is out of view, so it can
    /// wait. A change of where it stands, how it is laid out or what it is composed of can be what
    /// brings it into view, and the scene only looks where it was told something changed.
    /// </summary>
    private static bool ChangesOnlyWhatIsDrawn(RenderDirtyKind kind)
        => (kind & (RenderDirtyKind.Placement | RenderDirtyKind.Layout | RenderDirtyKind.Composition)) == 0;

    internal override void NotifyDescendantRenderDirty(ref RenderDirtyRequest request)
    {
        _subtreeContentVersion++;
        NoteDescendantChangedUnderCache();

        if (ChangesOnlyWhatIsDrawn(request.Kind) && StopsAtCulledVisual())
        {
            return;
        }

        if (IsCompatibilityRoot)
        {
            request.CompatibilityOwner = this;
        }

        base.NotifyDescendantRenderDirty(ref request);
    }

    internal override void OnArrangeCompleted()
    {
        // What a visual draws can follow how it arranged its children, which no size comparison sees.
        RaiseRenderDirty(RenderDirtyKind.Layout);
    }

    internal override void OnBoundsChanged(Rect previousBounds)
    {
        var bounds = Bounds;
        if (previousBounds.Width != bounds.Width || previousBounds.Height != bounds.Height)
        {
            // Drawing is laid out inside the bounds, so a new size is a new drawing.
            RaiseRenderDirty(RenderDirtyKind.Content | RenderDirtyKind.Placement);
            StoreOriginInParent();
            return;
        }

        // Children of a visual that moved ride along with it. Only the visual whose place inside its
        // parent changed has moved on its own, which keeps a scroll from queueing everything it carries.
        var previousOrigin = _originInParent;
        StoreOriginInParent();
        if (previousOrigin != _originInParent || Parent is not UIElement)
        {
            RaiseRenderDirty(RenderDirtyKind.Placement);
        }
    }

    private void StoreOriginInParent()
    {
        var bounds = Bounds;
        if (Parent is UIElement parent)
        {
            _originInParent = new Point(bounds.X - parent.Bounds.X, bounds.Y - parent.Bounds.Y);
        }
        else
        {
            _originInParent = new Point(bounds.X, bounds.Y);
        }
    }

    /// <summary>True when the scene records this visual and everything under it as one drawing.</summary>
    internal bool IsCompatibilityRoot => HasBitmapCache;

    /// <summary>
    /// Renders this element's visuals that lie over its children. Runs after the children are drawn;
    /// the default draws nothing.
    /// </summary>
    protected virtual void OnRenderOverlay(IGraphicsContext context) { }

    /// <summary>
    /// Gets the clip <paramref name="child"/> is drawn under, in window coordinates, or null to draw it unclipped.
    /// Read when the child is drawn and when the scene lays out this element's composition.
    /// </summary>
    protected virtual ChildClip? GetChildClip(UIElement child) => null;

    /// <summary>
    /// True when <paramref name="child"/> and everything under it move as one whenever this element moves the
    /// child, as a scroll viewer moves its content: the scene then shifts that subtree on a whole-pixel move
    /// instead of visiting each node in it. Drawing is unaffected.
    /// </summary>
    internal virtual bool IsChildFrame(UIElement child) => false;

    /// <summary>
    /// Whether glyphs drawn under this element's child transform still land on whole pixels; false turns
    /// text pixel snapping off under the transform.
    /// </summary>
    internal virtual bool KeepsTextPixelSnapUnderChildTransform(Matrix3x2 transform) => TransformHost.KeepsTextPixelSnap(transform);

    /// <summary>
    /// Alpha every drawing call of the children is multiplied by, 1 for none. Unlike <see cref="Opacity"/> it
    /// blends each call on its own, which is what a content transition fades with.
    /// </summary>
    internal virtual double ChildAlpha => 1.0;

    /// <summary>True when the type draws something over its children, so its composition has a second content slot.</summary>
    internal bool HasOverlay
    {
        get
        {
            var type = GetType();
            if (_overlayByType.TryGetValue(type, out bool known))
            {
                return known;
            }

            // A delegate to a virtual method binds to the override that would run and says which class
            // declared it, without looking methods up by name, which trimming cannot follow.
            Action<IGraphicsContext> overlay = OnRenderOverlay;
            bool overridden = overlay.Method.DeclaringType != typeof(UIElement);
            _overlayByType[type] = overridden;
            return overridden;
        }
    }

    private bool _isHeldOutOfView;

    /// <summary>
    /// Set by a host that keeps this element attached and laid out but off the screen, as a virtualizing
    /// presenter keeps a focused container outside its viewport. The host neither draws nor hit tests it,
    /// and a change of what it draws waits, as under a culled visual, until the host places it again.
    /// </summary>
    internal bool IsHeldOutOfView
    {
        get => _isHeldOutOfView;
        set
        {
            if (_isHeldOutOfView == value)
            {
                return;
            }

            _isHeldOutOfView = value;
            if (value)
            {
                MarkSubtreeCulled();
            }

            // The host's set of drawn children changed, which is a change of its composition.
            (Parent as UIElement)?.RaiseRenderDirty(RenderDirtyKind.Composition);
        }
    }

    /// <summary>
    /// True when <paramref name="host"/> draws this visual as one of its children this frame: it is attached to
    /// the host, visible, not fully transparent, not held out of view and laid out with an area.
    /// </summary>
    internal bool IsDrawnBy(UIElement host)
    {
        if (!ReferenceEquals(Parent, host) || !IsVisible || Opacity <= 0 || _isHeldOutOfView)
        {
            return false;
        }

        var bounds = Bounds;
        return bounds.Width > 0 && bounds.Height > 0;
    }

    /// <summary>Draws one own-content slot: 0 is <see cref="OnRender"/>, 1 is <see cref="OnRenderOverlay"/>.</summary>
    internal void RenderOwnSlot(IGraphicsContext context, int slotIndex)
    {
        if (slotIndex == 0)
        {
            OnRender(context);
        }
        else
        {
            OnRenderOverlay(context);
        }
    }

    /// <summary>Draws this element's own content, its children and its overlay, in that order, without the visual-level scopes.</summary>
    internal void RenderContentAndChildren(IGraphicsContext context)
    {
        OnRender(context);
        RenderChildren(context);
        OnRenderOverlay(context);
    }

    private void RenderChildren(IGraphicsContext context)
    {
        if (this is not IVisualTreeHost host)
        {
            return;
        }

        bool transformed = TryGetChildTransform(out var transform) && !transform.IsIdentity;
        double alpha = ChildAlpha;
        bool scoped = transformed || alpha < 1;
        if (scoped)
        {
            context.Save();
            if (transformed)
            {
                context.SetTransform(transform * context.GetTransform());
                if (!KeepsTextPixelSnapUnderChildTransform(transform))
                {
                    context.TextPixelSnap = false;
                }
            }

            if (alpha < 1)
            {
                context.GlobalAlpha *= (float)alpha;
            }
        }

        var renderers = _childRenderers ??= new Stack<ChildRenderer>();
        var renderer = renderers.Count > 0 ? renderers.Pop() : new ChildRenderer();
        renderer.Begin(this, context);
        try
        {
            host.VisitChildren(renderer.Visitor);
        }
        finally
        {
            renderer.End();
            renderers.Push(renderer);
            if (scoped)
            {
                context.Restore();
            }
        }
    }

    /// <summary>
    /// Writes this element's composition as the scene lays it out: own content, the children it draws
    /// under their clips and transform, and the overlay slot when the type draws one.
    /// </summary>
    internal void WritePlan(CompositionPlanBuilder builder)
    {
        builder.Content(0);
        if (this is IVisualTreeHost host)
        {
            bool transformed = TryGetChildTransform(out var transform) && !transform.IsIdentity;
            if (transformed)
            {
                builder.PushTransform(transform, disablesTextPixelSnap: !KeepsTextPixelSnapUnderChildTransform(transform));
            }

            double alpha = ChildAlpha;
            if (alpha < 1)
            {
                builder.PushAlpha(alpha);
            }

            var planners = _childPlanners ??= new Stack<ChildPlanner>();
            var planner = planners.Count > 0 ? planners.Pop() : new ChildPlanner();
            planner.Begin(this, builder);
            try
            {
                host.VisitChildren(planner.Visitor);
            }
            finally
            {
                planner.End();
                planners.Push(planner);
            }

            if (alpha < 1)
            {
                builder.Pop();
            }

            if (transformed)
            {
                builder.Pop();
            }
        }

        if (HasOverlay)
        {
            builder.Content(1);
        }
    }

    /// <summary>Hit tests the drawn children from the topmost down, as the base hit test does.</summary>
    private UIElement? HitTestChildren(IVisualTreeHost host, Point point)
    {
        var buffers = _hitTestBuffers ??= new Stack<List<UIElement>>();
        var children = buffers.Count > 0 ? buffers.Pop() : [];
        try
        {
            host.VisitChildren(child =>
            {
                if (child is UIElement visual && visual.IsDrawnBy(this))
                {
                    children.Add(visual);
                }

                return true;
            });

            for (int index = children.Count - 1; index >= 0; index--)
            {
                if (children[index].HitTest(point) is UIElement hit)
                {
                    return hit;
                }
            }

            return null;
        }
        finally
        {
            children.Clear();
            buffers.Push(children);
        }
    }

    /// <summary>
    /// Draws the children a host visits, grouping neighbours that share a clip under one scope. Pooled per
    /// thread, since a child's own drawing visits its children with another instance.
    /// </summary>
    private sealed class ChildRenderer
    {
        private UIElement? _host;
        private IGraphicsContext? _context;
        private ChildClip? _openClip;

        internal ChildRenderer()
        {
            Visitor = Visit;
        }

        internal Func<Element, bool> Visitor { get; }

        internal void Begin(UIElement host, IGraphicsContext context)
        {
            _host = host;
            _context = context;
            _openClip = null;
        }

        internal void End()
        {
            CloseClip();
            _host = null;
            _context = null;
        }

        private bool Visit(Element child)
        {
            if (child is not UIElement visual || !visual.IsDrawnBy(_host!))
            {
                return true;
            }

            var clip = _host!.GetChildClip(visual);
            if (clip != _openClip)
            {
                CloseClip();
                if (clip is ChildClip opened)
                {
                    var context = _context!;
                    context.Save();
                    if (opened.IsRounded)
                    {
                        context.SetClipRoundedRect(opened.Rect, opened.RadiusX, opened.RadiusY);
                    }
                    else
                    {
                        context.SetClip(opened.Rect);
                    }

                    _openClip = opened;
                }
            }

            visual.Render(_context!);
            return true;
        }

        private void CloseClip()
        {
            if (_openClip != null)
            {
                _context!.Restore();
                _openClip = null;
            }
        }
    }

    /// <summary>Writes the children a host visits into a plan, with the same clip grouping as <see cref="ChildRenderer"/>.</summary>
    private sealed class ChildPlanner
    {
        private UIElement? _host;
        private CompositionPlanBuilder? _builder;
        private ChildClip? _openClip;

        internal ChildPlanner()
        {
            Visitor = Visit;
        }

        internal Func<Element, bool> Visitor { get; }

        internal void Begin(UIElement host, CompositionPlanBuilder builder)
        {
            _host = host;
            _builder = builder;
            _openClip = null;
        }

        internal void End()
        {
            CloseClip();
            _host = null;
            _builder = null;
        }

        private bool Visit(Element child)
        {
            if (child is not UIElement visual || !visual.IsDrawnBy(_host!))
            {
                return true;
            }

            var clip = _host!.GetChildClip(visual);
            if (clip != _openClip)
            {
                CloseClip();
                if (clip is ChildClip opened)
                {
                    if (opened.IsRounded)
                    {
                        _builder!.PushClipRoundedRect(opened.Rect, opened.RadiusX, opened.RadiusY);
                    }
                    else
                    {
                        _builder!.PushClipRect(opened.Rect);
                    }

                    _openClip = opened;
                }
            }

            if (_host.IsChildFrame(visual))
            {
                _builder!.PushFrame();
                _builder.Child(visual);
                _builder.Pop();
            }
            else
            {
                _builder!.Child(visual);
            }

            return true;
        }

        private void CloseClip()
        {
            if (_openClip != null)
            {
                _builder!.Pop();
                _openClip = null;
            }
        }
    }

}
