using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// WPF-like decorator that draws background/border and hosts a single child element.
/// </summary>
public sealed class Border : Control, IVisualTreeHost
{
    public static readonly MewProperty<bool> ClipToBoundsProperty =
        MewProperty<bool>.Register<Border>(nameof(ClipToBounds), false, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<Border>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue));

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

    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    private void OnChildChanged(UIElement? oldValue, UIElement? newValue)
    {
        if (oldValue != null) oldValue.Parent = null;
        if (newValue != null) newValue.Parent = this;
    }

    public bool ClipToBounds
    {
        get => GetValue(ClipToBoundsProperty);
        set => SetValue(ClipToBoundsProperty, value);
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
        var snapped = GetSnappedBorderBounds(bounds);
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var inner = snapped.Deflate(border).Deflate(Padding);
        Child?.Arrange(inner);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, Math.Max(0, CornerRadius));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (Child == null) return;

        if (ClipToBounds)
        {
            // Use the same pixel-snapped metrics as OnRender to ensure clip aligns with drawn border.
            var metrics = GetBorderRenderMetrics(Bounds, Math.Max(0, CornerRadius));
            var bt = metrics.BorderThickness;
            var clipRect = bt > 0
                ? new Rect(metrics.Bounds.X + bt, metrics.Bounds.Y + bt,
                    Math.Max(0, metrics.Bounds.Width - bt * 2),
                    Math.Max(0, metrics.Bounds.Height - bt * 2))
                : metrics.Bounds;
            clipRect = clipRect.Deflate(Padding);

            context.Save();
            if (metrics.CornerRadius > 0)
            {
                var clipRadius = Math.Max(0, metrics.CornerRadius - bt);
                context.SetClipRoundedRect(clipRect, clipRadius, clipRadius);
            }
            else
            {
                context.SetClip(clipRect);
            }

            Child.Render(context);
            context.Restore();
        }
        else
        {
            Child.Render(context);
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
