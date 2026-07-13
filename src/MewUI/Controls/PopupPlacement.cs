namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared math for placing drop-down/tooltip popups relative to an anchor within a placement region:
/// the owner window's client area for in-surface popups, or the monitor work area for natively hosted
/// popups (see Window.GetPopupPlacementRegion). Keeps horizontal clamping and vertical flip behavior
/// identical across the popup-owning controls.
/// </summary>
internal static class PopupPlacement
{
    /// <summary>
    /// Clamps a candidate x so a popup of the given width stays within the region.
    /// When <paramref name="floorToLeftEdge"/> is set, also floors the result to the region's left edge
    /// (DropDownBase/ComboBox clamp a possibly-outside anchor this way; DatePicker/ColorPicker/
    /// ContextMenu/tooltips do not).
    /// </summary>
    public static double ClampHorizontal(double anchorX, double width, Rect region, bool floorToLeftEdge)
    {
        double x = anchorX;
        if (x + width > region.Right)
        {
            x = Math.Max(region.X, region.Right - width);
        }

        if (floorToLeftEdge && x < region.X)
        {
            x = region.X;
        }

        return x;
    }

    /// <summary>
    /// Resolves vertical placement by preferring below the anchor when it fully fits the desired height
    /// or has more space than above; otherwise flips above. Used by DatePicker and ColorPicker.
    /// </summary>
    public static (double y, double height) ResolveVerticalPreferBelowIfFits(
        double anchorY, double belowY, Rect region, double desiredHeight)
    {
        double availableBelow = Math.Max(0, region.Bottom - belowY);
        double availableAbove = Math.Max(0, anchorY - region.Y);

        double y;
        double height;
        if (availableBelow >= desiredHeight || availableBelow >= availableAbove)
        {
            y = belowY;
            height = Math.Min(desiredHeight, availableBelow);
        }
        else
        {
            height = Math.Min(desiredHeight, availableAbove);
            y = anchorY - height;
        }

        return (y, height);
    }
}
