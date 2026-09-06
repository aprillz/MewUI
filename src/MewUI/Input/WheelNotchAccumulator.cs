using System.Diagnostics;

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
    // Gap that separates one swipe from the next. A trackpad reports every 8-16 ms while the
    // fingers move, and a wheel detent carries a whole notch, so neither loses anything to it.
    private const long IDLE_RESET_MS = 200;

    private double _residualX;
    private double _residualY;
    private long _lastTakeTimestamp;

    /// <summary>
    /// Adds a Y-axis notch delta and returns the whole number of notches crossed
    /// since the last emission. Returns 0 when accumulated magnitude is below 1.0.
    /// </summary>
    public int TakeY(double notchesY)
    {
        DropStaleResidual();
        return Take(ref _residualY, notchesY);
    }

    /// <summary>
    /// Adds an X-axis notch delta and returns the whole number of notches crossed
    /// since the last emission. Returns 0 when accumulated magnitude is below 1.0.
    /// </summary>
    public int TakeX(double notchesX)
    {
        DropStaleResidual();
        return Take(ref _residualX, notchesX);
    }

    /// <summary>
    /// Discards any residual fractional notch state.
    /// </summary>
    public void Reset()
    {
        _residualX = 0;
        _residualY = 0;
        _lastTakeTimestamp = 0;
    }

    /// <summary>Starts a new run when this input is too far from the last to belong to it.</summary>
    private void DropStaleResidual()
    {
        long now = Stopwatch.GetTimestamp();
        // A fraction left by a control the pointer merely crossed would otherwise ride along until
        // that control is used again, and a reversal would spend itself paying the old one back.
        if (_lastTakeTimestamp != 0
            && Stopwatch.GetElapsedTime(_lastTakeTimestamp, now).TotalMilliseconds > IDLE_RESET_MS)
        {
            _residualX = 0;
            _residualY = 0;
        }

        _lastTakeTimestamp = now;
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
