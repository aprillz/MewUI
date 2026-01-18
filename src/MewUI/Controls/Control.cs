using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for all controls.
/// </summary>
public abstract class Control : FrameworkElement, IDisposable
{
    private IFont? _font;
    private bool _disposed;
    private Color? _background;
    private Color? _foreground;
    private Color? _borderBrush;
    private string? _fontFamily;
    private double? _fontSize;
    private FontWeight? _fontWeight;

    protected virtual Color DefaultBackground => Color.Transparent;
    protected virtual Color DefaultForeground => Theme.Current.Palette.WindowText;
    protected virtual Color DefaultBorderBrush => Color.Transparent;
    protected virtual string DefaultFontFamily => Theme.Current.FontFamily;
    protected virtual double DefaultFontSize => Theme.Current.FontSize;
    protected virtual FontWeight DefaultFontWeight => Theme.Current.FontWeight;

    protected readonly struct TextMeasurementScope : IDisposable
    {
        public IGraphicsFactory Factory { get; }
        public IGraphicsContext Context { get; }
        public IFont Font { get; }

        public TextMeasurementScope(IGraphicsFactory factory, IGraphicsContext context, IFont font)
        {
            Factory = factory;
            Context = context;
            Font = font;
        }

        public void Dispose() => Context.Dispose();
    }

    protected TextMeasurementScope BeginTextMeasurement()
    {
        var factory = GetGraphicsFactory();
        var context = factory.CreateMeasurementContext(GetDpi());
        var font = GetFont(factory);
        return new TextMeasurementScope(factory, context, font);
    }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public Color Background
    {
        get => _background ?? DefaultBackground;
        set
        {
            _background = value;
            InvalidateVisual();
        }
    }

    public void ClearBackground()
    {
        if (_background == null)
        {
            return;
        }

        _background = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Gets or sets the foreground (text) color.
    /// </summary>
    public Color Foreground
    {
        get => _foreground ?? DefaultForeground;
        set
        {
            _foreground = value;
            InvalidateVisual();
        }
    }

    public void ClearForeground()
    {
        if (_foreground == null)
        {
            return;
        }

        _foreground = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Gets or sets the border color.
    /// </summary>
    public Color BorderBrush
    {
        get => _borderBrush ?? DefaultBorderBrush;
        set
        {
            _borderBrush = value;
            InvalidateVisual();
        }
    }

    public void ClearBorderBrush()
    {
        if (_borderBrush == null)
        {
            return;
        }

        _borderBrush = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    public double BorderThickness
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily
    {
        get => _fontFamily ?? DefaultFontFamily;
        set
        {
            _fontFamily = value ?? string.Empty;
            _font?.Dispose();
            _font = null;
            InvalidateMeasure();
        }
    }

    public void ClearFontFamily()
    {
        if (_fontFamily == null)
        {
            return;
        }

        _fontFamily = null;
        _font?.Dispose();
        _font = null;
        InvalidateMeasure();
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double FontSize
    {
        get => _fontSize ?? DefaultFontSize;
        set
        {
            _fontSize = value;
            _font?.Dispose();
            _font = null;
            InvalidateMeasure();
        }
    }

    public void ClearFontSize()
    {
        if (_fontSize == null)
        {
            return;
        }

        _fontSize = null;
        _font?.Dispose();
        _font = null;
        InvalidateMeasure();
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FontWeight FontWeight
    {
        get => _fontWeight ?? DefaultFontWeight;
        set
        {
            _fontWeight = value;
            _font?.Dispose();
            _font = null;
            InvalidateMeasure();
        }
    }

    public void ClearFontWeight()
    {
        if (_fontWeight == null)
        {
            return;
        }

        _fontWeight = null;
        _font?.Dispose();
        _font = null;
        InvalidateMeasure();
    }

    /// <summary>
    /// Gets or creates the font for this control.
    /// </summary>
    protected IFont GetFont(IGraphicsFactory factory)
    {
        if (_font == null)
        {
            _font = factory.CreateFont(FontFamily, FontSize, GetDpi(), FontWeight);
        }

        return _font;
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi) => OnDpiChanged(oldDpi, newDpi);

    protected virtual void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        _font?.Dispose();
        _font = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    internal void NotifyThemeChanged(Theme oldTheme, Theme newTheme) => OnThemeChanged(oldTheme, newTheme);

    protected virtual void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        _font?.Dispose();
        _font = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected Theme GetTheme()
    {
        return Theme.Current;
    }

    protected readonly struct VisualState
    {
        public bool IsEnabled { get; }
        public bool IsHot { get; }
        public bool IsFocused { get; }
        public bool IsPressed { get; }
        public bool IsActive { get; }

        public VisualState(bool isEnabled, bool isHot, bool isFocused, bool isPressed, bool isActive)
        {
            IsEnabled = isEnabled;
            IsHot = isHot;
            IsFocused = isFocused;
            IsPressed = isPressed;
            IsActive = isActive;
        }
    }

    protected VisualState GetVisualState(bool isPressed = false, bool isActive = false)
    {
        var enabled = IsEffectivelyEnabled;
        var hot = enabled && (IsMouseOver || IsMouseCaptured);
        var focused = enabled && IsFocused;
        var pressed = enabled && isPressed;
        var active = enabled && isActive;
        return new VisualState(enabled, hot, focused, pressed, active);
    }

    protected Color PickAccentBorder(Theme theme, Color baseBorder, in VisualState state, double hoverMix = 0.6)
    {
        if (!state.IsEnabled)
        {
            return baseBorder;
        }

        if (state.IsFocused || state.IsActive || state.IsPressed)
        {
            return theme.Palette.Accent;
        }

        if (state.IsHot)
        {
            return baseBorder.Lerp(theme.Palette.Accent, hoverMix);
        }

        return baseBorder;
    }

    protected void DrawFocusGlow(IGraphicsContext context, Rect bounds, double radiusDip, double thicknessDip, byte alpha = 0x44, double expandDip = 1)
    {
        var theme = GetTheme();
        var outer = bounds.Inflate(expandDip, expandDip);
        var dpiScale = GetDpi() / 96.0;
        var radius = radiusDip <= 0 ? 0 : LayoutRounding.RoundToPixel(radiusDip + expandDip, dpiScale);
        var stroke = thicknessDip <= 0 ? 1 : LayoutRounding.SnapThicknessToPixels(thicknessDip, dpiScale, 1) + 2;
        outer = GetSnappedBorderBounds(outer);
        if (radius > 0)
        {
            context.DrawRoundedRectangle(outer, radius, radius, theme.Palette.Accent.WithAlpha(alpha), stroke);
        }
        else
        {
            context.DrawRectangle(outer, theme.Palette.Accent.WithAlpha(alpha), stroke);
        }
    }

    /// <summary>
    /// Gets the graphics factory from the owning window, or the default factory.
    /// </summary>
    protected IGraphicsFactory GetGraphicsFactory()
    {
        var root = FindVisualRoot();
        if (root is Window window)
        {
            return window.GraphicsFactory;
        }

        return Application.DefaultGraphicsFactory;
    }

    /// <summary>
    /// Gets the font using the control's graphics factory.
    /// </summary>
    protected IFont GetFont() => GetFont(GetGraphicsFactory());

    protected uint GetDpi()
    {
        return GetDpiCached();
    }

    protected double GetBorderVisualInset()
    {
        if (BorderThickness <= 0)
        {
            return 0;
        }

        var dpiScale = GetDpi() / 96.0;
        // Treat borders as an "inside" inset and snap thickness to whole device pixels.
        return LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale,  1);
    }

    protected Rect GetSnappedBorderBounds(Rect bounds)
    {
        var dpiScale = GetDpi() / 96.0;
        return LayoutRounding.SnapRectEdgesToPixels(bounds, dpiScale);
    }

    protected void DrawBackgroundAndBorder(
        IGraphicsContext context,
        Rect bounds,
        Color background,
        Color borderBrush,
        double cornerRadiusDip)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderThickness = BorderThickness <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale, 1);
        var radius = cornerRadiusDip <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadiusDip, dpiScale);

        bounds = GetSnappedBorderBounds(bounds);

        if (borderThickness > 0 && borderBrush.A > 0 && background.A > 0)
        {
            // Fill "stroke" using outer + inner shapes (avoids half-pixel pen alignment issues).
            if (radius > 0)
            {
                context.FillRoundedRectangle(bounds, radius, radius, borderBrush);
            }
            else
            {
                context.FillRectangle(bounds, borderBrush);
            }

            var inner = bounds.Deflate(new Thickness(borderThickness));
            var innerRadius = Math.Max(0, radius - borderThickness);

            if (inner.Width > 0 && inner.Height > 0)
            {
                if (innerRadius > 0)
                {
                    context.FillRoundedRectangle(inner, innerRadius, innerRadius, background);
                }
                else
                {
                    context.FillRectangle(inner, background);
                }
            }

            return;
        }

        if (background.A > 0)
        {
            if (radius > 0)
            {
                context.FillRoundedRectangle(bounds, radius, radius, background);
            }
            else
            {
                context.FillRectangle(bounds, background);
            }
        }

        if (borderThickness > 0 && borderBrush.A > 0)
        {
            // Fallback: draw as stroke when background is transparent.
            if (radius > 0)
            {
                context.DrawRoundedRectangle(bounds, radius, radius, borderBrush, borderThickness);
            }
            else
            {
                context.DrawRectangle(bounds, borderBrush, borderThickness);
            }
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        DrawBackgroundAndBorder(
            context,
            Bounds,
            Background,
            BorderBrush,
            0);
    }

    protected virtual void OnDispose() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Release extension-managed bindings (and any other UIElement-registered disposables).
        DisposeBindings();

        // Release cached font resources.
        _font?.Dispose();
        _font = null;

        OnDispose();
    }
}
