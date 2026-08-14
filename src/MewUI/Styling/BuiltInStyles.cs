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

    /// <summary>
    /// StyleName key for an accent-colored split button. The accent is carried by the chrome the two
    /// faces sit on; the hairline between them keeps its own color.
    /// </summary>
    public static string AccentSplitButton
    {
        get
        {
            FrameworkNamedStyles.Register("accent-split-button", CreateAccentSplitButtonStyle);
            return "accent-split-button";
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

    private static readonly (string Name, Func<Style> Factory)[] _all =
    [
        ("flat-button", CreateFlatButtonStyle),
        ("accent-button", CreateAccentButtonStyle),
        ("accent-split-button", CreateAccentSplitButtonStyle),
        ("combobox-popup", CreateComboBoxPopupStyle),
        ("datepicker-popup", CreateDatePickerPopupStyle),
    ];

    internal static void Register(StyleSheet sheet)
    {
        foreach (var (name, factory) in _all)
        {
            sheet.Define(name, factory);
        }
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

    internal static Style CreateAccentSplitButtonStyle()
    {
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        DefaultStyles.EnsureRegistered<SplitButton>(DefaultStyles.CreateSplitButtonStyle);
        return Style.DeriveFromRegisteredDefault(typeof(SplitButton),
            transitions:
            [
                Transition.Create(Control.BackgroundProperty),
                Transition.Create(TextElement.ForegroundProperty),
            ],
            setters:
            [
                // The chrome, not the faces: the faces are transparent at rest, so this is what shows.
                Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent),
                Setter.Create(TextElement.ForegroundProperty, t => t.Palette.AccentText),
                Setter.Create(Control.BorderThicknessProperty, 0.0),

                // The template carries the face look: the two faces are controls of their own, and this is
                // how a chrome color reaches them without a style key anyone else could take.
                Setter.Create(
                    Control.TemplateProperty,
                    (ControlTemplate?)SplitButtonTemplate.WithFaceStyle(
                        CreateAccentSplitFaceStyle(),
                        static t => t.AccentText.WithAlpha(64))),
            ],
            triggers:
            [
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

    internal static Style CreateAccentSplitFaceStyle()
    {
        DefaultStyles.EnsureRegistered<Control>(DefaultStyles.CreateControlBaseStyle);
        DefaultStyles.EnsureRegistered<Button>(DefaultStyles.CreateButtonStyle);
        return Style.DeriveFromRegisteredDefault(typeof(Button),
            transitions: [Transition.Create(Control.BackgroundProperty)],
            setters:
            [
                // The hover hue at zero alpha rather than Color.Transparent, which is white and would
                // flash bright over the accent on the way in and out.
                Setter.Create(Control.BackgroundProperty,
                    t => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15).WithAlpha(0)),
                Setter.Create(TextElement.ForegroundProperty, t => t.Palette.AccentText),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            triggers:
            [
                // Only the hovered half reacts, and it reacts within the accent rather than reverting to
                // the button face the default drop-down face would paint.
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty,
                            t => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty,
                            t => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25)),
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
