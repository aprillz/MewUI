using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Shapes;

/// <summary>
/// Abstract base class for shape elements that render a <see cref="PathGeometry"/>.
/// </summary>
public abstract class Shape : FrameworkElement
{
    /// <summary>
    /// Gets or sets the brush used to fill the shape interior.
    /// </summary>
    public IBrush? Fill
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush used to stroke the shape outline.
    /// </summary>
    public IBrush? Stroke
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the stroke thickness in device-independent pixels.
    /// </summary>
    public double StrokeThickness
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the stroke style (line cap, line join, dash pattern).
    /// </summary>
    public StrokeStyle StrokeStyle
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets how the geometry is stretched to fill the available space.
    /// </summary>
    public Stretch Stretch
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

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

        context.Save();

        if (Stretch != Stretch.None && geoBounds.Width > 0 && geoBounds.Height > 0)
        {
            ComputeStretchTransform(geoBounds, bounds, Stretch,
                out double scaleX, out double scaleY, out double offsetX, out double offsetY);
            context.Translate(offsetX, offsetY);
            context.Scale(scaleX, scaleY);
            context.Translate(-geoBounds.X, -geoBounds.Y);
        }
        else
        {
            // Position geometry at the element's bounds origin (context is in window coordinates).
            context.Translate(bounds.X - geoBounds.X, bounds.Y - geoBounds.Y);
        }

        if (Fill != null)
            context.FillPath(geometry, Fill);

        if (Stroke != null && StrokeThickness > 0)
        {
            using var pen = GetGraphicsFactory().CreatePen(Stroke, StrokeThickness, StrokeStyle);
            context.DrawPath(geometry, pen);
        }

        context.Restore();
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
