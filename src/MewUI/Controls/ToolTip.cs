using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tooltip popup control. Content is set via <see cref="ContentControl.Content"/>.
/// For simple text tooltips, use the <see cref="ControlExtensions.ToolTip{T}(T, string)"/> extension method which creates a TextBlock.
/// </summary>
public sealed partial class ToolTip : ContentControl
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ToolTip>(DefaultStyles.CreateToolTipStyle);

    static ToolTip()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<ToolTip>(false);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the built-in chrome would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = LayoutRounding.RoundToPixel(CornerRadius, dpiScale);

        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, radius);
    }
}
