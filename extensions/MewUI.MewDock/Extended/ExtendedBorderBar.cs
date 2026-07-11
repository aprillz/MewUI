using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// The Unpinned (auto-hide) Tool Dock: the faithful <see cref="FlexBorderBar"/> edge strip plus a caption bar on
/// top of the instant-expand panel (title + pin/close). It does NOT host a bottom tab strip or splitting - those
/// belong to a Pinned Tool TabSet. Clicking an edge tab instant-expands that tool; the pin re-docks/auto-hides.
/// </summary>
internal sealed class ExtendedBorderBar : FlexBorderBar
{
    private readonly DockCaption _caption;

    public ExtendedBorderBar(BorderNode border, FlexViewContext context) : base(border, context)
    {
        _caption = DockCaption.ForBorder(border);
        AttachChild(_caption);
    }

    // Auto-hide strip tabs: no per-tab close, no drag, always-closed chrome.
    protected override FlexBorderButton CreateButton(TabNode tab) => new ExtendedBorderButton(tab, _border, _context);

    // Auto-hide: the edge strip plus a gap reserve space (so the strip sits off the content); the revealed panel
    // overlays the document content.
    public override double Footprint => BarThickness + SplitterSize;

    public override double OverlayExtent => Expanded ? PanelSize : 0;

    internal override void SyncSelection()
    {
        _caption.Refresh();
        InvalidateVisualState(); // focus/reveal change -> re-evaluate the accent border (with transition)
        base.SyncSelection();
    }

    // The caption participates in the visual tree only while expanded (render and hit test both
    // gate on Expanded), so a collapsed bar must not yield it.
    protected override bool VisitChildrenCore(Func<Element, bool> visitor)
        => (!Expanded || visitor(_caption)) && base.VisitChildrenCore(visitor);

    protected override Size MeasureContent(Size availableSize)
    {
        _caption.Measure(availableSize);
        return base.MeasureContent(availableSize);
    }

    // Panel = caption (top) + content (below), inside the floated frame border. Use the SAME pixel-snapped frame as
    // OnRender so the caption edges line up with the border at fractional DPI (e.g. 150%) instead of being 1px off.
    protected override void ArrangePanel(Rect panelRect)
    {
        var snapped = GetSnappedBorderBounds(FloatPanel(panelRect));
        double bd = Theme.Metrics.ControlBorderThickness;
        var inner = new Rect(snapped.X + bd, snapped.Y + bd,
            Math.Max(0, snapped.Width - 2 * bd), Math.Max(0, snapped.Height - 2 * bd));
        var captionRect = new Rect(inner.X, inner.Y, inner.Width, Math.Min(_caption.DesiredSize.Height, inner.Height));
        var contentRect = new Rect(inner.X, captionRect.Bottom, inner.Width, Math.Max(0, inner.Bottom - captionRect.Bottom));

        _caption.Arrange(captionRect);
        _content?.Arrange(contentRect);
    }

    // The revealed panel floats off the strip by the border gap on the STRIP-facing side only (the other sides reach
    // the dock-area edges).
    private Rect FloatPanel(Rect panelRect)
    {
        double m = SplitterSize;
        return _border.Location switch
        {
            DockLocation.Left => new Rect(panelRect.X + m, panelRect.Y, Math.Max(0, panelRect.Width - m), panelRect.Height),
            DockLocation.Right => new Rect(panelRect.X, panelRect.Y, Math.Max(0, panelRect.Width - m), panelRect.Height),
            DockLocation.Top => new Rect(panelRect.X, panelRect.Y + m, panelRect.Width, Math.Max(0, panelRect.Height - m)),
            _ => new Rect(panelRect.X, panelRect.Y, panelRect.Width, Math.Max(0, panelRect.Height - m)), // Bottom
        };
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Strip background only; the revealed panel is a FLOATING box with every corner rounded and no pierce (the
        // faithful base draws a directional, strip-connected frame, which is wrong for an auto-hide overlay).
        context.FillRectangle(_border.TabHeaderRect, Theme.Palette.ContainerBackground);
        if (!Expanded)
        {
            return;
        }
        var panel = FloatPanel(_border.ContentRect);
        if (panel.Width <= 0 || panel.Height <= 0)
        {
            return;
        }
        double r = Theme.Metrics.ControlCornerRadius;
        double t = Theme.Metrics.ControlBorderThickness;
        // Styled border (same accent-on-focus colour + transition as the other panes; see DockStyles).
        DrawBackgroundAndBorder(context, GetSnappedBorderBounds(panel),
            Theme.Palette.ContainerBackground, GetValue(Control.BorderBrushProperty), new Thickness(t), new CornerRadius(r));
    }

    // Focused while the auto-hide pane is revealed and no tabset holds focus (drives the accent border via the style).
    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        var flags = state.Flags & ~VisualStateFlags.Focused;
        if (Expanded && _border.Model.FocusedTabSet is null)
        {
            flags |= VisualStateFlags.Focused;
        }
        return new VisualState { Flags = flags };
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        base.RenderSubtree(context);
        if (Expanded)
        {
            _caption.Render(context);
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (Expanded && _caption.HitTest(point) is UIElement captionHit)
        {
            return captionHit;
        }
        return base.OnHitTest(point);
    }
}
