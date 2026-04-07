using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// WPF-like decorator that draws background/border and hosts a single child element.
/// </summary>
public sealed class Border : Control, IVisualTreeHost
{
    public static readonly MewProperty<Thickness> NonUniformBorderThicknessProperty =
        MewProperty<Thickness>.Register<Border>(nameof(NonUniformBorderThickness), default,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<CornerRadius> NonUniformCornerRadiusProperty =
        MewProperty<CornerRadius>.Register<Border>(nameof(NonUniformCornerRadius), default, MewPropertyOptions.AffectsRender);

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

    /// <summary>
    /// Gets or sets the per-side border thickness. When set (non-zero), overrides the
    /// uniform <see cref="Control.BorderThickness"/> inherited from Control.
    /// </summary>
    public Thickness NonUniformBorderThickness
    {
        get => GetValue(NonUniformBorderThicknessProperty);
        set => SetValue(NonUniformBorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the per-corner radius. When set (non-zero), overrides the
    /// uniform <see cref="Control.CornerRadius"/> inherited from Control.
    /// </summary>
    public CornerRadius NonUniformCornerRadius
    {
        get => GetValue(NonUniformCornerRadiusProperty);
        set => SetValue(NonUniformCornerRadiusProperty, value);
    }

    private Thickness EffectiveBorderThickness
    {
        get
        {
            var s = NonUniformBorderThickness;
            return s != Thickness.Zero ? s : new Thickness(BorderThickness);
        }
    }

    private CornerRadius EffectiveCornerRadius
    {
        get
        {
            var s = NonUniformCornerRadius;
            return s != MewUI.CornerRadius.Zero ? s : new CornerRadius(CornerRadius);
        }
    }

    public bool ClipToBounds
    {
        get => GetValue(ClipToBoundsProperty);
        set => SetValue(ClipToBoundsProperty, value);
    }

    private BorderRenderMetrics CreateMetrics(Rect bounds)
        => CreateBorderRenderMetrics(bounds, GetDpi() / 96.0, EffectiveBorderThickness, EffectiveCornerRadius);

    protected override Size MeasureContent(Size availableSize)
    {
        var border = EffectiveBorderThickness;
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
        var border = EffectiveBorderThickness;
        var inner = snapped.Deflate(border).Deflate(Padding);
        Child?.Arrange(inner);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bg = Background;
        var borderBrush = BorderBrush;
        var metrics = CreateMetrics(Bounds);

        if (metrics.IsSimple)
        {
            // Background only — border is drawn after subtree in RenderSubtree.
            if (bg.A == 0) return;
            var bounds = metrics.Bounds;
            var radius = metrics.UniformRadius;

            if (radius > 0)
                context.FillRoundedRectangle(bounds, radius, radius, bg);
            else
                context.FillRectangle(bounds, bg);
        }
        else
        {
            // Non-uniform: border first (outer fill), then background on top (inner fill).
            // Border color extends under background — no seam at boundary.
            if (borderBrush.A > 0 && metrics.BorderThickness != Thickness.Zero)
            {
                var outerPath = BorderGeometry.CreateOuterContour(in metrics);
                if (!outerPath.IsEmpty)
                    context.FillPath(outerPath, borderBrush);
            }

            if (bg.A > 0)
            {
                var bgPath = BorderGeometry.CreateBackgroundRegion(in metrics);
                if (!bgPath.IsEmpty)
                    context.FillPath(bgPath, bg);
            }
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var metrics = CreateMetrics(Bounds);

        if (Child != null)
        {
            if (ClipToBounds)
            {
                context.Save();

                if (metrics.IsSimple)
                {
                    var bt = metrics.UniformThickness;
                    var clipRect = bt > 0
                        ? new Rect(metrics.Bounds.X + bt, metrics.Bounds.Y + bt,
                            Math.Max(0, metrics.Bounds.Width - bt * 2),
                            Math.Max(0, metrics.Bounds.Height - bt * 2))
                        : metrics.Bounds;
                    clipRect = clipRect.Deflate(Padding);

                    if (metrics.UniformRadius > 0)
                    {
                        var clipRadius = metrics.UniformInnerRadius;
                        context.SetClipRoundedRect(clipRect, clipRadius, clipRadius);
                    }
                    else
                    {
                        context.SetClip(clipRect);
                    }
                }
                else
                {
                    var clipRect = metrics.InnerBounds.Deflate(Padding);
                    double minRX = Math.Min(
                        Math.Min(metrics.InnerTopLeftX, metrics.InnerTopRightX),
                        Math.Min(metrics.InnerBottomRightX, metrics.InnerBottomLeftX));
                    double minRY = Math.Min(
                        Math.Min(metrics.InnerTopLeftY, metrics.InnerTopRightY),
                        Math.Min(metrics.InnerBottomRightY, metrics.InnerBottomLeftY));

                    if (minRX > 0 || minRY > 0)
                        context.SetClipRoundedRect(clipRect, minRX, minRY);
                    else
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

        // Simple case: border stroke drawn after child (on top).
        // Non-uniform case: already painted in OnRender (border under background under child).
        if (metrics.IsSimple)
        {
            var borderBrush = BorderBrush;
            if (metrics.UniformThickness > 0 && borderBrush.A > 0)
            {
                var bounds = metrics.Bounds;
                var radius = metrics.UniformRadius;
                var bt = metrics.UniformThickness;

                if (radius > 0)
                    context.DrawRoundedRectangle(bounds, radius, radius, borderBrush, bt, strokeInset: true);
                else
                    context.DrawRectangle(bounds, borderBrush, bt, strokeInset: true);
            }
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
