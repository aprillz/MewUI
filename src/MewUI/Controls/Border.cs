using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// WPF-like decorator that draws background/border and hosts a single child element.
/// </summary>
public sealed class Border : Control, IVisualTreeHost
{
    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (Child != null)
        {
            var hit = Child.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return base.OnHitTest(point);
    }

    public double CornerRadius
    {
        get;
        set
        {
            if (field.Equals(value))
            {
                return;
            }

            field = value;
            InvalidateVisual();
        }
    }

    public UIElement? Child
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            if (field != null)
            {
                field.Parent = null;
            }

            field = value;
            if (field != null)
            {
                field.Parent = this;
            }

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var slot = availableSize.Deflate(border).Deflate(Padding);

        if (Child == null)
        {
            return new Size(0, 0).Inflate(Padding).Inflate(border);
        }

        Child.Measure(slot);
        return Child.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var inner = bounds.Deflate(border).Deflate(Padding);
        Child?.Arrange(inner);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        
        var radius = Math.Max(0, CornerRadius);
        DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, radius);

        if (Child != null)
        {
            Child.Render(context);
        }
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        if (Child != null)
        {
            visitor(Child);
        }
    }
}
