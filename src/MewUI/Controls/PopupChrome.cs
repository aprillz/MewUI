using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Wraps a popup element with a drop-shadow chrome layer.
/// PopupManager creates one of these around every popup element so that
/// the shadow is rendered within the chrome's own layout bounds, avoiding
/// unintended clipping by ancestor clip regions.
/// </summary>
internal sealed class PopupChrome : FrameworkElement, IVisualTreeHost
{
    internal const double ShadowBlurRadius = 8;
    internal const double ShadowOffsetY = 4;

    internal static readonly MewProperty<double> ShadowOpacityProperty =
        MewProperty<double>.Register<PopupChrome>(nameof(ShadowOpacity), 1.0, MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Extra space around the child that the shadow occupies.
    /// Left/Right = BlurRadius, Top = BlurRadius - OffsetY, Bottom = BlurRadius.
    /// </summary>
    internal static readonly Thickness ShadowPadding = new(
        ShadowBlurRadius,
        ShadowBlurRadius - ShadowOffsetY,
        ShadowBlurRadius,
        ShadowBlurRadius);

    private readonly UIElement _child;

    internal PopupChrome(UIElement child)
    {
        _child = child;
    }

    internal UIElement Child => _child;

    /// <summary>
    /// The popup window rendering this chrome when it is native-hosted (portal model). Invalidation of the
    /// popup subtree bubbles through Parent to the owner window, so it is forwarded here to also wake the
    /// popup window's render/layout pass. Null for in-surface popups.
    /// </summary>
    internal Window? HostSurface { get; set; }

    internal override Window? HostedPopupSurface => HostSurface;

    /// <inheritdoc/>
    public override void InvalidateVisual()
    {
        base.InvalidateVisual();
        HostSurface?.InvalidateVisual();
    }

    /// <inheritdoc/>
    public override void InvalidateMeasure()
    {
        base.InvalidateMeasure();
        HostSurface?.InvalidateMeasure();
    }

    /// <inheritdoc/>
    public override void InvalidateArrange()
    {
        base.InvalidateArrange();
        // Arrange-only invalidation (e.g. a scroll offset change) bubbles up to here; forward it so the
        // hosting popup window runs a layout pass, not just a repaint - otherwise the content re-paints
        // at its old arrangement (the scroll bar moves but the scrolled content does not).
        HostSurface?.InvalidateArrange();
    }

    private double ShadowOpacity => GetValue(ShadowOpacityProperty);

    protected override Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(ShadowPadding);
        _child.Measure(inner);
        return _child.DesiredSize.Inflate(ShadowPadding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _child.Arrange(bounds.Deflate(ShadowPadding));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var cb = _child.Bounds;
        if (cb.Width <= 0 || cb.Height <= 0)
        {
            return;
        }

        double opacity = ShadowOpacity;
        if (opacity <= 0)
        {
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        double radius = LayoutRounding.RoundToPixel(Theme.Metrics.ControlCornerRadius, dpiScale);
        int strength = Theme.IsDark ? 128 : 64;
        byte alpha = (byte)(strength * opacity);

        var shadowBounds = new Rect(cb.X, cb.Y + ShadowOffsetY, cb.Width, cb.Height - ShadowOffsetY);
        context.DrawBoxShadow(shadowBounds, radius, ShadowBlurRadius,
            Color.FromArgb(alpha, 0, 0, 0));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        _child.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        return _child.HitTest(point);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_child);

    internal void AttachChild()
    {
        _child.Parent = this;
    }

    internal void DetachChild()
    {
        _child.Parent = null;
    }
}
