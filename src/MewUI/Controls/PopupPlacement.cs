namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared math for placing drop-down/tooltip popups relative to an anchor within a window's client area.
/// Keeps horizontal clamping and vertical flip behavior identical across the popup-owning controls.
/// </summary>
internal static class PopupPlacement
{
    /// <summary>
    /// Clamps a candidate x so a popup of the given width stays within the client width.
    /// When <paramref name="floorToZero"/> is set, also floors the result to zero (DropDownBase/ComboBox
    /// clamp a possibly-negative anchor this way; DatePicker/ColorPicker/ContextMenu/tooltips do not).
    /// </summary>
    public static double ClampHorizontal(double anchorX, double width, double clientWidth, bool floorToZero)
    {
        double x = anchorX;
        if (x + width > clientWidth)
        {
            x = Math.Max(0, clientWidth - width);
        }

        if (floorToZero && x < 0)
        {
            x = 0;
        }

        return x;
    }

    /// <summary>
    /// Resolves vertical placement by preferring whichever side (below/above the anchor) has more raw
    /// space, then clamping height to that side's available space. Used by DropDownBase and ComboBox.
    /// </summary>
    public static (double y, double height) ResolveVerticalPreferMoreSpace(
        double anchorY, double belowY, double clientHeight, double desiredHeight)
    {
        double availableBelow = Math.Max(0, clientHeight - belowY);
        double availableAbove = Math.Max(0, anchorY);

        double y;
        double height;
        if (availableBelow >= availableAbove)
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

    /// <summary>
    /// Resolves vertical placement by preferring below the anchor when it fully fits the desired height
    /// or has more space than above; otherwise flips above. Used by DatePicker and ColorPicker.
    /// </summary>
    public static (double y, double height) ResolveVerticalPreferBelowIfFits(
        double anchorY, double belowY, double clientHeight, double desiredHeight)
    {
        double availableBelow = Math.Max(0, clientHeight - belowY);
        double availableAbove = Math.Max(0, anchorY);

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
