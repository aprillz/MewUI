using Aprillz.MewUI.Rendering;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI;

/// <summary>
/// Base class for all UI elements. Provides the core Measure/Arrange layout system.
/// </summary>
public abstract class Element
{
    private bool _dpiCacheValid;
    private uint _cachedDpi;
    private Size _lastMeasureConstraint;
    private bool _hasMeasureConstraint;

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
    /// Gets the final bounds calculated during the Arrange pass.
    /// </summary>
    public Rect Bounds { get; private set; }

    /// <summary>
    /// Gets or sets the parent element.
    /// </summary>
    public Element? Parent
    {
        get; internal set
        {
            if (field != value)
            {
                var oldRoot = FindVisualRoot();
                field = value;
                ClearDpiCacheDeep();
                OnParentChanged();

                var newRoot = FindVisualRoot();
                if (!ReferenceEquals(oldRoot, newRoot))
                {
                    NotifyVisualRootChanged(oldRoot, newRoot);
                }
            }
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

        var measured = MeasureCore(availableSize);
        DesiredSize = ApplyLayoutRounding(measured);
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
        ArrangeCore(arrangedRect);
        IsArrangeDirty = false;
    }

    /// <summary>
    /// Core arrangement logic. Override in derived classes.
    /// </summary>
    protected abstract void ArrangeCore(Rect finalRect);

    /// <summary>
    /// Invalidates the Measure pass, causing a re-measure on next layout.
    /// </summary>
    public virtual void InvalidateMeasure()
    {
        IsMeasureDirty = true;
        IsArrangeDirty = true;
        Parent?.InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Invalidates the Arrange pass, causing a re-arrange on next layout.
    /// </summary>
    public virtual void InvalidateArrange()
    {
        IsArrangeDirty = true;
        Parent?.InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>
    /// Invalidates the visual representation, causing a repaint.
    /// </summary>
    public virtual void InvalidateVisual() =>
        // Will be implemented to trigger repaint
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
    /// Renders the element to the graphics context.
    /// </summary>
    public virtual void Render(IGraphicsContext context)
    {
        // Base implementation does nothing
    }

    /// <summary>
    /// Finds the visual root of this element (typically a Window).
    /// </summary>
    public Element? FindVisualRoot()
    {
        var current = this;
        while (current.Parent != null)
        {
            current = current.Parent;
        }
        return current;
    }

    private void NotifyVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        VisualTree.Visit(this, element => element.OnVisualRootChanged(oldRoot, newRoot));
    }

    internal uint GetDpiCached()
    {
        if (_dpiCacheValid)
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
        _dpiCacheValid = true;
        return dpi;
    }

    internal double GetDpiScaleCached() => GetDpiCached() / 96.0;

    internal void ClearDpiCache() => _dpiCacheValid = false;

    internal void ClearDpiCacheDeep() => VisualTree.Visit(this, e => e.ClearDpiCache());

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
