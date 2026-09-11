namespace Aprillz.MewUI.Text;

/// <summary>Numeric limits the text engine enforces when it validates a layout request.</summary>
internal static class TextLayoutLimits
{
    // A quarter of the range keeps every sum with a font metric finite, so no line box can overflow.
    public const double MAX_BASELINE_OFFSET = double.MaxValue / 4;

    /// <summary>True when a baseline offset is finite and small enough to lay out.</summary>
    public static bool IsValidBaselineOffset(double offset)
        => double.IsFinite(offset) && Math.Abs(offset) <= MAX_BASELINE_OFFSET;
}
