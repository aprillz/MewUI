using System.Numerics;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Abstract base class for shape elements that render a <see cref="PathGeometry"/>.
/// </summary>
public abstract class Shape : FrameworkElement
{
    /// <summary>The brush that fills the shape interior; null draws no fill.</summary>
    public static readonly MewProperty<Brush?> FillProperty =
        MewProperty<Brush?>.Register<Shape>(nameof(Fill), null, MewPropertyOptions.AffectsRender);

    /// <summary>The brush that strokes the shape outline; null draws no outline.</summary>
    public static readonly MewProperty<Brush?> StrokeProperty =
        MewProperty<Brush?>.Register<Shape>(nameof(Stroke), null, MewPropertyOptions.AffectsRender);

    /// <summary>The stroke thickness in DIPs.</summary>
    public static readonly MewProperty<double> StrokeThicknessProperty =
        MewProperty<double>.Register<Shape>(nameof(StrokeThickness), 0.0, MewPropertyOptions.AffectsLayout);

    /// <summary>The stroke style (line cap, line join, dash pattern).</summary>
    public static readonly MewProperty<StrokeStyle> StrokeStyleProperty =
        MewProperty<StrokeStyle>.Register<Shape>(nameof(StrokeStyle), default, MewPropertyOptions.AffectsRender);

    /// <summary>How the geometry is stretched to fill the available space; the default is <see cref="Stretch.None"/>.</summary>
    public static readonly MewProperty<Stretch> StretchProperty =
        MewProperty<Stretch>.Register<Shape>(nameof(Stretch), Stretch.None, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// The source rectangle a stretch maps onto the element, in the geometry's own coordinates; null uses the geometry's ink bounds.
    /// </summary>
    public static readonly MewProperty<Rect?> ViewBoxProperty =
        MewProperty<Rect?>.Register<Shape>(nameof(ViewBox), null, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets the brush used to fill the shape interior; null draws no fill.
    /// </summary>
    public Brush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to stroke the shape outline; null draws no outline.
    /// </summary>
    public Brush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness in DIPs.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke style (line cap, line join, dash pattern).
    /// </summary>
    public StrokeStyle StrokeStyle
    {
        get => GetValue(StrokeStyleProperty);
        set => SetValue(StrokeStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets how the geometry is stretched to fill the available space; the default is <see cref="Stretch.None"/>.
    /// </summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the source rectangle a stretch maps onto the element, in the geometry's own
    /// coordinates; null uses the geometry's ink bounds.
    /// </summary>
    /// <remarks>
    /// Icons drawn on a design grid set it to that grid, so the margin the grid leaves around the ink
    /// survives: stretching to the ink bounds instead scales each icon by however tightly it happens to
    /// be drawn, and a row of them comes out at different optical weights.
    /// </remarks>
    public Rect? ViewBox
    {
        get => GetValue(ViewBoxProperty);
        set => SetValue(ViewBoxProperty, value);
    }

    private Pen? _cachedPen;
    private Brush? _cachedPenBrush;
    private double _cachedPenThickness;
    private StrokeStyle _cachedPenStyle;

    /// <summary>
    /// When overridden, returns the <see cref="PathGeometry"/> that defines this shape; null draws nothing.
    /// </summary>
    protected abstract PathGeometry? GetDefiningGeometry();

    /// <summary>Gets a copy of the geometry after applying the shape's layout and stretch, or null when the shape has no geometry or no size.</summary>
    public PathGeometry? RenderedGeometry
    {
        get
        {
            PathGeometry? geometry = GetRenderedGeometry();
            return geometry == null ? null : CopyGeometry(geometry);
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureContent(Size availableSize)
    {
        var geometry = GetDefiningGeometry();
        if (geometry == null || geometry.IsEmpty)
            return Size.Empty;

        if (Stretch == Stretch.None)
        {
            var geoBounds = geometry.GetBounds();
            double st = (Stroke != null && StrokeThickness > 0) ? StrokeThickness : 0;
            return new Size(geoBounds.Right + st, geoBounds.Bottom + st);
        }

        return Size.Empty;
    }

    /// <inheritdoc/>
    protected override void OnRender(IGraphicsContext context)
    {
        PathGeometry? renderGeometry = GetRenderedGeometry();
        if (renderGeometry == null) return;
        if (Fill == null && (Stroke == null || StrokeThickness <= 0)) return;

        if (Fill != null)
            context.FillPath(renderGeometry, Fill);

        if (Stroke != null && StrokeThickness > 0)
        {
            var stroke = Stroke;
            var thickness = StrokeThickness;
            var style = StrokeStyle;

            if (_cachedPen == null ||
                !ReferenceEquals(_cachedPenBrush, stroke) ||
                _cachedPenThickness != thickness ||
                _cachedPenStyle != style)
            {
                _cachedPen = new Pen(stroke, thickness, style);
                _cachedPenBrush = stroke;
                _cachedPenThickness = thickness;
                _cachedPenStyle = style;
            }

            context.DrawPath(renderGeometry, _cachedPen);
        }
    }

    /// <inheritdoc/>
    protected override UIElement? OnHitTest(Point point)
    {
        UIElement? hit = base.OnHitTest(point);
        if (!ReferenceEquals(hit, this))
        {
            return hit;
        }

        PathGeometry? renderGeometry = GetRenderedGeometry();
        ShapeHitTestResult result = ShapeHitTesting.HitTest(this, renderGeometry, point);
        if (result == ShapeHitTestResult.Miss)
        {
            return null;
        }

        return this;
    }

    private PathGeometry? GetRenderedGeometry()
    {
        PathGeometry? geometry = GetDefiningGeometry();
        if (geometry == null || geometry.IsEmpty)
        {
            return null;
        }

        Rect bounds = Bounds;
        if (bounds.Width <= 0 && bounds.Height <= 0)
        {
            return null;
        }

        // The declared grid wins over the ink: two icons drawn on the same grid then scale by the same
        // factor whatever margin each one leaves.
        Rect geoBounds = ViewBox ?? geometry.GetBounds();

        // Match WPF's Path/Shape semantics: Stretch operates on the geometry (so its baked
        // path coordinates fit the element bounds), while Stroke is applied at the Path
        // level on top of that - meaning StrokeThickness stays in element-DIP regardless
        // of the stretch factor. We achieve this by baking the stretch into a transformed
        // geometry instead of pushing a Scale transform onto the context (which, under the
        // Model D scale-with-transform stroke contract, would also scale the stroke).
        if (Stretch != Stretch.None && geoBounds.Width > 0 && geoBounds.Height > 0)
        {
            ComputeStretchTransform(geoBounds, bounds, Stretch,
                out double scaleX, out double scaleY, out double offsetX, out double offsetY);
            // [Translate(offset)] × [Scale] × [Translate(-geoOrigin)]
            var bake =
                Matrix3x2.CreateTranslation((float)-geoBounds.X, (float)-geoBounds.Y) *
                Matrix3x2.CreateScale((float)scaleX, (float)scaleY) *
                Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY);
            return geometry.Transform(bake);
        }

        var translation = Matrix3x2.CreateTranslation(
            (float)(bounds.X - geoBounds.X), (float)(bounds.Y - geoBounds.Y));
        return geometry.Transform(translation);
    }

    private static PathGeometry CopyGeometry(PathGeometry geometry)
    {
        var copy = new PathGeometry { FillRule = geometry.FillRule };
        foreach (PathCommand command in geometry.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    copy.MoveTo(command.X0, command.Y0);
                    break;
                case PathCommandType.LineTo:
                    copy.LineTo(command.X0, command.Y0);
                    break;
                case PathCommandType.BezierTo:
                    copy.BezierTo(command.X0, command.Y0, command.X1, command.Y1, command.X2, command.Y2);
                    break;
                case PathCommandType.Close:
                    copy.Close();
                    break;
            }
        }

        return copy;
    }

    private static void ComputeStretchTransform(
        Rect geoBounds, Rect destBounds, Stretch stretch,
        out double scaleX, out double scaleY, out double offsetX, out double offsetY)
    {
        double gw = geoBounds.Width, gh = geoBounds.Height;
        double dw = destBounds.Width, dh = destBounds.Height;

        switch (stretch)
        {
            case Stretch.Fill:
                scaleX = dw / gw;
                scaleY = dh / gh;
                break;
            case Stretch.Uniform:
            {
                double scale = Math.Min(dw / gw, dh / gh);
                scaleX = scaleY = scale;
                break;
            }
            case Stretch.UniformToFill:
            {
                double scale = Math.Max(dw / gw, dh / gh);
                scaleX = scaleY = scale;
                break;
            }
            default:
                scaleX = scaleY = 1.0;
                break;
        }

        // Center the scaled geometry within the destination bounds.
        double scaledW = gw * scaleX, scaledH = gh * scaleY;
        offsetX = destBounds.X + (dw - scaledW) * 0.5;
        offsetY = destBounds.Y + (dh - scaledH) * 0.5;
    }
}
