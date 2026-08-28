using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Default visual tree for <see cref="NumericUpDown"/>, applied through its default style
/// (see <see cref="DefaultStyles"/>). Text area on the left, a hairline separator, then a
/// two-way spinner column on the right.
/// </summary>
internal static class NumericUpDownTemplate
{
    private static DelegateControlTemplate<NumericUpDown>? _instance;

    /// <summary>Gets the shared template definition; each control that applies it builds its own tree.</summary>
    public static DelegateControlTemplate<NumericUpDown> Instance
        => _instance ??= new DelegateControlTemplate<NumericUpDown>(Build);

    // Build reads theme and DPI at build time; Control invalidates template instances on either
    // change, so a rebuilt tree always bakes the current metrics, colors and scale.
    private static Element Build(NumericUpDown owner, ControlTemplateContext ctx)
    {
        var theme = owner.ThemeInternal;
        // Both columns are whole device pixels: a fractional-pixel column puts the boundary between two
        // columns on a half pixel, where the separator can collapse to nothing and leave a seam, and it
        // would also let the hairline render two pixels wide next to a one-pixel chrome border.
        double dpiScale = owner.GetDpi() / 96.0;
        double separatorWidth = LayoutRounding.SnapThicknessToPixels(
            theme.Metrics.ControlBorderThickness, dpiScale, 1);
        double spinnerWidth = LayoutRounding.SnapThicknessToPixels(
            theme.Metrics.BaseControlHeight - theme.Metrics.ControlBorderThickness * 2, dpiScale, 1);

        var displayText = new TextBlock().CenterVertical();
        ctx.Register(NumericUpDown.PART_DISPLAY_TEXT, displayText);
        ctx.Bind(displayText, TextBlock.TextProperty, NumericUpDown.DisplayTextProperty);

        var editBox = new TextBox
        {
            BorderThickness = 0,
            Background = Color.Transparent,
            Padding = Thickness.Zero,
            MinHeight = 0,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            IsHitTestVisible = false,
            // Focus enters via SetIsEditing, not Tab; keeps the control a single tab stop while editing.
            IsTabStop = false,
            ImeMode = ImeMode.Disabled,
            // The display text governs the control's width; the edit text must not
            // resize it: clearing it shrank the control, longer input grew it.
            MeasuresOwnTextWidth = false,
        };
        ctx.Register(NumericUpDown.PART_TEXT_BOX, editBox);

        // Padding is the text area's inset only; the separator and spinner column run the full
        // inner height, matching the pre-template layout.
        var textHost = new Border
        {
            Child = new Grid().Children(displayText, editBox),
        }.Column(0);
        ctx.Bind(textHost, Control.PaddingProperty);

        var separator = new Border
        {
            Background = theme.Palette.ControlBorder,
        }.Column(1);

        var spinner = new UniformGrid { Rows = 2, Columns = 1 }
            .Children(
                CreateSpinnerButton(owner, GlyphKind.ChevronUp, owner.StepUp),
                CreateSpinnerButton(owner, GlyphKind.ChevronDown, owner.StepDown))
            .Column(2);

        var grid = new Grid()
            .Columns(GridLength.Star, GridLength.Pixels(separatorWidth), GridLength.Pixels(spinnerWidth))
            .Children(textHost, separator, spinner);

        var chrome = new Border
        {
            Child = grid,
            ClipToBounds = true,
        };
        ctx.BindChrome(chrome);

        return chrome;
    }

    private static RepeatButton CreateSpinnerButton(NumericUpDown owner, GlyphKind glyphKind, Action step)
    {
        var button = new RepeatButton
        {
            // Spinner parts must not join the tab order or steal focus from the control.
            Focusable = false,
            IsTabStop = false,
            BorderThickness = 0,
            CornerRadius = 0,
            Padding = Thickness.Zero,
            MinHeight = 0,
            Content = new GlyphElement { Kind = glyphKind },
        };
        button.Click += () =>
        {
            // The buttons are not focusable, so clicking them keeps keyboard stepping working
            // by focusing the control itself (outside of an in-flight edit).
            if (!owner.IsEditing)
            {
                owner.Focus();
            }
            step();
        };
        return button;
    }
}
