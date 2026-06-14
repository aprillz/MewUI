using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Extended;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Theme-aware styles for the MewDock view controls (ported from the golden-layout MewDock's DockStyles):
/// theme-bound <see cref="Setter"/>s + state <see cref="StateTrigger"/>s registered as type rules on a
/// <see cref="StyleSheet"/>. Colours come from <see cref="Theme.Palette"/> so the dock follows the active theme
/// and recolours live. Set the sheet on <see cref="FlexLayoutView"/> so every descendant is styled.
/// </summary>
internal static class DockStyles
{
    private static readonly Transition[] ColorTransitions =
    [
        Transition.Create(Control.BackgroundProperty),
        Transition.Create(Control.ForegroundProperty),
        Transition.Create(Control.BorderBrushProperty),
    ];

    public static StyleSheet CreateStyleSheet()
    {
        var sheet = new StyleSheet();
        sheet.Define<FlexTabButton>(BuildTabButtonStyle(typeof(FlexTabButton)));
        sheet.Define<FlexBorderButton>(BuildTabButtonStyle(typeof(FlexBorderButton)));
        sheet.Define<FlexSplitter>(CreateSplitterStyle());
        sheet.Define<FlexTabSetView>(CreateTabSetStyle());
        // TODO(layering): move to the Extended layer when the docking styles are extracted there.
        sheet.Define<ExtendedBorderBar>(CreateBorderBarStyle());
        return sheet;
    }

    // The auto-hide revealed pane frame: same accent-on-focus border + transition as a Tool/Document tabset.
    private static Style CreateBorderBarStyle() => new(typeof(ExtendedBorderBar))
    {
        Transitions = [Transition.Create(Control.BorderBrushProperty)],
        Setters =
        [
            Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Focused,
                Setters = [Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.75))],
            },
        ],
    };

    // The pane content frame: a themed border that highlights toward the accent when the tabset is active.
    private static Style CreateTabSetStyle() => new(typeof(FlexTabSetView))
    {
        Transitions = [Transition.Create(Control.BorderBrushProperty)],
        Setters =
        [
            // Match the selected tab's background so the active tab opens seamlessly into the content frame.
            Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ContainerBackground),
            Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder),
            Setter.Create(Control.CornerRadiusProperty, (Theme t) => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, (Theme t) => t.Metrics.ControlBorderThickness),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Focused,
                Setters = [Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.75))],
            },
        ],
    };

    // The splitter: transparent at rest with a theme-tinted grip; the accent tints in on hover and drag.
    private static Style CreateSplitterStyle() => new(typeof(FlexSplitter))
    {
        Transitions = ColorTransitions,
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, Color.Transparent),
            Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.15)),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters =
                [
                    Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent.WithAlpha(26)),
                    Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.35)),
                ],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters =
                [
                    Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent.WithAlpha(48)),
                    Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.65)),
                ],
            },
        ],
    };

    // The tab button: document-tab visuals; the selected tab takes the container background so it reads as
    // continuous with the content below, and its border highlights toward the accent when the tabset is active.
    private static Style BuildTabButtonStyle(Type type) => new(type)
    {
        Transitions = ColorTransitions,
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ButtonFace),
            Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder),
            Setter.Create(Control.ForegroundProperty, (Theme t) => t.Palette.WindowText),
            Setter.Create(Control.PaddingProperty, new Thickness(8, 2, 8, 2)),
            Setter.Create(Control.CornerRadiusProperty, (Theme t) => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, (Theme t) => t.Metrics.ControlBorderThickness),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters =
                [
                    Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ButtonHoverBackground),
                    Setter.Create(Control.BorderBrushProperty, (Theme t) => Color.Composite(t.Palette.ControlBorder, t.Palette.AccentBorderHotOverlay)),
                ],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected,
                Setters = [Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ContainerBackground)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected,
                Exclude = VisualStateFlags.Focused,
                Setters = [Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected | VisualStateFlags.Focused,
                Setters = [Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.ControlBorder.Lerp(t.Palette.Accent, 0.75))],
            },
        ],
    };
}
