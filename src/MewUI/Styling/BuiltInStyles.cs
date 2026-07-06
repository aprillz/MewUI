using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides built-in named styles that can be referenced via <see cref="Control.StyleName"/>.
/// These styles are automatically registered in the application-level <see cref="StyleSheet"/>.
/// </summary>
public static class BuiltInStyles
{
    /// <summary>StyleName key for a flat (borderless) button.</summary>
    public const string FlatButton = "flat-button";

    /// <summary>StyleName key for an accent-colored button.</summary>
    public const string AccentButton = "accent-button";

    /// <summary>StyleName key for a ComboBox dropdown list popup.</summary>
    public const string ComboBoxPopup = "combobox-popup";

    /// <summary>StyleName key for a DatePicker calendar popup.</summary>
    public const string DatePickerPopup = "datepicker-popup";

    internal static void Register(StyleSheet sheet)
    {
        sheet.Define(FlatButton, CreateFlatButtonStyle());
        sheet.Define(AccentButton, CreateAccentButtonStyle());
        sheet.Define(ComboBoxPopup, CreateComboBoxPopupStyle());
        sheet.Define(DatePickerPopup, CreateDatePickerPopupStyle());
    }

    private static Style CreateFlatButtonStyle()
    {
        return new Style(typeof(Button))
        {
            BasedOn = Style.ForType<Button>(),
            Transitions =
            [
                Transition.Create(Control.BackgroundProperty),
                Transition.Create(Control.ForegroundProperty),
            ],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground.WithAlpha(0)),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground.WithAlpha(128)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonDisabledBackground.WithAlpha(128)),
                        Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };
    }

    private static Style CreateComboBoxPopupStyle()
    {
        return new Style(typeof(ListBox))
        {
            BasedOn = Style.ForType<ScrollableItemsBase>(),
            Setters =
            [
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)),
            ],
        };
    }

    private static Style CreateDatePickerPopupStyle()
    {
        return new Style(typeof(Calendar))
        {
            BasedOn = Style.ForType<Calendar>(),
            Setters =
            [
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)),
            ],
        };
    }

    private static Style CreateAccentButtonStyle()
    {
        return new Style(typeof(Button))
        {
            BasedOn = Style.ForType<Button>(),
            Transitions =
            [
                Transition.Create(Control.BackgroundProperty),
                Transition.Create(Control.ForegroundProperty),
            ],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent),
                Setter.Create(Control.ForegroundProperty, t => t.Palette.AccentText),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonDisabledBackground),
                        Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };
    }
}
