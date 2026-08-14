namespace Aprillz.MewUI.Controls;

/// <summary>
/// Default visual tree for <see cref="DropDownButton"/>, applied through its default style
/// (see <see cref="DefaultStyles"/>). The owner's chrome wraps one face part that holds the content
/// and a trailing chevron.
/// </summary>
internal static class DropDownButtonTemplate
{
    // The width a ComboBox reserves for its arrow, so both controls place the glyph alike.
    private const double GLYPH_AREA_WIDTH = 22;

    private static DelegateControlTemplate<DropDownButton>? _instance;

    /// <summary>Gets the shared template definition; each control that applies it builds its own tree.</summary>
    public static DelegateControlTemplate<DropDownButton> Instance
        => _instance ??= new DelegateControlTemplate<DropDownButton>(Build);

    private static Element Build(DropDownButton owner, ControlTemplateContext ctx)
    {
        // Padding insets the content only. The glyph area is carved out first and sits against the
        // chrome, so Padding.Right reads as the gap before the glyph, matching the ComboBox header.
        var contentHost = new Border { Child = new ContentPresenter().CenterVertical() }.Column(0);
        ctx.Bind(contentHost, Control.PaddingProperty);

        var chevron = new GlyphElement { Kind = GlyphKind.ChevronDown }.Center().Column(1);

        var face = new DropDownFaceButton
        {
            // The owner's chrome draws the border and background, so the face contributes only its
            // hover and press fill. The owner is the single focus and tab stop.
            Focusable = false,
            IsTabStop = false,
            Padding = Thickness.Zero,
            Content = new Grid()
                .Columns(GridLength.Star, GridLength.Pixels(GLYPH_AREA_WIDTH))
                .Children(contentHost, chevron),
        };
        ctx.Register(DropDownButton.PART_DROP_DOWN_BUTTON, face);
        ctx.Bind(face, Control.CornerRadiusProperty);

        var chrome = new Border
        {
            Child = face,
            ClipToBounds = true,
        };
        ctx.BindChrome(chrome);

        return chrome;
    }
}
