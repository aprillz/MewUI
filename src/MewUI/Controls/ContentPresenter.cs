using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The visual slot inside a control template that displays a logical slot of the templated
/// parent. Without a presenter the projected slot stays out of the visual tree.
/// </summary>
public sealed class ContentPresenter : FrameworkElement, IVisualTreeHost
{
    private Element? _projected;
    private MewProperty<Element?>? _contentSource;

    /// <summary>
    /// Gets or sets which slot of the templated parent to display. Leaving it unset projects the
    /// templated parent's own display slot; set it to another element-typed slot (e.g. Header)
    /// inside the template build.
    /// </summary>
    public MewProperty<Element?> ContentSource
    {
        get => _contentSource ?? ContentControl.ContentProperty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_contentSource, value))
            {
                return;
            }

            _contentSource = value;
            UpdateProjection();
        }
    }

    // An unset ContentSource follows the owner's slot, so a bare presenter works in any template
    // without the author knowing which property that control displays.
    internal MewProperty<Element?>? ResolvedContentSource
        => _contentSource ?? TemplatedParent?.ResolveDefaultContentSource();

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
        var source = ResolvedContentSource;
        var content = TemplatedParent != null && source != null
            ? TemplatedParent.PropertyStore.GetValue(source)
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

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var projected = ActiveProjection;
        return projected == null || visitor(projected);
    }
}
