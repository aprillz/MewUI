using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Retained;

namespace Aprillz.MewUI.Controls;

public abstract partial class UIElement
{
    private const byte COMPOSITION_UNKNOWN = 0;
    private const byte COMPOSITION_DECLARED = 1;
    private const byte COMPOSITION_COMPATIBILITY = 2;

    private int _renderContentVersion;
    private int _subtreeContentVersion;
    private byte _compositionSupport;
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
    internal bool IsCompatibilityRoot => !SupportsDeclaredComposition || HasBitmapCache;

    /// <summary>
    /// True when the composition this type declares describes how it draws, so the scene can own its
    /// children as independent nodes. Otherwise it is recorded as one compatibility subtree.
    /// </summary>
    internal bool SupportsDeclaredComposition
    {
        get
        {
            if (_compositionSupport == COMPOSITION_UNKNOWN)
            {
                bool supported = DeclaredComposition.TryGetKnown(GetType(), out bool known)
                    ? known
                    : DeclaredComposition.IsSupported(this, RenderSubtree, WriteComposition);
                _compositionSupport = supported ? COMPOSITION_DECLARED : COMPOSITION_COMPATIBILITY;
            }

            return _compositionSupport == COMPOSITION_DECLARED;
        }
    }

    /// <summary>
    /// Draws the content of one own-content slot. The default has a single slot holding
    /// <see cref="OnRender"/>; a visual that draws before and after its children declares more.
    /// </summary>
    internal virtual void WriteOwnContent(IGraphicsContext context, int slotIndex) => OnRender(context);

    /// <summary>
    /// Declares the order of this visual's content slots, children, and the scopes that apply to
    /// them. The default is one content slot and no children.
    /// </summary>
    internal virtual void WriteComposition(CompositionPlanBuilder builder) => builder.Content(0);
}
