namespace Aprillz.MewUI.Input;

/// <summary>
/// Accumulates fractional wheel notch deltas across input events and emits
/// whole notch counts. Allows discrete consumers (NumericUpDown, ComboBox, etc.)
/// to receive trackpad / high-resolution mouse input without firing per sub-notch
/// event while preserving the natural "one notch = one step" intent.
/// </summary>
/// <remarks>
/// Sign convention matches <see cref="MouseWheelEventArgs.Delta"/> - positive
/// values represent "toward earlier content" (up / left).
/// </remarks>
internal struct WheelNotchAccumulator
{
    private double _residualX;
    private double _residualY;

    /// <summary>
    /// Adds a Y-axis notch delta and returns the whole number of notches crossed
    /// since the last emission. Returns 0 when accumulated magnitude is below 1.0.
    /// </summary>
    public int TakeY(double notchesY) => Take(ref _residualY, notchesY);

    /// <summary>
    /// Adds an X-axis notch delta and returns the whole number of notches crossed
    /// since the last emission. Returns 0 when accumulated magnitude is below 1.0.
    /// </summary>
    public int TakeX(double notchesX) => Take(ref _residualX, notchesX);

    /// <summary>
    /// Discards any residual fractional notch state.
    /// </summary>
    public void Reset()
    {
        _residualX = 0;
        _residualY = 0;
    }

    private static int Take(ref double residual, double notches)
    {
        residual += notches;
        // (int) truncates toward zero, which is what we want for both signs:
        //  1.7 → 1, leaving 0.7 residual
        // -1.7 → -1, leaving -0.7 residual
        int whole = (int)residual;
        residual -= whole;
        return whole;
    }
}
