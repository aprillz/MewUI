namespace Aprillz.MewUI.Controls;

/// <summary>
/// Default visual tree for <see cref="SplitButton"/>, applied through its default style
/// (see <see cref="DefaultStyles"/>). One chrome border holds both faces so the control reads as a
/// single button; each face is a real flat button, so hover, press and disabled visuals apply to
/// the half the pointer is on.
/// </summary>
internal static class SplitButtonTemplate
{
    // The width a ComboBox reserves for its arrow, so both controls place the glyph alike.
    private const double GLYPH_AREA_WIDTH = 22;

    private static DelegateControlTemplate<SplitButton>? _instance;

    /// <summary>Gets the shared template definition; each control that applies it builds its own tree.</summary>
    public static DelegateControlTemplate<SplitButton> Instance
        => _instance ??= new DelegateControlTemplate<SplitButton>(Build);

    private static Element Build(SplitButton owner, ControlTemplateContext ctx)
    {
        // Only the outer corners round; the shared edge stays square so the two fills meet flush and
        // the pair still reads as one button.
        double radius = owner.CornerRadius;

        var theme = owner.ThemeInternal;
        // Whole device pixels: a fractional column puts the boundary on a half pixel, where the
        // hairline can collapse to nothing or render two pixels wide.
        double dpiScale = owner.GetDpi() / 96.0;
        double splitterWidth = LayoutRounding.SnapThicknessToPixels(
            theme.Metrics.ControlBorderThickness, dpiScale, 1);

        var primary = new DropDownFaceButton
        {
            Focusable = false,
            IsTabStop = false,
            FaceCornerRadius = new CornerRadius(radius, 0, 0, radius),
            Content = new ContentPresenter().CenterVertical(),
        }.Column(0).ColumnSpan(2);
        ctx.Register(SplitButton.PART_PRIMARY_BUTTON, primary);
        ctx.Bind(primary, Control.PaddingProperty);

        // The hairline is what tells a split button from a plain drop-down button: it marks where the
        // primary action ends and the menu begins.
        var splitter = new Border
        {
            Background = theme.Palette.ControlBorder,
            Margin = new (0,4)
        }.Column(1);

        var dropDown = new DropDownFaceButton
        {
            Focusable = false,
            IsTabStop = false,
            FaceCornerRadius = new CornerRadius(0, radius, radius, 0),
            Padding = Thickness.Zero,
            Content = new GlyphElement { Kind = GlyphKind.ChevronDown }.Center(),
        }.Column(2);
        ctx.Register(SplitButton.PART_DROP_DOWN_BUTTON, dropDown);

        var chrome = new Border
        {
            Child = new Grid()
                .Columns(
                    GridLength.Star,
                    GridLength.Pixels(splitterWidth),
                    GridLength.Pixels(GLYPH_AREA_WIDTH))
                .Children(primary, splitter, dropDown),
            ClipToBounds = true,
        };
        ctx.BindChrome(chrome);

        return chrome;
    }
}
