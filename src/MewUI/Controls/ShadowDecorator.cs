using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A decorator that draws a drop shadow behind its child element.
/// The shadow is rendered within the decorator's own layout bounds -
/// use <see cref="ShadowPadding"/> to reserve space around the child.
/// </summary>
public sealed class ShadowDecorator : FrameworkElement, IVisualTreeHost
{
    public static readonly MewProperty<double> BlurRadiusProperty =
        MewProperty<double>.Register<ShadowDecorator>(nameof(BlurRadius), 8.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> OffsetYProperty =
        MewProperty<double>.Register<ShadowDecorator>(nameof(OffsetY), 2.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color> ShadowColorProperty =
        MewProperty<Color>.Register<ShadowDecorator>(nameof(ShadowColor), Color.FromArgb(48, 0, 0, 0),
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> CornerRadiusProperty =
        MewProperty<double>.Register<ShadowDecorator>(nameof(CornerRadius), 0.0,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<ShadowDecorator>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) =>
            {
                if (oldValue != null) oldValue.Parent = null;
                if (newValue != null) newValue.Parent = self;
            });

    /// <summary>Gets or sets the blur radius of the shadow.</summary>
    public double BlurRadius
    {
        get => GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    /// <summary>Gets or sets the vertical offset of the shadow.</summary>
    public double OffsetY
    {
        get => GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>Gets or sets the shadow color (including alpha for intensity).</summary>
    public Color ShadowColor
    {
        get => GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    /// <summary>Gets or sets the corner radius of the shadow shape.</summary>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the child element.</summary>
    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>
    /// Gets the padding needed around the child to accommodate the shadow extent.
    /// </summary>
    public Thickness ShadowPadding
    {
        get
        {
            double blur = Math.Max(0, BlurRadius);
            double oy = OffsetY;
            return new Thickness(
                blur,
                Math.Max(0, blur - oy),
                blur,
                Math.Max(0, blur + oy));
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Child == null) return Size.Empty;

        var pad = ShadowPadding;
        var inner = availableSize.Deflate(pad);
        Child.Measure(inner);
        return Child.DesiredSize.Inflate(pad);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        Child?.Arrange(bounds.Deflate(ShadowPadding));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (Child == null) return;

        var cb = Child.Bounds;
        if (cb.Width <= 0 || cb.Height <= 0) return;

        var color = ShadowColor;
        if (color.A == 0) return;

        double blur = Math.Max(0, BlurRadius);
        double oy = OffsetY;
        double radius = Math.Max(0, CornerRadius);

        context.DrawBoxShadow(cb, radius, blur, color, offsetY: oy);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        Child?.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled) return null;
        return Child?.HitTest(point);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child != null && visitor(Child);
}
