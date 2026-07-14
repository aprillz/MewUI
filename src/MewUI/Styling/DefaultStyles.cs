using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Built-in default styles for framework controls.
/// </summary>
public static class DefaultStyles
{
    // Style resolution can run on multiple UI threads (per-window loops, parallel tests);
    // the cache must not be mutated concurrently.
    private static readonly object _stylesLock = new();
    private static Dictionary<Type, Style>? _styles;

    private static IReadOnlyDictionary<Type, Func<Style>> StyleFactories =>
        field ??= CreateStyleFactories();

    private static Transition[] ColorTransitions =>
        field ??=
        [
            Transition.Create(Control.BackgroundProperty),
            Transition.Create(Control.BorderBrushProperty),
            Transition.Create(TextElement.ForegroundProperty),
        ];

    private static Transition[] SliderColorTransitions =>
        field ??=
        [
            ..ColorTransitions,
            Transition.Create(Slider.ThumbBrushProperty),
            Transition.Create(Slider.ThumbBorderBrushProperty),
        ];

    private static Transition[] ToggleSwitchColorTransitions =>
        field ??=
        [
            ..ColorTransitions,
            Transition.Create(ToggleSwitch.ThumbBrushProperty),
        ];

    private static IReadOnlyDictionary<Type, Func<Style>> CreateStyleFactories()
    {
        return new Dictionary<Type, Func<Style>>
        {
            [typeof(Control)] = CreateControlBaseStyle,
            [typeof(Button)] = CreateButtonStyle,
            [typeof(ToggleButton)] = CreateToggleButtonStyle,
            [typeof(DropDownBase)] = CreateDropDownBaseStyle,
            [typeof(TabHeaderButton)] = CreateTabHeaderButtonStyle,
            [typeof(SegmentedControl)] = CreateSegmentedControlStyle,
            [typeof(SegmentButton)] = CreateSegmentButtonStyle,
            [typeof(ButtonGroup)] = CreateButtonGroupStyle,
            [typeof(MenuBar)] = CreateMenuBarStyle,
            [typeof(TextBase)] = CreateTextBaseStyle,
            [typeof(CheckBox)] = CreateCheckBoxStyle,
            [typeof(RadioButton)] = CreateRadioButtonStyle,
            [typeof(ToggleSwitch)] = CreateToggleSwitchStyle,
            [typeof(NumericUpDown)] = CreateNumericUpDownStyle,
            [typeof(ProgressBar)] = CreateProgressBarStyle,
            [typeof(Slider)] = CreateSliderStyle,
            [typeof(ItemsControl)] = CreateItemsControlStyle,
            [typeof(ScrollableItemsBase)] = CreateScrollableItemsBaseStyle,
            [typeof(TreeView)] = CreateTreeViewStyle,
            [typeof(GridView)] = CreateGridViewStyle,
            [typeof(NavigationView)] = CreateNavigationViewStyle,
            [typeof(ContextMenu)] = CreateContextMenuStyle,
            [typeof(ToolTip)] = CreateToolTipStyle,
            [typeof(Expander)] = CreateExpanderStyle,
            [typeof(GroupBox)] = CreateGroupBoxStyle,
            [typeof(TabControl)] = CreateTabControlStyle,
            [typeof(Window)] = CreateWindowStyle,
            [typeof(Calendar)] = CreateCalendarStyle,
            [typeof(Border)] = CreateBorderStyle,
            [typeof(SplitPanel.SplitterThumb)] = CreateSplitterThumbStyle,
            [typeof(ScrollBar)] = CreateScrollBarStyle,
        };
    }

    /// <summary>
    /// Gets the default style for the specified control type, or null if none registered.
    /// </summary>
    public static Style? GetStyle(Type controlType)
    {
        lock (_stylesLock)
        {
            _styles ??= new Dictionary<Type, Style>();
            if (_styles.TryGetValue(controlType, out var style))
            {
                return style;
            }

            if (!StyleFactories.TryGetValue(controlType, out var factory))
            {
                return null;
            }

            style = factory();
            _styles[controlType] = style;
            return style;
        }
    }

    private static Style CreateControlBaseStyle() =>
        new(typeof(Control))
        {
            Setters =
            [
                // Foreground inherited from Window style ??not set here.
                // Disabled Foreground is handled by individual control styles, not here,
                // to avoid propagating DisabledText to child TextBlocks via inheritance.
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
        };

    private static Style CreateControlBasedStyle(Type targetType, params SetterBase[] extraSetters) =>
        new(targetType)
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };

    private static Style CreateCheckBoxStyle()
        => CreateControlBasedStyle(typeof(CheckBox),
            Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateRadioButtonStyle()
        => CreateControlBasedStyle(typeof(RadioButton),
            Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateNumericUpDownStyle()
        => CreateControlBasedStyle(typeof(NumericUpDown),
            Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
            Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            Setter.Create(Control.TemplateProperty, (ControlTemplate?)NumericUpDownTemplate.Instance));

    private static Style CreateItemsControlStyle()
        => CreateControlBasedStyle(typeof(ItemsControl),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateScrollableItemsBaseStyle()
        => CreateControlBasedStyle(typeof(ScrollableItemsBase),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateTreeViewStyle()
        => CreateControlBasedStyle(typeof(TreeView),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateGridViewStyle()
        => CreateControlBasedStyle(typeof(GridView),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateNavigationViewStyle() =>
        new(typeof(NavigationView))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
            ],
        };

    private static Style CreateContextMenuStyle() =>
        new(typeof(ContextMenu))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(TextElement.ForegroundProperty, t => t.Palette.WindowText),
                Setter.Create(TextElement.FontFamilyProperty, t => t.Metrics.FontFamily),
                Setter.Create(TextElement.FontSizeProperty, t => t.Metrics.FontSize),
                Setter.Create(TextElement.FontWeightProperty, t => t.Metrics.FontWeight),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.5)),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
        };

    private static Style CreateToolTipStyle()
        => CreateControlBasedStyle(typeof(ToolTip),
            Setter.Create(Control.PaddingProperty, new Thickness(8, 4, 8, 4)),
            Setter.Create(TextElement.ForegroundProperty, t => t.Palette.WindowText),
            Setter.Create(TextElement.FontFamilyProperty, t => t.Metrics.FontFamily),
            Setter.Create(TextElement.FontSizeProperty, t => t.Metrics.FontSize),
            Setter.Create(TextElement.FontWeightProperty, t => t.Metrics.FontWeight),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateGroupBoxStyle()
        => CreateContainerStyle(typeof(GroupBox));

    private static Style CreateCalendarStyle()
        => CreateControlBasedStyle(typeof(Calendar),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness));

    private static Style CreateBorderStyle() =>
        new(typeof(Border))
        {
            Setters =
            [
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
        };

    private static Style CreateContainerStyle(Type targetType, params SetterBase[] extraSetters) =>
        new(targetType)
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.PaddingProperty, t=>t.Metrics.ContainerPadding),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
                ..extraSetters,
            ],
        };

    private static Style CreateWindowStyle() =>
        new(typeof(Window))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.WindowBackground),
                Setter.Create(TextElement.ForegroundProperty, t => t.Palette.WindowText),
                Setter.Create(TextElement.FontFamilyProperty, t => t.Metrics.FontFamily),
                Setter.Create(TextElement.FontSizeProperty, t => t.Metrics.FontSize),
                Setter.Create(TextElement.FontWeightProperty, t => t.Metrics.FontWeight),
                Setter.Create(Control.PaddingProperty, t=>t.Metrics.ContainerPadding),
            ],
        };

    private static Style CreateSplitterThumbStyle() =>
        new(typeof(SplitPanel.SplitterThumb))
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

    private static Style CreateScrollBarStyle() =>
        new(typeof(ScrollBar))
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

    private static Style CreateToggleSwitchStyle() =>
        new(typeof(ToggleSwitch))
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
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

    // No triggers - thumb uses PickAccentBorder with its own thumbState (includes _isDragging).

    private static Style CreateSliderStyle() =>
        new(typeof(Slider))
        {
            Transitions = SliderColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Slider.ThumbBrushProperty, t => t.Palette.AccentText),
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
                        Setter.Create(Slider.ThumbBrushProperty, t => t.Palette.ButtonDisabledBackground),
                        Setter.Create(Slider.ThumbBorderBrushProperty, t => t.Palette.ControlBorder),
                    ],
                },
            ],
        };

    private static Style CreateProgressBarStyle() =>
        new(typeof(ProgressBar))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ControlBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
        };

    private static Style CreateExpanderStyle() =>
        new(typeof(Expander))
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
                    Setters = [Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText)],
                },
            ],
        };

    private static Style CreateTabControlStyle() =>
        new(typeof(TabControl))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.PaddingProperty, t=>t.Metrics.ContainerPadding),
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

    private static Style CreateSegmentedControlStyle() =>
        new(typeof(SegmentedControl))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.PaddingProperty, Thickness.Zero),
                // The control owns the height; segments stretch to fill it (segments carry no min-height).
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            Triggers =
            [
                // Focus ring on the container.
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
            ],
        };

    private static Style CreateSegmentButtonStyle() =>
        new(typeof(SegmentButton))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                // Matches the other button-like controls; the container shares the same face.
                // Padding comes from SegmentedControl.ItemPadding; height follows the control.
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(TextElement.ForegroundProperty, t => t.Palette.WindowText),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground)],
                },
                // Keyboard focus marks the focused segment with an accent-tinted face. Only ButtonGroup
                // makes segments focusable; SegmentedControl segments are non-focusable so this never shows.
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonFace, t.Palette.Accent.WithAlpha(72)))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused | VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonHoverBackground, t.Palette.Accent.WithAlpha(72)))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground)],
                },
                // Selected: accent-tinted face, matching ToggleButton's Checked state
                // (a segmented control is a mutually exclusive toggle group). Foreground stays WindowText.
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonFace, t.Palette.Accent.WithAlpha(96)))],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected | VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonHoverBackground, t.Palette.Accent.WithAlpha(96)))],
                },
                // Disabled (non-selected).
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
                // Disabled + Selected ??mirror ToggleButton's disabled-checked face.
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Exclude = VisualStateFlags.Enabled,
                    Setters = [Setter.Create(Control.BackgroundProperty, t => Color.Composite(t.Palette.ButtonFace, t.Palette.WindowText.WithAlpha(48)))],
                },
            ],
        };

    private static Style CreateButtonGroupStyle() =>
        new(typeof(ButtonGroup))
        {
            Transitions = ColorTransitions,
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.PaddingProperty, Thickness.Zero),
                Setter.Create(FrameworkElement.MinHeightProperty, t => t.Metrics.BaseControlHeight),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
            ],
            // No container focus trigger: each segment is its own Tab stop and marks its own focus
            // (accent-tinted face). A container border ring would double-indicate and hide which
            // segment is focused.
        };

    private static Style CreateMenuBarStyle() =>
        new(typeof(MenuBar))
        {
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),

                Setter.Create(Control.PaddingProperty, new Thickness(4, 2, 4, 2)),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            ],
        };

    private static Style CreateButtonStyle() =>
        new(typeof(Button))
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };

    private static Style CreateToggleButtonStyle() =>
        new(typeof(ToggleButton))
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
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

    private static Style CreateTextBaseStyle() =>
        new(typeof(TextBase))
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };

    private static Style CreateDropDownBaseStyle() =>
        new(typeof(DropDownBase))
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };

    private static Style CreateTabHeaderButtonStyle() =>
        new(typeof(TabHeaderButton))
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
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
                        Setter.Create(TextElement.ForegroundProperty, t => t.Palette.DisabledText),
                    ],
                },
            ],
        };
}
