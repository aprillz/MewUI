using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Default visual tree for <see cref="NumericUpDown"/>, applied through its default style
/// (see <see cref="DefaultStyles"/>). Text area on the left, a hairline separator, then a
/// two-way spinner column on the right.
/// </summary>
internal static class NumericUpDownTemplate
{
    private static readonly Dictionary<Theme, DelegateControlTemplate<NumericUpDown>> _perTheme = [];

    /// <summary>Gets the shared template definition for the theme; each control builds its own tree.</summary>
    public static DelegateControlTemplate<NumericUpDown> GetForTheme(Theme theme)
    {
        lock (_perTheme)
        {
            if (!_perTheme.TryGetValue(theme, out var template))
            {
                template = new DelegateControlTemplate<NumericUpDown>(Build);
                _perTheme[theme] = template;
            }
            return template;
        }
    }

    // Build reads theme values at build time; the per-theme definition cache makes a theme swap
    // deliver a different definition, which rebuilds the instance with fresh metrics and colors.
    private static Element Build(NumericUpDown owner, ControlTemplateContext ctx)
    {
        var theme = owner.ThemeInternal;
        double separatorWidth = theme.Metrics.ControlBorderThickness;
        double spinnerWidth = theme.Metrics.BaseControlHeight - theme.Metrics.ControlBorderThickness * 2;

        var displayText = new TextBlock().Column(0);
        ctx.Register(NumericUpDown.PART_DISPLAY_TEXT, displayText);
        ctx.Bind(displayText, TextBlock.TextProperty, NumericUpDown.DisplayTextProperty);

        var editBox = new TextBox
        {
            BorderThickness = 0,
            Background = Color.Transparent,
            Padding = Thickness.Zero,
            MinHeight = 0,
            IsVisible = false,
            IsHitTestVisible = false,
            // Focus enters via SetIsEditing, not Tab; keeps the control a single tab stop while editing.
            IsTabStop = false,
            ImeMode = ImeMode.Disabled,
        }.Column(0);
        ctx.Register(NumericUpDown.PART_TEXT_BOX, editBox);

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
            .Children(displayText, editBox, separator, spinner);

        var chrome = new Border
        {
            Child = grid,
            ClipToBounds = true,
        };
        ctx.BindChrome(chrome);
        ctx.Bind(chrome, Control.PaddingProperty);

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
