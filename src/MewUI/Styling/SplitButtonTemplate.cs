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

    private static readonly Func<Palette, Color> _defaultSplitLine = static palette => palette.ControlBorder;

    private static DelegateControlTemplate<SplitButton>? _instance;

    /// <summary>Gets the shared template definition; each control that applies it builds its own tree.</summary>
    public static DelegateControlTemplate<SplitButton> Instance
        => _instance ??= Create(faceStyle: null, _defaultSplitLine);

    /// <summary>
    /// The same tree dressed for a chrome of another color: the faces take the given style, and the split
    /// line the given color. A style rather than a style name, because the template hands it to the faces
    /// through a sheet of their own scope: nothing outside this control can reach or take that look.
    /// </summary>
    public static DelegateControlTemplate<SplitButton> WithFaceStyle(
        Style faceStyle,
        Func<Palette, Color> splitLineColorSelector)
    {
        ArgumentNullException.ThrowIfNull(faceStyle);
        ArgumentNullException.ThrowIfNull(splitLineColorSelector);
        return Create(faceStyle, splitLineColorSelector);
    }

    private static DelegateControlTemplate<SplitButton> Create(
        Style? faceStyle,
        Func<Palette, Color> splitLineColorSelector)
        => new((owner, ctx) => Build(owner, ctx, faceStyle, splitLineColorSelector));

    private static Element Build(
        SplitButton owner,
        ControlTemplateContext ctx,
        Style? faceStyle,
        Func<Palette, Color> splitLineColorSelector)
    {
        // Only the outer corners round; the shared edge stays square so the two fills meet flush and
        // the pair still reads as one button.
        var primary = new DropDownFaceButton
        {
            Focusable = false,
            IsTabStop = false,
            FaceSide = DropDownFaceSide.Left,
            Content = new ContentPresenter().CenterVertical(),
        }.Column(0);
        ctx.Register(SplitButton.PART_PRIMARY_BUTTON, primary);
        ctx.Bind(primary, Control.PaddingProperty);
        ctx.Bind(primary, Control.CornerRadiusProperty);

        // The hairline is what tells a split button from a plain drop-down button: it marks where the
        // primary action ends and the menu begins. It rides the right edge of the primary column rather
        // than holding a column of its own, so neither its colour nor its width reaches the grid
        // definition, where a value read at build time would freeze the theme and scale it was built at.
        // Whole device pixels: a fractional width puts the hairline on a half pixel, where it can
        // collapse to nothing or render two pixels wide.
        var splitter = new Border
        {
            Margin = new(0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            // It overlays the primary face, which owns the pointer along that edge.
            IsHitTestVisible = false,
        }
            .WithTheme((t, border) =>
            {
                border.Background = splitLineColorSelector(t.Palette);
                border.Width = LayoutRounding.SnapThicknessToPixels(
                    t.Metrics.ControlBorderThickness, border.GetDpi() / 96.0, 1);
            })
            .Column(0);

        var dropDown = new DropDownFaceButton
        {
            Focusable = false,
            IsTabStop = false,
            FaceSide = DropDownFaceSide.Right,
            Padding = Thickness.Zero,
            Content = new GlyphElement { Kind = GlyphKind.ChevronDown }.Center(),
        }.Column(1);
        ctx.Register(SplitButton.PART_DROP_DOWN_BUTTON, dropDown);
        ctx.Bind(dropDown, Control.CornerRadiusProperty);

        var chrome = new Border
        {
            Child = new Grid()
                .Columns(GridLength.Star, GridLength.Pixels(GLYPH_AREA_WIDTH))
                .Children(primary, splitter, dropDown),
            ClipToBounds = true,
        };
        ctx.BindChrome(chrome);

        if (faceStyle != null)
        {
            // A type rule on the chrome: the faces resolve from the nearest sheet up their context chain,
            // so this dresses them without a name anyone outside this control could reach.
            var sheet = new StyleSheet();
            sheet.Define<DropDownFaceButton>(faceStyle);
            chrome.StyleSheet = sheet;
        }

        return chrome;
    }
}
