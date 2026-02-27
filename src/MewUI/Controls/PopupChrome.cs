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

        var dpiScale = GetDpi() / 96.0;
        double radius = LayoutRounding.RoundToPixel(Theme.Metrics.ControlCornerRadius, dpiScale);
        int strength = Theme.IsDark ? 128 : 64;

        var shadowBounds = new Rect(cb.X, cb.Y + ShadowOffsetY, cb.Width, cb.Height - ShadowOffsetY);
        context.DrawBoxShadow(shadowBounds, radius, ShadowBlurRadius,
            Color.FromArgb((byte)strength, 0, 0, 0));
    }

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        OnRender(context);
        _child.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
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
