using System.Numerics;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Abstract base class for shape elements that render a <see cref="PathGeometry"/>.
/// </summary>
public abstract class Shape : FrameworkElement
{
    public static readonly MewProperty<Brush?> FillProperty =
        MewProperty<Brush?>.Register<Shape>(nameof(Fill), null, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Brush?> StrokeProperty =
        MewProperty<Brush?>.Register<Shape>(nameof(Stroke), null, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> StrokeThicknessProperty =
        MewProperty<double>.Register<Shape>(nameof(StrokeThickness), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<StrokeStyle> StrokeStyleProperty =
        MewProperty<StrokeStyle>.Register<Shape>(nameof(StrokeStyle), default, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Stretch> StretchProperty =
        MewProperty<Stretch>.Register<Shape>(nameof(Stretch), Stretch.None, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets the brush used to fill the shape interior.
    /// </summary>
    public Brush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to stroke the shape outline.
    /// </summary>
    public Brush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness in device-independent pixels.
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
    /// Gets or sets how the geometry is stretched to fill the available space.
    /// </summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    private Pen? _cachedPen;
    private Brush? _cachedPenBrush;
    private double _cachedPenThickness;
    private StrokeStyle _cachedPenStyle;

    /// <summary>
    /// When overridden, returns the <see cref="PathGeometry"/> that defines this shape.
    /// </summary>
    protected abstract PathGeometry? GetDefiningGeometry();

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
        var geometry = GetDefiningGeometry();
        if (geometry == null || geometry.IsEmpty) return;
        if (Fill == null && (Stroke == null || StrokeThickness <= 0)) return;

        var bounds = Bounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return;

        var geoBounds = geometry.GetBounds();

        // Match WPF's Path/Shape semantics: Stretch operates on the geometry (so its baked
        // path coordinates fit the element bounds), while Stroke is applied at the Path
        // level on top of that - meaning StrokeThickness stays in element-DIP regardless
        // of the stretch factor. We achieve this by baking the stretch into a transformed
        // geometry instead of pushing a Scale transform onto the context (which, under the
        // Model D scale-with-transform stroke contract, would also scale the stroke).
        PathGeometry renderGeometry;
        if (Stretch != Stretch.None && geoBounds.Width > 0 && geoBounds.Height > 0)
        {
            ComputeStretchTransform(geoBounds, bounds, Stretch,
                out double scaleX, out double scaleY, out double offsetX, out double offsetY);
            // [Translate(offset)] × [Scale] × [Translate(-geoOrigin)]
            var bake =
                Matrix3x2.CreateTranslation((float)-geoBounds.X, (float)-geoBounds.Y) *
                Matrix3x2.CreateScale((float)scaleX, (float)scaleY) *
                Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY);
            renderGeometry = geometry.Transform(bake);
        }
        else
        {
            var bake = Matrix3x2.CreateTranslation(
                (float)(bounds.X - geoBounds.X), (float)(bounds.Y - geoBounds.Y));
            renderGeometry = geometry.Transform(bake);
        }

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
