using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies a supported quarter-turn rotation.
/// </summary>
public enum Rotation
{
    /// <summary>Rotates the child 90 degrees counter-clockwise.</summary>
    CounterClockwise90,

    /// <summary>Rotates the child 90 degrees clockwise.</summary>
    Clockwise90,
}

/// <summary>
/// Hosts a single child rotated clockwise or counter-clockwise by 90 degrees.
/// </summary>
public sealed class RotationDecorator : FrameworkElement, IVisualTreeHost
{
    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<RotationDecorator>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue));

    public static readonly MewProperty<Rotation> RotationProperty =
        MewProperty<Rotation>.Register<RotationDecorator>(nameof(Rotation), Rotation.Clockwise90,
            MewPropertyOptions.AffectsRender);

    /// <summary>Gets or sets the child element.</summary>
    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>Gets or sets the direction of the 90-degree rotation.</summary>
    public Rotation Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    private double Angle => Rotation == Rotation.CounterClockwise90 ? -Math.PI / 2 : Math.PI / 2;

    private void OnChildChanged(UIElement? oldValue, UIElement? newValue)
    {
        if (ReferenceEquals(newValue, this))
        {
            throw new InvalidOperationException("Cannot set Child to self.");
        }

        if (oldValue != null)
        {
            oldValue.Parent = null;
        }

        if (newValue != null)
        {
            newValue.Parent = this;
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Child == null)
        {
            return Size.Empty;
        }

        Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = Child.DesiredSize;
        return new Size(desired.Height, desired.Width);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Child == null)
        {
            return;
        }

        var desired = Child.DesiredSize;
        double centerX = bounds.X + bounds.Width / 2;
        double centerY = bounds.Y + bounds.Height / 2;
        Child.Arrange(new Rect(
            centerX - desired.Width / 2,
            centerY - desired.Height / 2,
            desired.Width,
            desired.Height));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (Child == null)
        {
            return;
        }

        double centerX = Bounds.X + Bounds.Width / 2;
        double centerY = Bounds.Y + Bounds.Height / 2;

        context.Save();
        context.Translate(centerX, centerY);
        context.Rotate(Angle);
        context.Translate(-centerX, -centerY);
        Child.Render(context);
        context.Restore();
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || !Bounds.Contains(point))
        {
            return null;
        }

        var center = new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
        var mapped = RotatePoint(point, center, -Angle);
        return Child?.HitTest(mapped) ?? this;
    }

    private static Point RotatePoint(Point point, Point center, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
