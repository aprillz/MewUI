using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Selects which sections appear inside the <see cref="ColorPicker"/> popup.
/// </summary>
public enum ColorPickerKind
{
    /// <summary>Only the color wheel (hue ring + SV triangle).</summary>
    Wheel,

    /// <summary>Only the numeric input panel (Hex / RGB / HSV).</summary>
    Panel,

    /// <summary>Both the color wheel and the numeric input panel.</summary>
    Both,
}

/// <summary>
/// A drop-down control that allows selecting a color via an HSV picker popup.
/// </summary>
public sealed class ColorPicker : DropDownBase
{
    private ColorPickerPopup? _popup;
    private bool _updatingFromPopup;

    public static readonly MewProperty<Color> SelectedColorProperty =
        MewProperty<Color>.Register<ColorPicker>(nameof(SelectedColor), Color.FromRgb(255, 0, 0),
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, old, @new) => self.OnSelectedColorChanged(old, @new));

    public static readonly MewProperty<ColorPickerKind> KindProperty =
        MewProperty<ColorPickerKind>.Register<ColorPicker>(nameof(Kind), ColorPickerKind.Both,
            MewPropertyOptions.None,
            static (self, _, _) => self.OnKindOrShowAlphaChanged());

    public static readonly MewProperty<bool> ShowAlphaProperty =
        MewProperty<bool>.Register<ColorPicker>(nameof(ShowAlpha), false,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnKindOrShowAlphaChanged());

    /// <summary>Gets or sets the selected color.</summary>
    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Gets or sets which sections are visible inside the popup.</summary>
    public ColorPickerKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>When <see langword="true"/>, exposes alpha editing via the popup and previews transparency in the header swatch.</summary>
    public bool ShowAlpha
    {
        get => GetValue(ShowAlphaProperty);
        set => SetValue(ShowAlphaProperty, value);
    }

    /// <summary>Raised when the selected color changes.</summary>
    public event Action<Color>? SelectedColorChanged;

    private void OnSelectedColorChanged(Color oldValue, Color newValue)
    {
        if (!_updatingFromPopup)
            _popup?.SetColor(newValue);
        SelectedColorChanged?.Invoke(newValue);
    }

    private void OnKindOrShowAlphaChanged()
    {
        _popup?.Configure(Kind, ShowAlpha);
    }

    protected override UIElement CreatePopupContent()
    {
        _popup = new ColorPickerPopup(Kind, ShowAlpha);
        _popup.ColorChanged += OnPopupColorChanged;
        _popup.SetColor(SelectedColor);
        return _popup;
    }

    protected override void SyncPopupContent(UIElement popup)
    {
        // Avoid overwriting HSV state while user is dragging inside the popup.
    }

    protected override void OnIsDropDownOpenChanged(bool oldValue, bool newValue)
    {
        if (newValue && _popup != null)
        {
            _popup.SetColor(SelectedColor);
        }

        base.OnIsDropDownOpenChanged(oldValue, newValue);
    }

    protected override UIElement GetPopupFocusTarget(UIElement popup) => popup;

    private void OnPopupColorChanged(Color color)
    {
        _updatingFromPopup = true;
        SelectedColor = color;
        _updatingFromPopup = false;
    }

    protected override Size MeasureHeader(Size availableSize)
    {
        var headerHeight = ResolveHeaderHeight();
        double width = 60 + ArrowAreaWidth;
        return new Size(width, headerHeight);
    }

    protected override void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect)
    {
        var swatchRect = new Rect(
            innerHeaderRect.X,
            innerHeaderRect.Y,
            innerHeaderRect.Width - ArrowAreaWidth,
            innerHeaderRect.Height).Deflate(Padding);

        if (swatchRect.Width <= 0 || swatchRect.Height <= 0)
            return;

        var state = CurrentVisualState;
        var color = SelectedColor;

        if (ShowAlpha && color.A != 255)
        {
            context.Save();
            context.SetClipRoundedRect(swatchRect, 2, 2);
            AlphaCheckerboard.Fill(context, swatchRect, Theme.IsDark);
            context.Restore();
        } 

        context.FillRoundedRectangle(swatchRect, 2, 2, color);

        //context.DrawRoundedRectangle(swatchRect, 2, 2, Theme.Palette.ControlBorder, 1, strokeInset: true);
    }

    protected override bool PopupSizesToContent => true;

    protected override Rect CalculatePopupBounds(Window window, UIElement popup)
    {
        var bounds = Bounds;
        var client = window.ClientSize;

        // Measure with unconstrained width so the popup reports its natural width
        // (otherwise inner panels with Stretch/Grid columns expand to the client width).
        popup.Measure(new Size(double.PositiveInfinity, client.Height));
        double popupW = Math.Min(popup.DesiredSize.Width, client.Width);
        double popupH = popup.DesiredSize.Height;

        double x = PopupPlacement.ClampHorizontal(bounds.X, popupW, client.Width, floorToZero: false);

        double belowY = bounds.Y + ResolveHeaderHeight();
        var (y, height) = PopupPlacement.ResolveVerticalPreferBelowIfFits(bounds.Y, belowY, client.Height, popupH);

        return new Rect(x, y, popupW, height);
    }
}
