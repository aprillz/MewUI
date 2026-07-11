using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The visual slot inside a control template that displays a logical slot of the templated
/// parent. Without a presenter the projected slot stays out of the visual tree.
/// </summary>
public sealed class ContentPresenter : FrameworkElement, IVisualTreeHost
{
    private Element? _projected;

    /// <summary>
    /// Gets or sets which logical slot of the templated parent to display.
    /// Defaults to <see cref="ContentControl.ContentProperty"/>; set to another
    /// element-typed slot (e.g. Header) inside the template build.
    /// </summary>
    public MewProperty<Element?> ContentSource { get; set; } = ContentControl.ContentProperty;

    internal Control? TemplatedParent { get; private set; }

    // A duplicate presenter for the same slot loses ownership to the last writer; a projection
    // counts only while the content is still parented here, so the loser degrades to empty.
    private Element? ActiveProjection
    {
        get
        {
            var projected = _projected;
            return projected != null && projected.Parent == this ? projected : null;
        }
    }

    internal void AttachToTemplatedParent(Control owner)
    {
        TemplatedParent = owner;
        UpdateProjection();
    }

    internal void DetachFromTemplatedParent()
    {
        TemplatedParent = null;
        UpdateProjection();
    }

    internal void UpdateProjection()
    {
        var content = TemplatedParent != null
            ? TemplatedParent.PropertyStore.GetValue(ContentSource)
            : null;
        if (ReferenceEquals(_projected, content))
        {
            return;
        }

        if (_projected != null && _projected.Parent == this)
        {
            _projected.Parent = null;
        }

        _projected = content;
        if (content != null)
        {
            // The Parent setter normalizes reassignment, so the content moves here even when
            // it is still visually attached to the control (pre-template compatibility path).
            content.Parent = this;
        }

        InvalidateMeasure();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var projected = ActiveProjection;
        if (projected == null)
        {
            return Size.Empty;
        }

        projected.Measure(availableSize);
        return projected.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var projected = ActiveProjection;
        projected?.Arrange(bounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var projected = ActiveProjection;
        projected?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        var projected = ActiveProjection;
        if (projected is UIElement uiContent)
        {
            var hit = uiContent.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return Bounds.Contains(point) ? this : null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var projected = ActiveProjection;
        return projected == null || visitor(projected);
    }
}
