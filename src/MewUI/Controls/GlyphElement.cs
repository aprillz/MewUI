using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A lightweight element that displays a <see cref="GlyphKind"/> shape.
/// Foreground color is inherited from the nearest ancestor <see cref="Control"/>.
/// </summary>
public sealed class GlyphElement : FrameworkElement
{
    public static readonly MewProperty<GlyphKind> KindProperty =
        MewProperty<GlyphKind>.Register<GlyphElement>(nameof(Kind), GlyphKind.ChevronDown, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> GlyphSizeProperty =
        MewProperty<double>.Register<GlyphElement>(nameof(GlyphSize), 4.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> StrokeThicknessProperty =
        MewProperty<double>.Register<GlyphElement>(nameof(StrokeThickness), 1.0, MewPropertyOptions.AffectsRender);

    private Color? _foreground;

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
    /// Gets or sets the foreground color. When not explicitly set, inherits from
    /// the nearest ancestor <see cref="Control.ForegroundProperty"/>.
    /// </summary>
    public Color Foreground
    {
        get => _foreground ?? GetValue(Control.ForegroundProperty);
        set
        {
            if (_foreground.HasValue && _foreground.Value == value)
            {
                return;
            }

            _foreground = value;
            InvalidateVisual();
        }
    }

    public void ClearForeground()
    {
        if (!_foreground.HasValue)
        {
            return;
        }

        _foreground = null;
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double size = GlyphSize * 2;
        return new Size(size, size);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Glyph.Draw(context, center, GlyphSize, Foreground, Kind, StrokeThickness);
    }
}
