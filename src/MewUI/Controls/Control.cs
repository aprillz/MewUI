using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for all controls.
/// </summary>
public abstract class Control : FrameworkElement
{
    private IFont? _font;
    private Color? _background;
    private Color? _foreground;
    private Color? _borderBrush;
    private double? _borderThickness;
    private string? _fontFamily;
    private double? _fontSize;
    private FontWeight? _fontWeight;
    private Point _lastMousePositionInWindow;

    /// <summary>
    /// Gets or sets the tooltip text for this control.
    /// </summary>
    public string? ToolTipText { get; set; }

    /// <summary>
    /// Gets or sets the tooltip content for this control.
    /// When set, this takes precedence over <see cref="ToolTipText"/>.
    /// </summary>
    public Element? ToolTipContent { get; set; }

    /// <summary>
    /// Gets or sets the context menu for this control.
    /// </summary>
    public ContextMenu? ContextMenu { get; set; }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public Color Background
    {
        get => _background ?? DefaultBackground;
        set
        {
            if (Background == value)
            {
                return;
            }

            _background = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the foreground (text) color.
    /// </summary>
    public Color Foreground
    {
        get => _foreground ?? DefaultForeground;
        set
        {
            if (Foreground == value)
            {
                return;
            }

            _foreground = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the border color.
    /// </summary>
    public Color BorderBrush
    {
        get => _borderBrush ?? DefaultBorderBrush;
        set
        {
            if (BorderBrush == value)
            {
                return;
            }

            _borderBrush = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    public double BorderThickness
    {
        get => _borderThickness ?? DefaultBorderThickness;
        set
        {
            if (BorderThickness.Equals(value))
            {
                return;
            }

            _borderThickness = value;
            InvalidateMeasure();
            InvalidateArrange();
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


    protected virtual Color DefaultBackground => Color.Transparent;

    protected virtual Color DefaultForeground => Theme.Palette.WindowText;

    protected virtual Color DefaultBorderBrush => Color.Transparent;

    protected virtual double DefaultBorderThickness => 0;


    protected virtual string DefaultFontFamily => Theme.Metrics.FontFamily;

    protected virtual double DefaultFontSize => Theme.Metrics.FontSize;

    protected virtual FontWeight DefaultFontWeight => Theme.Metrics.FontWeight;

    public void ClearBackground()
    {
        if (_background == null)
        {
            return;
        }

        _background = null;
        InvalidateVisual();
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

    public void ClearBorderBrush()
    {
        if (_borderBrush == null)
        {
            return;
        }

        _borderBrush = null;
        InvalidateVisual();
    }

    public void ClearBorderThickness()
    {
        if (_borderThickness == null)
        {
            return;
        }

        _borderThickness = null;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
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

    protected TextMeasurementScope BeginTextMeasurement()
    {
        var factory = GetGraphicsFactory();
        var context = factory.CreateMeasurementContext(GetDpi());
        var font = GetFont(factory);
        return new TextMeasurementScope(factory, context, font);
    }

    /// <summary>
    /// Gets or creates the font for this control.
    /// </summary>
    protected IFont GetFont(IGraphicsFactory factory)
    {
        _font ??= factory.CreateFont(FontFamily, FontSize, GetDpi(), FontWeight);

        return _font;
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        _font?.Dispose();
        _font = null;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        _font?.Dispose();
        _font = null;

        base.OnThemeChanged(oldTheme, newTheme);
    }

    protected VisualState GetVisualState(bool isPressed = false, bool isActive = false)
    {
        var enabled = IsEffectivelyEnabled;
        var hot = enabled && (IsMouseOver || IsMouseCaptured);
        var focused = enabled && (IsFocused || IsFocusWithin);
        var pressed = enabled && isPressed;
        var active = enabled && isActive;
        return new VisualState(enabled, hot, focused, pressed, active);
    }

    protected VisualState GetVisualState(bool isPressed, bool isActive, bool enabledOverride)
    {
        var enabled = enabledOverride;
        var hot = enabled && (IsMouseOver || IsMouseCaptured);
        var focused = enabled && (IsFocused || IsFocusWithin);
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

        var accent = theme.Palette.Accent;

        if (state.IsFocused || state.IsActive || state.IsPressed)
        {
            // If the control uses the standard border color, keep the strong accent border.
            // If a custom border was supplied, tint it toward the accent instead of hard-replacing it.
            // This avoids "jumping" to a ButtonFace/ControlBorder-based accent when Background/BorderBrush is customized.
            return baseBorder == theme.Palette.ControlBorder
                ? accent
                : Color.Composite(baseBorder, theme.Palette.AccentBorderActiveOverlay);
        }

        if (state.IsHot)
        {
            var overlay = hoverMix == 0.6
                ? theme.Palette.AccentBorderHotOverlay
                : accent.WithAlpha((byte)Math.Clamp(Math.Round(hoverMix * 255.0), 0, 255));

            return Color.Composite(baseBorder, overlay);
        }

        return baseBorder;
    }

    protected Color PickButtonBackground(in VisualState state, Color? normalBackground = null)
    {
        var baseBg = normalBackground ?? Background;

        if (!state.IsEnabled)
        {
            return Theme.Palette.ButtonDisabledBackground;
        }

        if (state.IsPressed || state.IsActive)
        {
            return Color.Composite(baseBg, Theme.Palette.AccentPressedOverlay);
        }

        if (state.IsHot)
        {
            return Color.Composite(baseBg, Theme.Palette.AccentHoverOverlay);
        }

        return baseBg;
    }

    protected Color PickControlBackground(in VisualState state, Color? normalBackground = null)
    {
        return state.IsEnabled ? (normalBackground ?? Background) : Theme.Palette.DisabledControlBackground;
    }

    protected Color PickControlBackground(in VisualState state, Color normalBackground)
    {
        return state.IsEnabled ? normalBackground : Theme.Palette.DisabledControlBackground;
    }

    /// <summary>
    /// Gets the font using the control's graphics factory.
    /// </summary>
    protected IFont GetFont() => GetFont(GetGraphicsFactory());

    protected double GetBorderVisualInset()
    {
        if (BorderThickness <= 0)
        {
            return 0;
        }

        var dpiScale = GetDpi() / 96.0;
        // Treat borders as an "inside" inset and snap thickness to whole device pixels.
        return LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale, 1);
    }

    protected BorderRenderMetrics GetBorderRenderMetrics(Rect bounds, double cornerRadiusDip, bool snapBounds = true)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderThickness = BorderThickness <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale, 1);
        var radius = cornerRadiusDip <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadiusDip, dpiScale);

        if (snapBounds)
        {
            bounds = LayoutRounding.SnapBoundsRectToPixels(bounds, dpiScale);
        }

        return new BorderRenderMetrics(bounds, dpiScale, borderThickness, radius);
    }

    protected void DrawBackgroundAndBorder(
        IGraphicsContext context,
        Rect bounds,
        Color background,
        Color borderBrush,
        double cornerRadiusDip)
    {
        if (background.A == 0 && (BorderThickness <= 0 || borderBrush.A == 0))
        {
            return;
        }

        var metrics = GetBorderRenderMetrics(bounds, cornerRadiusDip);
        bounds = metrics.Bounds;
        var borderThickness = metrics.BorderThickness;
        var radius = metrics.CornerRadius;


        bool canUseFillStrokeTrick = borderThickness > 0 &&
                                  borderBrush.A > 0 &&
                                  background.A > 0;

        if (canUseFillStrokeTrick || background.A == 255)
        {
            if (borderThickness > 0)
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

        if (Background.A == 0 && (BorderThickness <= 0 || BorderBrush.A == 0))
        {
            return;
        }

        // Use the shared state-based border color logic (focus/hover/pressed/active).
        // This keeps base rendering consistent with other controls.
        var state = GetVisualState();
        var borderBrush = PickAccentBorder(Theme, BorderBrush, state, 0.6);

        DrawBackgroundAndBorder(
            context,
            Bounds,
            Background,
            borderBrush,
            0);
    }

    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        ShowToolTip();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        HideToolTip();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastMousePositionInWindow = e.Position;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        HideToolTip();

        if (e.Handled)
        {
            return;
        }

        if (e.Button == MouseButton.Right && ContextMenu != null)
        {
            ContextMenu.ShowAt(this, e.Position);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        // Hide tooltips on keyboard interaction.
        HideToolTip();
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        // Release cached font resources.
        _font?.Dispose();
        _font = null;

        HideToolTip();
    }

    private void ShowToolTip()
    {
        if (!IsMouseOver)
        {
            return;
        }

        if (ToolTipContent == null && string.IsNullOrWhiteSpace(ToolTipText))
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        var client = window.ClientSize;
        var anchor = window.LastMousePositionDip;
        if (anchor.X == 0 && anchor.Y == 0)
        {
            anchor = _lastMousePositionInWindow;
        }
        if (anchor.X == 0 && anchor.Y == 0 && Bounds.Width > 0 && Bounds.Height > 0)
        {
            anchor = new Point(Bounds.X + Bounds.Width / 2, Bounds.Bottom);
        }

        const double dx = 12;
        const double dy = 18;
        double x = anchor.X + dx;
        double y = anchor.Y + dy;

        Size desired;
        if (ToolTipContent != null)
        {
            // Measure using the window-owned tooltip instance so sizing matches what will be shown.
            // This avoids creating a tooltip per control.
            var measureSize = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
            desired = window.MeasureToolTip(ToolTipContent, measureSize);
        }
        else
        {
            var toolTipText = ToolTipText!;
            var measureSize = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
            desired = window.MeasureToolTip(toolTipText, measureSize);
        }

        double w = Math.Max(0, desired.Width);
        double h = Math.Max(0, desired.Height);

        if (x + w > client.Width)
        {
            x = Math.Max(0, client.Width - w);
        }

        if (y + h > client.Height)
        {
            y = Math.Max(0, anchor.Y - h - dy);
            if (y < 0)
            {
                y = Math.Max(0, client.Height - h);
            }
        }

        if (ToolTipContent != null)
        {
            window.ShowToolTip(this, ToolTipContent, new Rect(x, y, w, h));
        }
        else
        {
            window.ShowToolTip(this, ToolTipText!, new Rect(x, y, w, h));
        }
    }

    private void HideToolTip()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        window.CloseToolTip(this);
    }

    protected readonly struct TextMeasurementScope : IDisposable
    {
        public TextMeasurementScope(IGraphicsFactory factory, IGraphicsContext context, IFont font)
        {
            Factory = factory;
            Context = context;
            Font = font;
        }

        public IGraphicsFactory Factory { get; }

        public IGraphicsContext Context { get; }

        public IFont Font { get; }

        public void Dispose() => Context.Dispose();
    }

    protected readonly struct VisualState
    {
        public VisualState(bool isEnabled, bool isHot, bool isFocused, bool isPressed, bool isActive)
        {
            IsEnabled = isEnabled;
            IsHot = isHot;
            IsFocused = isFocused;
            IsPressed = isPressed;
            IsActive = isActive;
        }

        public bool IsEnabled { get; }

        public bool IsHot { get; }

        public bool IsFocused { get; }

        public bool IsPressed { get; }

        public bool IsActive { get; }
    }

    protected readonly struct BorderRenderMetrics
    {
        public BorderRenderMetrics(Rect bounds, double dpiScale, double borderThickness, double cornerRadius)
        {
            Bounds = bounds;
            DpiScale = dpiScale;
            BorderThickness = borderThickness;
            CornerRadius = cornerRadius;
        }

        public Rect Bounds { get; }

        public double DpiScale { get; }

        public double BorderThickness { get; }

        public double CornerRadius { get; }
    }
}
