using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// WPF-like decorator that draws background/border and hosts a single child element.
/// </summary>
public sealed class Border : Control, IVisualTreeHost
{
    private UIElement? _child;

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (_child != null)
        {
            var hit = _child.HitTest(point);
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
        get => _child;
        set
        {
            if (_child == value)
            {
                return;
            }

            if (_child != null)
            {
                _child.Parent = null;
            }

            _child = value;
            if (_child != null)
            {
                _child.Parent = this;
            }

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var slot = availableSize.Deflate(border).Deflate(Padding);

        if (_child == null)
        {
            return new Size(0, 0).Inflate(Padding).Inflate(border);
        }

        _child.Measure(slot);
        return _child.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var inner = bounds.Deflate(border).Deflate(Padding);
        _child?.Arrange(inner);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var radius = Math.Max(0, CornerRadius);
        DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, radius);

        if (_child != null)
        {
            _child.Render(context);
        }
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        if (_child != null)
        {
            visitor(_child);
        }
    }
}
