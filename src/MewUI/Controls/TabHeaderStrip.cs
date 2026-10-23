using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// The row of tab headers and the overflow button a <see cref="TabControl"/> lays out. The control keeps the
/// header logic; this element gives the row a place of its own, so a template can put it into a host part.
/// </summary>
internal sealed class TabHeaderStrip : FrameworkElement, IVisualTreeHost
{
    private readonly TabControl _owner;

    internal TabHeaderStrip(TabControl owner)
    {
        _owner = owner;
    }

    protected override Size MeasureContent(Size availableSize) => _owner.MeasureHeaderStrip(availableSize);

    protected override void ArrangeContent(Rect bounds) => _owner.ArrangeHeaderStrip(bounds);

    protected override void RenderSubtree(IGraphicsContext context) => _owner.RenderHeaderStrip(context);

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(0);
        _owner.WriteHeaderStripComposition(builder);
    }

    protected override UIElement? OnHitTest(Point point) => _owner.HitTestHeaderStrip(point);

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => _owner.VisitHeaderStrip(visitor);
}
