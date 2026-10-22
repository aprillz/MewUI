namespace Aprillz.MewUI.Controls;

/// <summary>
/// A lightweight wrapper that can be positioned relative to an adorned element and hosted by a window adorner layer.
/// This mirrors the intent of WPF's <c>System.Windows.Documents.Adorner</c> but is implemented in a MewUI-native way.
/// </summary>
public class Adorner : Panel
{
    public Adorner(UIElement adornedElement)
    {
        ArgumentNullException.ThrowIfNull(adornedElement);
        AdornedElement = adornedElement;
    }

    public Adorner(UIElement adornedElement, UIElement child) : this(adornedElement)
    {
        ArgumentNullException.ThrowIfNull(child);
        Add(child);
    }

    /// <summary>Gets the element this adorner is positioned relative to.</summary>
    public UIElement AdornedElement { get; }

    protected override Size MeasureContent(Size availableSize)
    {
        var maxWidth = 0.0;
        var maxHeight = 0.0;

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(availableSize);
            var size = Children[i].DesiredSize;
            if (size.Width > maxWidth) maxWidth = size.Width;
            if (size.Height > maxHeight) maxHeight = size.Height;
        }

        return new Size(maxWidth, maxHeight);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        // Arrange children in absolute (window) coordinates.
        // This matches MewUI's layout convention where Bounds are in the same coordinate space
        // used by hit testing and rendering.
        var rect = bounds;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(rect);
        }
    }

    /// <summary>
    /// Only what an adorner draws takes the pointer. An adorner spans the whole element it adorns,
    /// so answering for the empty space around its children - which is what a panel does - would put
    /// a sheet of glass over everything underneath.
    /// </summary>
    protected override UIElement? OnHitTest(Point point)
    {
        var hit = base.OnHitTest(point);
        return ReferenceEquals(hit, this) ? null : hit;
    }
}
