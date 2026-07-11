using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A lightweight element that displays a <see cref="GlyphKind"/> shape.
/// Foreground color is inherited from ancestors.
/// </summary>
public sealed class GlyphElement : FrameworkElement
{
    public static readonly MewProperty<GlyphKind> KindProperty =
        MewProperty<GlyphKind>.Register<GlyphElement>(nameof(Kind), GlyphKind.ChevronDown, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> GlyphSizeProperty =
        MewProperty<double>.Register<GlyphElement>(nameof(GlyphSize), 4.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> StrokeThicknessProperty =
        MewProperty<double>.Register<GlyphElement>(nameof(StrokeThickness), 1.0, MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Gets or sets the foreground color, backed by <see cref="TextElement.ForegroundProperty"/> so it participates
    /// in the same inheritance chain as text-bearing ancestors.
    /// </summary>
    public Color Foreground
    {
        get => GetValue(TextElement.ForegroundProperty);
        set => SetValue(TextElement.ForegroundProperty, value);
    }

    public GlyphKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Clears a locally set <see cref="Foreground"/>, reverting to the inherited value.
    /// </summary>
    public void ClearForeground()
    {
        PropertyStore.ClearLocal(TextElement.ForegroundProperty);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double size = GlyphSize * 2;
        return new Size(size, size);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        double cx = bounds.X + bounds.Width / 2;
        double cy = bounds.Y + bounds.Height / 2;

        // Snap to pixel grid for crisp rendering.
        double dpiScale = GetDpiScaleCached();
        if (dpiScale > 0)
        {
            double pixel = 1.0 / dpiScale;
            double strokePx = Math.Round(StrokeThickness * dpiScale);
            double halfPixelOffset = (strokePx % 2 == 1) ? 0.5 * pixel : 0;

            // Snap center and apply half-pixel offset for odd strokes
            cx = Math.Floor(cx * dpiScale) * pixel + halfPixelOffset;
            cy = Math.Floor(cy * dpiScale) * pixel + halfPixelOffset;
        }

        Glyph.Draw(context, new Point(cx, cy), GlyphSize, Foreground, Kind, StrokeThickness);
    }
}
