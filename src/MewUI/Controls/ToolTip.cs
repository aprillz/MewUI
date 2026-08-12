using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tooltip popup control. Content is set via <see cref="ContentControl.Content"/>.
/// For simple text tooltips, use the <c>ToolTip(string)</c> extension method which creates a TextBlock.
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
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = LayoutRounding.RoundToPixel(CornerRadius, dpiScale);

        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, radius);
    }
}
