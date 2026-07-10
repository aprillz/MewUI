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
        if (_projected == null)
        {
            return Size.Empty;
        }

        _projected.Measure(availableSize);
        return _projected.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _projected?.Arrange(bounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        _projected?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (_projected is UIElement uiContent)
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
        => _projected == null || visitor(_projected);
}
