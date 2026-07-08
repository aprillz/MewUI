namespace Aprillz.MewUI;

/// <summary>
/// Arguments raised when a drag session that originated from this element has ended.
/// </summary>
public sealed class DragCompletedEventArgs
{
    /// <summary>
    /// Gets the effect chosen by the drop target (<see cref="DragDropEffects.None"/> if rejected or canceled).
    /// </summary>
    public DragDropEffects FinalEffect { get; }

    /// <summary>
    /// Gets whether the drag session was canceled (Esc, source destroyed, etc.) without reaching a drop target.
    /// Distinct from a release over empty space, which reports <see cref="WasCanceled"/> = false with
    /// <see cref="FinalEffect"/> = <see cref="DragDropEffects.None"/>.
    /// </summary>
    public bool WasCanceled { get; }

    /// <summary>The cursor's screen position (pixels) when the drag ended. Lets a source react to a release
    /// over empty space (no drop target) - e.g. spawn a window there. Default when canceled.</summary>
    public Point ScreenPosition { get; }

    public DragCompletedEventArgs(DragDropEffects finalEffect, bool wasCanceled, Point screenPosition = default)
    {
        FinalEffect = finalEffect;
        WasCanceled = wasCanceled;
        ScreenPosition = screenPosition;
    }
}
