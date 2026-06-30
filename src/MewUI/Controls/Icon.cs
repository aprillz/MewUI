using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class Icon : FrameworkElement
{
    public static readonly MewProperty<PathGeometry?> DataProperty =
        MewProperty<PathGeometry?>.Register<Icon>(nameof(Data), null, MewPropertyOptions.AffectsLayout);

    private Color? _foreground;

    public PathGeometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Color Foreground
    {
        get => _foreground ?? GetValue(Control.ForegroundProperty);
        set
        {
            if (_foreground.HasValue && _foreground.Value == value)
                return;
            _foreground = value;
            InvalidateVisual();
        }
    }

    public void ClearForeground()
    {
        if (!_foreground.HasValue)
            return;
        _foreground = null;
        InvalidateVisual();
    }

    public double FontSize
    {
        get => GetValue(Control.FontSizeProperty);
        set => SetValue(Control.FontSizeProperty, value);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property.Id == Control.FontSizeProperty.Id)
        {
            InvalidateVisual();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var geo = Data;
        if (geo == null || geo.IsEmpty)
            return Size.Empty;
        var size = FontSize;
        return new Size(size, size);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var geo = Data;
        if (geo == null || geo.IsEmpty)
            return;

        var color = Foreground;
        if (color.A == 0)
            return;

        var bounds = Bounds;
        var geoBounds = geo.GetBounds();
        if (geoBounds.Width <= 0 || geoBounds.Height <= 0)
            return;

        double scale = Math.Min(bounds.Width / geoBounds.Width, bounds.Height / geoBounds.Height);
        if (scale <= 0)
            return;

        double scaledW = geoBounds.Width * scale;
        double scaledH = geoBounds.Height * scale;
        double offsetX = bounds.X + (bounds.Width - scaledW) / 2;
        double offsetY = bounds.Y + (bounds.Height - scaledH) / 2;

        var bake =
            System.Numerics.Matrix3x2.CreateTranslation((float)-geoBounds.X, (float)-geoBounds.Y) *
            System.Numerics.Matrix3x2.CreateScale((float)scale) *
            System.Numerics.Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY);

        var renderGeo = geo.Transform(bake);
        context.FillPath(renderGeo, color);
    }
}
