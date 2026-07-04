namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for scrollable items controls: hosts a <see cref="ScrollViewer"/> over an
/// <see cref="IItemsPresenter"/> and provides item-binding generation, deferred scroll-into-view,
/// a pending tab-focus helper, and shared list chrome. Virtualization is a presenter concern, not this base.
/// </summary>
public abstract class ScrollableItemsBase : Control, ISubtreeInvalidationHost
{
    protected readonly ScrollViewer _scrollViewer;

    /// <summary>
    /// Set by derived class constructor (after the presenter is created).
    /// </summary>
    private protected PendingTabFocusHelper _tabFocusHelper = null!;

    private protected uint _itemBindingGeneration;
    private ScrollIntoViewRequest _scrollIntoViewRequest;

    protected ScrollableItemsBase()
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

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        InvalidateItemBindings(invalidateMeasure: false);
        InvalidateVisual();
    }

    private protected uint ItemBindingGeneration => _itemBindingGeneration;

    private protected void InvalidateItemBindings(bool invalidateMeasure = true)
    {
        unchecked { _itemBindingGeneration++; }
        if (invalidateMeasure)
        {
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => VisitScrollChildren(visitor);

    private protected void RequestScrollIntoView(ScrollIntoViewRequest request)
        => _scrollIntoViewRequest = request;

    private protected bool TryConsumeScrollIntoViewRequest(out ScrollIntoViewRequest request)
    {
        if (_scrollIntoViewRequest.IsNone)
        {
            request = default;
            return false;
        }

        request = _scrollIntoViewRequest;
        _scrollIntoViewRequest.Clear();
        return true;
    }

    /// <summary>
    /// Override to visit additional children (e.g. a header row) before or after the scroll viewer.
    /// </summary>
    protected virtual bool VisitScrollChildren(Func<Element, bool> visitor) => visitor(_scrollViewer);

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
    /// Resolves the foreground color for an item, applying the correct priority:
    /// disabled state > selection state > default Foreground.
    /// </summary>
    protected Color ResolveItemForeground(bool selected)
        => !IsEffectivelyEnabled
            ? Theme.Palette.DisabledText
            : selected ? Theme.Palette.SelectionText : Theme.Palette.WindowText;

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
