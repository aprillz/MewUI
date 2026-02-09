namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextViewState
{
    public double HorizontalOffset { get; private set; }
    public double VerticalOffset { get; private set; }

    public bool SetHorizontalOffset(double value, double dpiScale)
    {
        value = SanitizeOffset(value, dpiScale);
        if (HorizontalOffset == value)
        {
            return false;
        }

        HorizontalOffset = value;
        return true;
    }

    public bool SetVerticalOffset(double value, double dpiScale)
    {
        value = SanitizeOffset(value, dpiScale);
        if (VerticalOffset == value)
        {
            return false;
        }

        VerticalOffset = value;
        return true;
    }

    public bool SetScrollOffsets(double horizontal, double vertical, double dpiScale)
    {
        bool changedH = SetHorizontalOffset(horizontal, dpiScale);
        bool changedV = SetVerticalOffset(vertical, dpiScale);
        return changedH || changedV;
    }

    private static double SanitizeOffset(double value, double dpiScale)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return value;
        }

        return LayoutRounding.RoundToPixel(value, dpiScale);
    }
}
