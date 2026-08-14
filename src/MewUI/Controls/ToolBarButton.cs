namespace Aprillz.MewUI.Controls;

/// <summary>
/// A command entry's button. Flat: the toolbar owns the background, so the button contributes only its
/// hover and press fill.
/// </summary>
internal sealed class ToolBarButton : Button
{
    // Removes beforefieldinit so the registration below runs on first instantiation rather than on
    // first static field access, which instantiating alone does not guarantee.
    static ToolBarButton() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToolBarButton>(DefaultStyles.CreateToolBarButtonStyle);
}

/// <summary>
/// The plate behind one group. It is an element with its own default style rather than something the
/// toolbar paints, so a theme or an application style reaches its background and corners.
/// </summary>
internal sealed class ToolBarGroupPlate : Control
{
    static ToolBarGroupPlate() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToolBarGroupPlate>(DefaultStyles.CreateToolBarGroupPlateStyle);

    internal ToolBarGroupPlate() => IsHitTestVisible = false;

    protected override void OnRender(Rendering.IGraphicsContext context)
        => DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds), Background, BorderBrush,
            BorderThickness, CornerRadius);
}

/// <summary>
/// A label entry. A control rather than the text alone, so that dimming while the toolbar is out of reach
/// is a style state like every other part's, and follows a theme change with them.
/// </summary>
internal sealed class ToolBarLabel : Label
{
    static ToolBarLabel() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToolBarLabel>(DefaultStyles.CreateToolBarLabelStyle);

    internal ToolBarLabel() => IsHitTestVisible = false;
}

/// <summary>A toggle entry's button. Flat like <see cref="ToolBarButton"/>, and stays filled while on.</summary>
internal sealed class ToolBarToggleButton : ToggleButton
{
    static ToolBarToggleButton() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToolBarToggleButton>(DefaultStyles.CreateToolBarToggleButtonStyle);
}
