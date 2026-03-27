using Aprillz.MewUI.Controls;


namespace Aprillz.MewUI;

public partial record class Theme
{
    private static readonly Transition[] ColorTransitions =
    [
        Transition.Create(Control.BackgroundProperty),
        Transition.Create(Control.BorderBrushProperty),
        Transition.Create(Control.ForegroundProperty),
    ];

    private static readonly Transition[] SliderColorTransitions =
    [
        ..ColorTransitions,
        Transition.Create(Slider.ThumbBrushProperty),
        Transition.Create(Slider.ThumbBorderBrushProperty),
    ];

    private static readonly Transition[] ToggleSwitchColorTransitions =
    [
        ..ColorTransitions,
        Transition.Create(ToggleSwitch.ThumbBrushProperty),
    ];

    private static Dictionary<Type, Style>? _styles;

    /// <summary>
    /// Gets the default style for the specified control type, or null if none registered.
    /// </summary>
    public Style? GetStyle(Type controlType) => GetStyleStatic(controlType);

    /// <summary>
    /// Gets the default style without requiring a Theme instance.
    /// Styles are static (shared across all themes) and use Func&lt;Theme, T&gt; for dynamic values.
    /// </summary>
    internal static Style? GetStyleStatic(Type controlType)
    {
        _styles ??= BuildDefaultStyles();
        return _styles.GetValueOrDefault(controlType);
    }

    /// <summary>
    /// Registers an additional default style. Can be used by application code to add
    /// styles for custom control types.
    /// </summary>
    public void RegisterStyle(Style style)
    {
        _styles ??= BuildDefaultStyles();
        _styles[style.TargetType] = style;
    }

    private static Dictionary<Type, Style> BuildDefaultStyles()
    {
        var styles = new Dictionary<Type, Style>();

        // Base
        Register(styles, CreateControlBaseStyle());

        // Button-like
        Register(styles, CreateButtonStyle());

        Register(styles, CreateToggleButtonStyle());

        Register(styles, CreateDropDownBaseStyle());

        Register(styles, CreateTabHeaderButtonStyle());

        Register(styles, CreateMenuBarStyle());

        // Input controls
        Register(styles, CreateTextBaseStyle());

        Register(styles, CreateControlBasedStyle(typeof(CheckBox),
            Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateControlBasedStyle(typeof(RadioButton),
            Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateToggleSwitchStyle());

        Register(styles, CreateControlBasedStyle(typeof(NumericUpDown),
            Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
            Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateProgressBarStyle());

        Register(styles, CreateSliderStyle());

        // List / item controls
        Register(styles, CreateControlBasedStyle(typeof(ItemsControl),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateControlBasedStyle(typeof(VirtualizedItemsBase),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateControlBasedStyle(typeof(TreeView),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateControlBasedStyle(typeof(GridView),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        // Popups
        Register(styles, CreateControlBasedStyle(typeof(ContextMenu),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        Register(styles, CreateControlBasedStyle(typeof(ToolTip),
            Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness)));

        // Containers
        Register(styles, CreateExpanderStyle());
        Register(styles, CreateContainerStyle(typeof(GroupBox)));

        Register(styles, CreateTabControlStyle());

        Register(styles, CreateWindowStyle());

        // Misc
        Register(styles, CreateBorderStyle());

        Register(styles, CreateSplitterThumbStyle());

        Register(styles, CreateScrollBarStyle());

        return styles;
    }

    private static void Register(Dictionary<Type, Style> styles, Style style)
    {
        styles[style.TargetType] = style;
    }

    private static Style CreateControlBaseStyle()
    {
        return new Style(typeof(Control))
        {
            Setters =
            [
                // Foreground inherited from Window style — not set here.
                // Disabled Foreground is handled by individual control styles, not here,
                // to avoid propagating DisabledText to child TextBlocks via inheritance.
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
        };
    }

    private static Style CreateControlBasedStyle(Type targetType, params SetterBase[] extraSetters)
    {
        return new Style(targetType)
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                // Foreground inherited from Window style
                ..extraSetters,
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.DisabledControlBackground),
                        Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };
    }

    private static Style CreateBorderStyle()
    {
        return new Style(typeof(Border))
        {
            Setters =
            [
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
        };
    }

    private static Style CreateContainerStyle(Type targetType, params SetterBase[] extraSetters)
    {
        return new Style(targetType)
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.PaddingProperty, new Thickness(8)),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
                ..extraSetters,
            ],
        };
    }

    private static Style CreateWindowStyle()
    {
        return new Style(typeof(Window))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.WindowBackground),
                Setter.Create(Control.ForegroundProperty, t => t.Palette.WindowText),
                Setter.Create(Control.FontFamilyProperty, t => t.Metrics.FontFamily),
                Setter.Create(Control.FontSizeProperty, t => t.Metrics.FontSize),
                Setter.Create(Control.FontWeightProperty, t => t.Metrics.FontWeight),
                Setter.Create(Control.PaddingProperty, new Thickness(8)),
            ],
        };
    }

    private static Style CreateSplitterThumbStyle()
    {
        return new Style(typeof(SplitPanel.SplitterThumb))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, Color.Transparent),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.15)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(26)),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.35)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(48)),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.65)),
                    ],
                },
            ],
        };
    }

    private static Style CreateScrollBarStyle()
    {
        return new Style(typeof(ScrollBar))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ScrollBarThumb),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ScrollBarThumbHover)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ScrollBarThumbActive)],
                },
            ],
        };
    }

    private static Style CreateToggleSwitchStyle()
    {
        return new Style(typeof(ToggleSwitch))
        {
            Transitions = ToggleSwitchColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(ToggleSwitch.ThumbBrushProperty, t => t.Palette.WindowText),
                Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                // Checked
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent),
                        Setter.Create(ToggleSwitch.ThumbBrushProperty, t => t.Palette.AccentText),
                    ],
                },
                // Hot (unchecked)
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Exclude = VisualStateFlags.Checked,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace.Lerp(t.Palette.Accent, 0.08)),
                        Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                    ],
                },
                // Hot + Checked
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot | VisualStateFlags.Checked,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.Lerp(t.Palette.ControlBackground, 0.10))],
                },
                // Pressed (unchecked)
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Exclude = VisualStateFlags.Checked,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace.Lerp(t.Palette.Accent, 0.12))],
                },
                // Pressed + Checked
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed | VisualStateFlags.Checked,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.Lerp(t.Palette.ControlBackground, 0.06))],
                },
                // Focused (unchecked)
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Exclude = VisualStateFlags.Checked,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                // Focused + Checked
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused | VisualStateFlags.Checked,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                // Disabled
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonDisabledBackground),
                        Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText),
                        Setter.Create(ToggleSwitch.ThumbBrushProperty, t => t.Palette.DisabledText),
                    ],
                },
                // Disabled + Checked
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.DisabledAccent),
                        Setter.Create(ToggleSwitch.ThumbBrushProperty, t => t.Palette.DisabledControlBackground),
                    ],
                },
            ],
        };
    }

    private static Style CreateSliderStyle()
    {
        // No triggers — thumb uses PickAccentBorder with its own thumbState (includes _isDragging).
        return new Style(typeof(Slider))
        {
            Transitions = SliderColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Slider.ThumbBrushProperty, t => t.Palette.ControlBackground),
                Setter.Create(Slider.ThumbBorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Slider.ThumbBorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Slider.ThumbBorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(Slider.ThumbBorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonDisabledBackground),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                        Setter.Create(Slider.ThumbBrushProperty, t => t.Palette.DisabledControlBackground),
                        Setter.Create(Slider.ThumbBorderBrushProperty, t => t.Palette.ControlBorder),
                    ],
                },
            ],
        };
    }

    private static Style CreateProgressBarStyle()
    {
        return new Style(typeof(ProgressBar))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
        };
    }

    private static Style CreateExpanderStyle()
    {
        return new Style(typeof(Expander))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, Color.Transparent),
                Setter.Create(Control.BorderBrushProperty, Color.Transparent),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters = [Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText)],
                },
            ],
        };
    }

    private static Style CreateTabControlStyle()
    {
        return new Style(typeof(TabControl))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.PaddingProperty, new Thickness(8)),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5))],
                },
            ],
        };
    }

    private static Style CreateMenuBarStyle()
    {
        return new Style(typeof(MenuBar))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            ],
        };
    }

    private static Style CreateButtonStyle()
    {
        return new Style(typeof(Button))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                // Hot
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground),
                        Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                    ],
                },
                // Focused (border only)
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                // Pressed
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent),
                    ],
                },
                // Disabled
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

    private static Style CreateToggleButtonStyle()
    {
        return new Style(typeof(ToggleButton))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                // Unchecked states
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground),
                        Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent),
                    ],
                },
                // Checked states
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonFace, t.Palette.Accent.WithAlpha(96)))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked | VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonHoverBackground, t.Palette.Accent.WithAlpha(96))),
                        Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked | VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked | VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonPressedBackground, t.Palette.Accent.WithAlpha(96))),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent),
                    ],
                },
                // Disabled
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
                new StateTrigger
                {
                    Match = VisualStateFlags.Checked,
                    Exclude = VisualStateFlags.Enabled,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonFace, t.Palette.WindowText.WithAlpha(48)))],
                },
            ],
        };
    }

    private static Style CreateTextBaseStyle()
    {
        return new Style(typeof(TextBase))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                // Disabled
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.DisabledControlBackground),
                        Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };
    }

    private static Style CreateDropDownBaseStyle()
    {
        return new Style(typeof(DropDownBase))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground),
                        Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Active,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent),
                    ],
                },
                // Disabled
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

    private static Style CreateTabHeaderButtonStyle()
    {
        return new Style(typeof(TabHeaderButton))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground),
                        Setter.Create(Control.BorderBrushProperty, t => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                    ],
                },
                // Selected
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Exclude = VisualStateFlags.Focused,
                    Setters =
                    [
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
        
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected | VisualStateFlags.Focused,
                    Setters =
                    [
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)),
        
                    ],
                },
                // Disabled (non-selected)
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
                // Disabled + Selected
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
                        Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                        Setter.Create(Control.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };
    }
}
