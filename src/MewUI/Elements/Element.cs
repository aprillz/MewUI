using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Diagnostics;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for all UI elements. Provides the core Measure/Arrange layout system.
/// </summary>
public abstract class Element : MewObject
{
    private Element? _cachedVisualRoot;
    private int _visualRootCacheVersion = -1;
    private int _dpiCacheVersion = -1;
    private uint _cachedDpi;

    // Version of this element's ancestor context. Bumped for the whole subtree when the
    // parent chain changes; caches resolved through that chain stamp it and re-resolve on mismatch.
    private int _contextVersion;

    // _contextVersion at the time inherited values were last cached. A mismatch means the
    // cached entries came from a previous chain and must be flushed before the next resolve.
    private int _inheritedCacheVersion = -1;

    internal int ContextVersion => _contextVersion;
    private Size _lastMeasureConstraint;
    private bool _hasMeasureConstraint;

    private protected override bool IsInheritedCacheCurrent() => _inheritedCacheVersion == _contextVersion;

    private void EnsureInheritedEpoch()
    {
        if (_inheritedCacheVersion != _contextVersion)
        {
            if (HasPropertyStore)
            {
                PropertyStore.ClearAllInherited();
            }
            _inheritedCacheVersion = _contextVersion;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool SetDouble(ref double field, double value)
    {
        // Needed for NaN == NaN semantics (double.Equals treats NaN as equal).
        if (field.Equals(value))
        {
            return false;
        }

        field = value;
        return true;
    }

    /// <summary>
    /// Gets the desired size calculated during the Measure pass.
    /// </summary>
    public Size DesiredSize { get; private set; }

    /// <summary>
    /// Gets the final bounds calculated during the Arrange pass, in window-absolute coordinates
    /// (not the parent's coordinate space): panels arrange children by offsetting from their own
    /// absolute <c>Bounds</c>, and element transforms are translate-only. Custom panels must pass
    /// window-absolute rects to <see cref="Arrange"/>. Prefer <see cref="RenderSize"/> for
    /// WPF-like usage.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Rect Bounds { get; private set; }

    /// <summary>
    /// Gets the final render size calculated during the Arrange pass.
    /// Equivalent to WPF's <c>RenderSize</c>.
    /// </summary>
    public Size RenderSize => new(Bounds.Width, Bounds.Height);

    /// <summary>
    /// Gets or sets the parent element.
    /// </summary>
    public Element? Parent
    {
        get;
        internal set
        {
            if (field != value)
            {
                // Normalize a direct non-null to non-null reassignment into detach then attach,
                // so no caller can bypass detach-side context handling (focus unwinding,
                // style release, visual-root notifications).
                if (field != null && value != null)
                {
                    Parent = null;
                }

                var oldRoot = FindVisualRoot();

                // Detaching: release window-scoped state (focus) while the parent chain is still intact,
                // so focus-within can unwind up to the retained ancestor before the link is severed.
                if (value == null)
                {
                    OnDetaching(oldRoot);
                }

                field = value;
                BumpContextVersionDeep();
                OnParentChanged();

                // A subtree already dirty (new elements default dirty, or invalidated while
                // detached) must re-propagate into the new parent chain on attach - otherwise
                // Window's O(1) descendant-dirty checks (HasMeasureDirty/IsLayoutDirty) would miss
                // it, since those no longer walk the tree and only trust the root's own flag.
                if (value != null)
                {
                    if (IsMeasureDirty)
                    {
                        value.InvalidateMeasure();
                    }
                    else if (IsArrangeDirty)
                    {
                        value.InvalidateArrange();
                    }
                }

                var newRoot = FindVisualRoot();
                if (!ReferenceEquals(oldRoot, newRoot))
                {
                    NotifyVisualRootChanged(oldRoot, newRoot);
                }
            }
        }
    }

    private Element? _contextParentOverride;

    /// <summary>
    /// Optional resolution-context parent for elements visually hosted outside their
    /// conceptual owner (e.g. overlay-hosted content owned by an element deeper in the tree).
    /// Style and inherited-property resolution divert through it; layout, DPI, and
    /// visual-root resolution keep following <see cref="Parent"/>.
    /// </summary>
    internal Element? ContextParentOverride
    {
        get => _contextParentOverride;
        set
        {
            if (_contextParentOverride != value)
            {
                _contextParentOverride = value;

                // Invalidate context-stamped caches resolved through the old chain, then
                // eagerly diff cached inherited values so layout/observers react even when
                // the override changes while the element stays attached (owner switch).
                BumpContextVersionDeep();
                RefreshInheritedSubtree();
            }
        }
    }

    /// <summary>
    /// Next element in the context resolution chain: the override when set,
    /// otherwise the visual parent.
    /// </summary>
    internal Element? ContextParent => _contextParentOverride ?? Parent;

    /// <summary>
    /// Attaches a child element to this element. Use this in derived controls
    /// instead of setting Parent directly.
    /// </summary>
    /// <param name="child">The child to attach.</param>
    protected void AttachChild(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (child.Parent == this)
        {
            return;
        }

        if (child.Parent != null)
        {
            throw new InvalidOperationException("The element already has a parent.");
        }

        child.Parent = this;
    }

    /// <summary>
    /// Detaches a child element from this element if attached.
    /// </summary>
    /// <param name="child">The child to detach.</param>
    protected void DetachChild(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (child.Parent == this)
        {
            child.Parent = null;
        }
    }

    /// <summary>
    /// Gets the logical owner of this element. Independent from the visual <see cref="Parent"/>;
    /// a logical child may exist without being attached to any visual tree.
    /// </summary>
    public Element? LogicalParent { get; internal set; }

    /// <summary>
    /// Makes this element the logical owner of <paramref name="child"/> without touching
    /// its visual <see cref="Parent"/>.
    /// </summary>
    /// <param name="child">The element to own logically.</param>
    protected void AttachLogicalChild(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (ReferenceEquals(child, this))
        {
            throw new InvalidOperationException("An element cannot be its own logical child.");
        }

        if (child.LogicalParent == this)
        {
            return;
        }

        if (child.LogicalParent != null)
        {
            throw new InvalidOperationException("The element already has a logical parent.");
        }

        for (var ancestor = LogicalParent; ancestor != null; ancestor = ancestor.LogicalParent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new InvalidOperationException("Attaching the element would create a logical tree cycle.");
            }
        }

        var oldRoot = child.FindLogicalRoot();
        child.LogicalParent = this;
        child.OnLogicalParentChanged();
        NotifyLogicalRootChanged(child, oldRoot, child.FindLogicalRoot());
    }

    /// <summary>
    /// Releases the logical ownership of <paramref name="child"/> if this element is its owner.
    /// Does not touch the visual <see cref="Parent"/> and never disposes the child.
    /// </summary>
    /// <param name="child">The element to release.</param>
    protected void DetachLogicalChild(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (child.LogicalParent != this)
        {
            return;
        }

        var oldRoot = child.FindLogicalRoot();
        child.LogicalParent = null;
        child.OnLogicalParentChanged();
        NotifyLogicalRootChanged(child, oldRoot, child);
    }

    /// <summary>
    /// Rejects a proposed logical child before it is committed: self, an element owned by a
    /// different logical parent, or a logical ancestor of this element (cycle). Intended for
    /// use in a property validate callback so a rejecting throw leaves no store/tree mismatch.
    /// </summary>
    /// <param name="candidate">The proposed logical child; null is always valid.</param>
    protected void ValidateLogicalChild(Element? candidate)
    {
        if (candidate == null)
        {
            return;
        }

        if (ReferenceEquals(candidate, this))
        {
            throw new InvalidOperationException("An element cannot be its own logical child.");
        }

        if (candidate.LogicalParent != null && candidate.LogicalParent != this)
        {
            throw new InvalidOperationException("The element already has a logical parent.");
        }

        for (var ancestor = LogicalParent; ancestor != null; ancestor = ancestor.LogicalParent)
        {
            if (ReferenceEquals(ancestor, candidate))
            {
                throw new InvalidOperationException("Attaching the element would create a logical tree cycle.");
            }
        }
    }

    /// <summary>
    /// Replaces a logical child slot: detaches the old child logically and visually, then
    /// attaches the new one the same way. All failure causes must have been rejected up front
    /// (see <see cref="ValidateLogicalChild"/>); the mutation itself does not fail.
    /// </summary>
    /// <param name="oldChild">The child leaving the slot.</param>
    /// <param name="newChild">The child entering the slot.</param>
    protected void ChangeLogicalChild(Element? oldChild, Element? newChild)
    {
        if (oldChild != null)
        {
            DetachLogicalChild(oldChild);
            if (oldChild.Parent == this)
            {
                oldChild.Parent = null;
            }
        }

        if (newChild != null)
        {
            AttachLogicalChild(newChild);
            newChild.Parent = this;
        }
    }

    /// <summary>
    /// Returns the topmost element of the logical parent chain,
    /// this element itself when it has no logical owner.
    /// </summary>
    public Element FindLogicalRoot()
    {
        var current = this;
        while (current.LogicalParent != null)
        {
            current = current.LogicalParent;
        }

        return current;
    }

    /// <summary>
    /// Called when <see cref="LogicalParent"/> changes.
    /// </summary>
    protected virtual void OnLogicalParentChanged() { }

    /// <summary>
    /// Called on every element of the logical subtree when its logical root changes.
    /// </summary>
    protected virtual void OnLogicalRootChanged(Element? oldRoot, Element? newRoot) { }

    private static void NotifyLogicalRootChanged(Element element, Element? oldRoot, Element? newRoot)
    {
        if (!ReferenceEquals(oldRoot, newRoot))
        {
            LogicalTree.Visit(element, node => node.OnLogicalRootChanged(oldRoot, newRoot));
        }
    }

    /// <summary>
    /// Gets whether a new Measure pass is needed.
    /// </summary>
    public bool IsMeasureDirty { get; private set; } = true;

    /// <summary>
    /// Gets whether a new Arrange pass is needed.
    /// </summary>
    public bool IsArrangeDirty { get; private set; } = true;

    /// <summary>
    /// Measures the element and determines its desired size.
    /// </summary>
    public void Measure(Size availableSize)
    {
        if (!IsMeasureDirty && _hasMeasureConstraint && _lastMeasureConstraint == availableSize)
        {
            return;
        }

        Size measured;
        using (PerformanceProfiler.Instance.SampleElement(GetType(), ProfilerSampleCategory.Measure))
        {
            measured = MeasureCore(availableSize);
        }

        var clamped = new Size(
            double.IsPositiveInfinity(availableSize.Width)
                ? measured.Width
                : Math.Min(measured.Width, availableSize.Width),
            double.IsPositiveInfinity(availableSize.Height)
                ? measured.Height
                : Math.Min(measured.Height, availableSize.Height));

        DesiredSize = ApplyLayoutRounding(clamped);
        IsMeasureDirty = false;
        _lastMeasureConstraint = availableSize;
        _hasMeasureConstraint = true;
    }

    /// <summary>
    /// Core measurement logic. Override in derived classes.
    /// </summary>
    protected abstract Size MeasureCore(Size availableSize);

    /// <summary>
    /// Positions and sizes the element within the given bounds.
    /// </summary>
    public void Arrange(Rect finalRect)
    {
        var arrangedRect = ApplyLayoutRounding(GetArrangedBounds(finalRect));

        if (!IsArrangeDirty && Bounds == arrangedRect)
        {
            return;
        }

        Bounds = arrangedRect;
        using (PerformanceProfiler.Instance.SampleElement(GetType(), ProfilerSampleCategory.Arrange))
        {
            ArrangeCore(arrangedRect);
        }
        IsArrangeDirty = false;
    }

    /// <summary>
    /// Core arrangement logic. Override in derived classes.
    /// </summary>
    protected abstract void ArrangeCore(Rect finalRect);

    /// <summary>
    /// Invalidates the Measure pass, causing a re-measure on next layout.
    /// </summary>
    /// <remarks>
    /// Idempotent per dirty cycle: if already dirty, returns immediately (Measure will reset the
    /// flag). This bounds both the upward propagation and the optional subtree cascade to a single
    /// visit per node per cycle.
    /// For hosts that implement <see cref="ISubtreeInvalidationHost"/>, the dirty flag also
    /// cascades into the visual subtree so private composition (e.g. ScrollViewer/presenter) does
    /// not get skipped by <see cref="Measure"/>'s same-constraint short-circuit.
    /// </remarks>
    public virtual void InvalidateMeasure()
    {
        if (IsMeasureDirty)
        {
            // Unconditional on purpose: an already-dirty parent's flag can be stale (a
            // virtualizing presenter may have skipped a still-dirty child), so it cannot be
            // trusted to have already notified its own ancestors. This walk also doubles as the
            // render-request wake up to the Window, which must run every time.
            Parent?.InvalidateMeasure();
            return;
        }
        IsMeasureDirty = true;
        IsArrangeDirty = true;
        Parent?.InvalidateMeasure();

        // The marker gates the *entry* of a cascade. Once started, the subtree is descended
        // unconditionally via the helper - otherwise a non-marker intermediate like ScrollViewer
        // would halt the cascade before it reaches the presenter that needs to re-measure.
        if (this is ISubtreeInvalidationHost)
        {
            CascadeMeasureInvalidationToSubtree();
        }

        InvalidateVisual();
    }

    private void CascadeMeasureInvalidationToSubtree()
    {
        if (this is not IVisualTreeHost host) return;
        host.VisitChildren(static child =>
        {
            if (child.IsMeasureDirty) return true; // idempotent
            child.IsMeasureDirty = true;
            child.IsArrangeDirty = true;
            child.CascadeMeasureInvalidationToSubtree();
            return true;
        });
    }

    /// <summary>
    /// Invalidates the Arrange pass, causing a re-arrange on next layout.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="InvalidateMeasure"/>: even when this node is already dirty, the
    /// upward propagation is re-issued so a parent that cleared its own dirty flag mid-pass
    /// (e.g. a child re-dirtying after the parent's <c>ArrangeCore</c> finished arranging it)
    /// still gets notified. Without this, ScrollIntoView-style re-arrange requests issued
    /// during a parent's Arrange would silently drop. Cascades into
    /// <see cref="ISubtreeInvalidationHost"/> subtrees so private composition re-arranges
    /// instead of short-circuiting on unchanged bounds.
    /// </remarks>
    public virtual void InvalidateArrange()
    {
        if (IsArrangeDirty)
        {
            Parent?.InvalidateArrange();
            return;
        }
        IsArrangeDirty = true;
        Parent?.InvalidateArrange();

        if (this is ISubtreeInvalidationHost)
        {
            CascadeArrangeInvalidationToSubtree();
        }

        InvalidateVisual();
    }

    private void CascadeArrangeInvalidationToSubtree()
    {
        if (this is not IVisualTreeHost host) return;
        host.VisitChildren(static child =>
        {
            if (child.IsArrangeDirty) return true;
            child.IsArrangeDirty = true;
            child.CascadeArrangeInvalidationToSubtree();
            return true;
        });
    }

    /// <summary>
    /// Invalidates the visual representation, causing a repaint.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="InvalidateMeasure"/> and <see cref="InvalidateArrange"/>, this walk is
    /// intentionally left unbounded: bounding it would require a per-element dirty flag that gets
    /// cleared on render, but <see cref="Render"/> is bypassed by <c>UIElement</c>'s sealed
    /// override (which never calls the base implementation), so no in-scope hook exists to clear
    /// such a flag. Since every element defaults to "dirty", an unclearable flag would get stuck
    /// true and silently stop all repaint propagation after the first call. Left unoptimized until
    /// UIElement gains a render-completion hook.
    /// </remarks>
    public virtual void InvalidateVisual() =>
        Parent?.InvalidateVisual();

    /// <summary>
    /// Called when the parent element changes.
    /// </summary>
    protected virtual void OnParentChanged() { }

    /// <summary>
    /// Called when this element's visual root changes (attach/detach from a Window).
    /// Raised for the entire subtree starting at the element whose Parent changed.
    /// </summary>
    protected virtual void OnVisualRootChanged(Element? oldRoot, Element? newRoot) { }

    /// <summary>
    /// Called on the element whose <see cref="Parent"/> is being cleared, before the link is severed
    /// (parent chain still intact). Override to release state that depends on the old ancestor chain.
    /// </summary>
    internal virtual void OnDetaching(Element? oldRoot) { }

    /// <summary>
    /// Renders the element to the graphics context.
    /// Subclasses must override <see cref="OnRender"/> instead of this method.
    /// </summary>
    public virtual void Render(IGraphicsContext context)
    {
        OnRender(context);
    }

    /// <summary>
    /// When overridden, performs the actual rendering.
    /// </summary>
    protected virtual void OnRender(IGraphicsContext context) { }

    /// <summary>
    /// Finds the visual root of this element (typically a Window).
    /// </summary>
    public Element? FindVisualRoot()
    {
        if (_visualRootCacheVersion == _contextVersion)
        {
            return _cachedVisualRoot;
        }

        Element? root;
        if (Parent == null)
        {
            root = this is Window ? this : null;
        }
        else
        {
            var current = Parent;
            while (current.Parent != null)
            {
                current = current.Parent;
            }
            root = current is Window ? current : null;
        }

        _cachedVisualRoot = root;
        _visualRootCacheVersion = _contextVersion;
        return root;
    }

    private void NotifyVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        VisualTree.Visit(this, element => element.OnVisualRootChanged(oldRoot, newRoot));
    }

    internal uint GetDpiCached()
    {
        if (_dpiCacheVersion == _contextVersion)
        {
            return _cachedDpi;
        }

        uint dpi = 0;
        for (Element? current = this; current != null; current = current.Parent)
        {
            if (current is Window window)
            {
                dpi = window.Dpi;
                break;
            }
        }

        if (dpi == 0)
        {
            dpi = DpiHelper.GetSystemDpi();
        }

        _cachedDpi = dpi;
        _dpiCacheVersion = _contextVersion;
        return dpi;
    }

    internal double GetDpiScaleCached() => GetDpiCached() / 96.0;

    internal void ClearDpiCache() => _dpiCacheVersion = -1;

    internal void ClearDpiCacheDeep() => VisualTree.Visit(this, static element => element.ClearDpiCache());

    private void BumpContextVersionDeep()
    {
        VisualTree.Visit(this, static element =>
        {
            element._contextVersion++;
            // Drop the root reference so a detached subtree does not keep a closed Window alive.
            element._cachedVisualRoot = null;
        });
    }

    /// <summary>
    /// Determines whether this element is an ancestor of the specified element.
    /// </summary>
    /// <param name="descendant">The potential descendant element.</param>
    /// <returns>True if this element is an ancestor; otherwise, false.</returns>
    public bool IsAncestorOf(Element descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);

        return descendant.IsDescendantOf(this);
    }

    /// <summary>
    /// Determines whether this element is a descendant of the specified element.
    /// </summary>
    /// <param name="ancestor">The potential ancestor element.</param>
    /// <returns>True if this element is a descendant; otherwise, false.</returns>
    public bool IsDescendantOf(Element ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);

        for (var current = Parent; current != null; current = current.Parent)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a transform from this element's coordinate space to the specified ancestor's coordinate space.
    /// </summary>
    public GeneralTransform TransformToAncestor(Element ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);

        if (ancestor == this)
        {
            return IdentityGeneralTransform.Instance;
        }

        if (!IsDescendantOf(ancestor))
        {
            throw new InvalidOperationException("The specified element is not an ancestor of this element.");
        }

        double dx = Bounds.X - ancestor.Bounds.X;
        double dy = Bounds.Y - ancestor.Bounds.Y;
        return new TranslateGeneralTransform(dx, dy);
    }

    /// <summary>
    /// Returns a transform from this element's coordinate space to the specified descendant's coordinate space.
    /// </summary>
    public GeneralTransform TransformToDescendant(Element descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);

        if (descendant == this)
        {
            return IdentityGeneralTransform.Instance;
        }

        if (!descendant.IsDescendantOf(this))
        {
            throw new InvalidOperationException("The specified element is not a descendant of this element.");
        }

        return descendant.TransformToAncestor(this).Inverse;
    }

    /// <summary>
    /// Returns a transform from this element's coordinate space to the specified visual's coordinate space.
    /// Both elements must be in the same visual tree.
    /// </summary>
    public GeneralTransform TransformToVisual(Element visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        if (visual == this)
        {
            return IdentityGeneralTransform.Instance;
        }

        var root = FindVisualRoot();
        if (root == null || root != visual.FindVisualRoot())
        {
            throw new InvalidOperationException("The specified element is not in the same visual tree.");
        }

        double dx = Bounds.X - visual.Bounds.X;
        double dy = Bounds.Y - visual.Bounds.Y;
        return new TranslateGeneralTransform(dx, dy);
    }

    /// <summary>
    /// Converts a point in this element's coordinate space to the specified element's coordinate space.
    /// </summary>
    public Point TranslatePoint(Point point, Element relativeTo)
        => TransformToVisual(relativeTo).Transform(point);

    /// <summary>
    /// Converts a rectangle in this element's coordinate space to the specified element's coordinate space.
    /// </summary>
    public Rect TranslateRect(Rect rect, Element relativeTo)
        => TransformToVisual(relativeTo).TransformBounds(rect);

    /// <summary>
    /// Allows an element to adjust its final arranged bounds (e.g. alignment, margin, rounding).
    /// </summary>
    protected virtual Rect GetArrangedBounds(Rect finalRect) => finalRect;

    /// <inheritdoc/>
    protected override T ResolveInheritedValue<T>(MewProperty<T> property)
    {
        EnsureInheritedEpoch();

        for (var ancestor = ContextParent; ancestor != null; ancestor = ancestor.ContextParent)
        {
            if (ancestor.HasPropertyStore && ancestor.PropertyStore.HasOwnValue(property.Id))
            {
                var value = ancestor.PropertyStore.GetValue(property);
                PropertyStore.SetInherited(property, value);
                return value;
            }
        }

        return property.GetDefaultForType(PropertyStore.OwnerType);
    }

    /// <summary>
    /// Boxed, non-generic mirror of <see cref="ResolveInheritedValue{T}"/>. Resolves the inherited
    /// value from the parent chain and caches it, so a re-resolve during inherited-change propagation
    /// can read and forward the fresh value without knowing the property's static type.
    /// </summary>
    internal object? ResolveInheritedValueBoxed(MewProperty property)
    {
        EnsureInheritedEpoch();

        for (var ancestor = ContextParent; ancestor != null; ancestor = ancestor.ContextParent)
        {
            if (ancestor.HasPropertyStore && ancestor.PropertyStore.HasOwnValue(property.Id))
            {
                var value = ancestor.PropertyStore.GetBoxedValue(property);
                PropertyStore.SetInheritedBoxed(property, value);
                return value;
            }
        }

        return property.GetBoxedDefaultForType(PropertyStore.OwnerType);
    }

    /// <summary>
    /// Re-resolves cached inherited values for this subtree against the current context chain,
    /// invalidating layout/render and notifying observers for values that actually changed.
    /// The lazy epoch flush alone cannot do that: nothing re-reads a property whose change
    /// must wake layout (Measure short-circuits on same constraints) or a binding.
    /// </summary>
    internal void RefreshInheritedSubtree()
    {
        List<int> inheritedIds = new();
        List<object?> oldValues = new();

        VisualTree.Visit(this, element =>
        {
            if (!element.HasPropertyStore)
            {
                return;
            }

            inheritedIds.Clear();
            element.PropertyStore.GetInheritedPropertyIds(inheritedIds);
            if (inheritedIds.Count == 0)
            {
                return;
            }

            // Capture all old values first: the first re-resolve flushes the element's
            // whole inherited epoch, which would destroy the remaining old entries.
            oldValues.Clear();
            for (int i = 0; i < inheritedIds.Count; i++)
            {
                var property = MewPropertyRegistry.GetProperty(inheritedIds[i]);
                oldValues.Add(property != null ? element.PropertyStore.GetBoxedValue(property) : null);
            }

            for (int i = 0; i < inheritedIds.Count; i++)
            {
                var property = MewPropertyRegistry.GetProperty(inheritedIds[i]);
                if (property == null)
                {
                    continue;
                }

                object? newValue = element.ResolveInheritedValueBoxed(property);
                if (Equals(oldValues[i], newValue))
                {
                    continue;
                }

                if (property.AffectsLayout)
                {
                    element.InvalidateMeasure();
                }
                else if (property.AffectsRender)
                {
                    element.InvalidateVisual();
                }

                if (element is Controls.Control control)
                {
                    control.InvalidateFontCache(property);
                }

                if (element.HasChangeObservers(property.Id))
                {
                    element.NotifyObserversBoxed(property, oldValues[i], newValue);
                }
            }
        });
    }

    private Size ApplyLayoutRounding(Size size)
    {
        var root = FindVisualRoot();
        if (root is not ILayoutRoundingHost host || !host.UseLayoutRounding)
        {
            return size;
        }

        return LayoutRounding.RoundSizeToPixels(size, host.DpiScale);
    }

    private Rect ApplyLayoutRounding(Rect rect)
    {
        var root = FindVisualRoot();
        if (root is not ILayoutRoundingHost host || !host.UseLayoutRounding)
        {
            return rect;
        }

        return LayoutRounding.RoundRectToPixels(rect, host.DpiScale);
    }
}
