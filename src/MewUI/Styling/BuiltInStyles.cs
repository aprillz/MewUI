using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides built-in named styles that can be referenced via <see cref="Control.StyleName"/>.
/// These styles are automatically registered in the application-level <see cref="StyleSheet"/>.
/// </summary>
public static class BuiltInStyles
{
    /// <summary>StyleName key for a flat (borderless) button.</summary>
    public static string FlatButton
    {
        get
        {
            FrameworkNamedStyles.Register("flat-button", CreateFlatButtonStyle);
            return "flat-button";
        }
    }

    /// <summary>StyleName key for an accent-colored button.</summary>
    public static string AccentButton
    {
        get
        {
            FrameworkNamedStyles.Register("accent-button", CreateAccentButtonStyle);
            return "accent-button";
        }
    }

    /// <summary>StyleName key for a ComboBox dropdown list popup.</summary>
    public static string ComboBoxPopup
    {
        get
        {
            FrameworkNamedStyles.Register("combobox-popup", CreateComboBoxPopupStyle);
            return "combobox-popup";
        }
    }

    /// <summary>StyleName key for a DatePicker calendar popup.</summary>
    public static string DatePickerPopup
    {
        get
        {
            FrameworkNamedStyles.Register("datepicker-popup", CreateDatePickerPopupStyle);
            return "datepicker-popup";
        }
    }

    internal static void Register(StyleSheet sheet)
    {
        sheet.Define("flat-button", CreateFlatButtonStyle);
        sheet.Define("accent-button", CreateAccentButtonStyle);
        sheet.Define("combobox-popup", CreateComboBoxPopupStyle);
        sheet.Define("datepicker-popup", CreateDatePickerPopupStyle);
    }

    internal static Style CreateFlatButtonStyle()
    {
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        DefaultStyles.EnsureRegistered<Button>(DefaultStyles.CreateButtonStyle);
        return Style.DeriveFromRegisteredDefault(typeof(Button),
            transitions:
            [
                Transition.Create(Control.BackgroundProperty),
                Transition.Create(TextElement.ForegroundProperty),
            ],
            setters:
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground.WithAlpha(0)),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            triggers:
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground.WithAlpha(128)),
                    ],
                },
                // Keyboard focus: a flat button has no chrome, so an accent-tinted face is its focus signal.
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(56)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused | VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(88)),
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ]);
    }

    internal static Style CreateComboBoxPopupStyle()
    {
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        DefaultStyles.EnsureRegistered<ScrollableItemsBase>(DefaultStyles.CreateScrollableItemsBaseStyle);
        DefaultStyles.EnsureRegistered<ListBox>(DefaultStyles.CreateListBoxStyle);
        return Style.DeriveFromRegisteredDefault(typeof(ListBox),
            setters:
            [
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)),
            ]);
    }

    internal static Style CreateDatePickerPopupStyle()
    {
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        DefaultStyles.EnsureRegistered<Calendar>(DefaultStyles.CreateCalendarStyle);
        return Style.DeriveFromRegisteredDefault(typeof(Calendar),
            setters:
            [
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)),
            ]);
    }

    internal static Style CreateAccentButtonStyle()
    {
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        DefaultStyles.EnsureRegistered<Button>(DefaultStyles.CreateButtonStyle);
        return Style.DeriveFromRegisteredDefault(typeof(Button),
            transitions:
            [
                Transition.Create(Control.BackgroundProperty),
                Transition.Create(TextElement.ForegroundProperty),
            ],
            setters:
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent),
                Setter.Create(TextElement.ForegroundProperty, t => t.Palette.AccentText),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            triggers:
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ]);
    }
}
