using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for virtualized, scrollable items controls.
/// Provides shared infrastructure: ScrollViewer hosting, rebind flag, deferred scroll-into-view request,
/// pending tab-focus helper, and default visual properties common to ListBox, GridView, and TreeView.
/// </summary>
public abstract class VirtualizedItemsBase : Control, IVisualTreeHost
{
    protected readonly ScrollViewer _scrollViewer;

    /// <summary>
    /// Set by derived class constructor (after the presenter is created).
    /// </summary>
    private protected PendingTabFocusHelper _tabFocusHelper = null!;

    private protected bool _rebindVisibleOnNextRender = true;
    private protected ScrollIntoViewRequest _scrollIntoViewRequest;

    protected VirtualizedItemsBase()
    {
        _scrollViewer = new ScrollViewer
        {
            BorderThickness = 0,
            Background = default,
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Auto,
        };
        _scrollViewer.Parent = this;
    }

    public override bool Focusable => true;
    protected override Color DefaultBackground => Theme.Palette.ControlBackground;
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;
    protected override double DefaultBorderThickness => Theme.Metrics.ControlBorderThickness;

    void IVisualTreeHost.VisitChildren(Action<Element> visitor) => VisitScrollChildren(visitor);

    /// <summary>
    /// Override to visit additional children (e.g. a header row) before or after the scroll viewer.
    /// </summary>
    protected virtual void VisitScrollChildren(Action<Element> visitor) => visitor(_scrollViewer);

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        var hit = _scrollViewer.HitTest(point);
        return hit ?? base.OnHitTest(point);
    }

    /// <summary>
    /// Returns the current viewport height in DIPs, accounting for border insets and padding.
    /// Returns 0 when the control has not been arranged.
    /// </summary>
    protected double GetViewportHeightDip()
    {
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            var snapped = GetSnappedBorderBounds(Bounds);
            var borderInset = GetBorderVisualInset();
            var innerBounds = snapped.Deflate(new Thickness(borderInset));
            var dpiScale = GetDpi() / 96.0;
            return LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Height - Padding.VerticalThickness), dpiScale);
        }

        return 0;
    }
}
