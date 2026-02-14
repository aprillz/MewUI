namespace Aprillz.MewUI.Rendering;

internal static class TextMeasurePolicy
{
    public const double WidthPaddingPx = 1.0;

    public static double ApplyWidthPadding(double widthPx)
        => widthPx > 0 ? widthPx + WidthPaddingPx : widthPx;
}
