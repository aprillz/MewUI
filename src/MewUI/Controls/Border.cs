using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// WPF-like decorator that draws background/border and hosts a single child element.
/// </summary>
public sealed partial class Border : Control, IVisualTreeHost, ILogicalTreeHost
{
    private const int BACKGROUND_SLOT = 0;
    private const int BORDER_STROKE_SLOT = 1;

    static Border() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Border>(DefaultStyles.CreateBorderStyle);

    private PathGeometry? _cachedBorderPath;
    private PathGeometry? _cachedBgPath;
    private BorderGeometryCacheKey _cachedBorderKey;
    private BorderGeometryCacheKey _cachedBgKey;

    // Frozen-geometry cache key: the generated contours depend only on these
    // inputs. Freezing the regenerated paths lets the backend reuse its
    // per-geometry fill caches across frames instead of re-tessellating.
    private readonly record struct BorderGeometryCacheKey(
        Rect Bounds,
        double DpiScale,
        Thickness BorderThickness,
        CornerRadius CornerRadius,
        bool RingHides);

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
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue),
            validate: static (self, value) => self.ValidateLogicalChild(value, allowTransfer: true));

    public UIElement? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    private void OnChildChanged(UIElement? oldValue, UIElement? newValue)
        => ChangeLogicalChild(oldValue, newValue);

    protected override void OnLogicalChildTaken(Element child)
    {
        base.OnLogicalChildTaken(child);

        if (ReferenceEquals(Child, child))
        {
            Child = null;
        }
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
            // Background only - border is drawn after subtree in RenderSubtree.
            if (bg.A == 0)
            {
                return;
            }

            var bounds = metrics.Bounds;
            var radius = metrics.UniformRadius;

            // The background stops at the stroke's centre line, the way WPF draws its simple
            // border: filled to the outer contour it would sit under the stroke's outer
            // antialiased fringe and bleed past the border.
            if (metrics.UniformThickness > 0 && borderBrush.A > 0)
            {
                double inset = metrics.UniformThickness / 2;
                bounds = bounds.Inflate(-inset, -inset);
                radius = Math.Max(0, radius - inset);
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            if (radius > 0)
            {
                context.FillRoundedRectangle(bounds, radius, radius, bg);
            }
            else
            {
                context.FillRectangle(bounds, bg);
            }
        }
        else
        {
            // Non-uniform: background first, then the border ring on top. The ring carries its own
            // hole, so a transparent background leaves the middle empty instead of filled with the
            // border colour. Where an opaque ring is about to cover it, the background runs to the
            // outer contour so their shared edge has no antialiased seam; anywhere else it stops at
            // the inner contour, which is the only area it may paint.
            bool ringHides = borderBrush.A == 255 && metrics.BorderThickness != Thickness.Zero;
            if (bg.A > 0)
            {
                var key = new BorderGeometryCacheKey(
                    metrics.Bounds, metrics.DpiScale, metrics.BorderThickness, metrics.CornerRadius, ringHides);
                if (_cachedBgPath == null || _cachedBgKey != key)
                {
                    var path = new PathGeometry();
                    if (ringHides)
                    {
                        BorderGeometry.GenerateOuterContour(path, in metrics);
                    }
                    else
                    {
                        BorderGeometry.GenerateBackgroundRegion(path, in metrics);
                    }

                    path.Freeze();
                    _cachedBgPath = path;
                    _cachedBgKey = key;
                }

                if (!_cachedBgPath.IsEmpty)
                {
                    context.FillPath(_cachedBgPath, bg);
                }
            }

            if (borderBrush.A > 0 && metrics.BorderThickness != Thickness.Zero)
            {
                var key = new BorderGeometryCacheKey(
                    metrics.Bounds, metrics.DpiScale, metrics.BorderThickness, metrics.CornerRadius, RingHides: false);
                if (_cachedBorderPath == null || _cachedBorderKey != key)
                {
                    var path = new PathGeometry();
                    BorderGeometry.GenerateBorderRegion(path, in metrics);
                    path.Freeze();
                    _cachedBorderPath = path;
                    _cachedBorderKey = key;
                }

                if (!_cachedBorderPath.IsEmpty)
                {
                    context.FillPath(_cachedBorderPath, borderBrush);
                }
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
                GetChildClip(in metrics, out var clipRect, out double radiusX, out double radiusY);
                if (radiusX > 0 || radiusY > 0)
                {
                    context.SetClipRoundedRect(clipRect, radiusX, radiusY);
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

        RenderBorderStroke(context, in metrics);
    }

    private void GetChildClip(in BorderRenderMetrics metrics, out Rect clipRect, out double radiusX, out double radiusY)
    {
        if (metrics.IsSimple)
        {
            var borderThickness = metrics.UniformThickness;
            clipRect = borderThickness > 0
                ? new Rect(metrics.Bounds.X + borderThickness, metrics.Bounds.Y + borderThickness,
                    Math.Max(0, metrics.Bounds.Width - borderThickness * 2),
                    Math.Max(0, metrics.Bounds.Height - borderThickness * 2))
                : metrics.Bounds;
            clipRect = clipRect.Deflate(Padding);
            radiusX = metrics.UniformRadius > 0 ? metrics.UniformInnerRadius : 0;
            radiusY = radiusX;
        }
        else
        {
            clipRect = metrics.InnerBounds.Deflate(Padding);
            radiusX = Math.Min(
                Math.Min(metrics.InnerTopLeftX, metrics.InnerTopRightX),
                Math.Min(metrics.InnerBottomRightX, metrics.InnerBottomLeftX));
            radiusY = Math.Min(
                Math.Min(metrics.InnerTopLeftY, metrics.InnerTopRightY),
                Math.Min(metrics.InnerBottomRightY, metrics.InnerBottomLeftY));
        }
    }

    private void RenderBorderStroke(IGraphicsContext context, in BorderRenderMetrics metrics)
    {
        // The non-uniform case paints its ring in OnRender, under the background and the child.
        if (!metrics.IsSimple)
        {
            return;
        }

        var borderBrush = BorderBrush;
        if (metrics.UniformThickness > 0 && borderBrush.A > 0)
        {
            var bounds = metrics.Bounds;
            var radius = metrics.UniformRadius;
            var thickness = metrics.UniformThickness;

            if (radius > 0)
            {
                context.DrawRoundedRectangle(bounds, radius, radius, borderBrush, thickness, strokeInset: true);
            }
            else
            {
                context.DrawRectangle(bounds, borderBrush, thickness, strokeInset: true);
            }
        }
    }

    internal override void WriteOwnContent(IGraphicsContext context, int slotIndex)
    {
        if (slotIndex == BORDER_STROKE_SLOT)
        {
            var metrics = CreateMetrics(Bounds);
            RenderBorderStroke(context, in metrics);
        }
        else
        {
            OnRender(context);
        }
    }

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(BACKGROUND_SLOT);

        if (Child != null)
        {
            var metrics = CreateMetrics(Bounds);
            if (ClipToBounds)
            {
                GetChildClip(in metrics, out var clipRect, out double radiusX, out double radiusY);
                if (radiusX > 0 || radiusY > 0)
                {
                    builder.PushClipRoundedRect(clipRect, radiusX, radiusY);
                }
                else
                {
                    builder.PushClipRect(clipRect);
                }

                builder.Child(Child);
                builder.Pop();
            }
            else
            {
                builder.Child(Child);
            }
        }

        builder.Content(BORDER_STROKE_SLOT);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);

    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor)
        => Child == null || visitor(Child);
}
