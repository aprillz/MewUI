namespace Aprillz.MewUI;

/// <summary>
/// Arguments raised when a drag session that originated from the element has ended.
/// </summary>
public sealed class DragCompletedEventArgs
{
    /// <summary>
    /// Gets the effect chosen by the drop target (<see cref="DragDropEffects.None"/> if rejected or canceled).
    /// </summary>
    public DragDropEffects FinalEffect { get; }

    /// <summary>
    /// Gets whether the drag session was canceled (Esc, source destroyed, etc.) without reaching a drop target.
    /// </summary>
    /// <remarks>
    /// Distinct from a release over empty space, which reports <see cref="WasCanceled"/> = false with
    /// <see cref="FinalEffect"/> = <see cref="DragDropEffects.None"/>.
    /// </remarks>
    public bool WasCanceled { get; }

    /// <summary>
    /// Gets the cursor's screen position in device pixels when the drag ended, or the default when canceled.
    /// </summary>
    /// <remarks>
    /// Lets a source react to a release over empty space (no drop target), e.g. spawn a window there.
    /// </remarks>
    public Point ScreenPosition { get; }

    /// <summary>
    /// Creates arguments with the final effect, whether the session was canceled and the release position.
    /// </summary>
    public DragCompletedEventArgs(DragDropEffects finalEffect, bool wasCanceled, Point screenPosition = default)
    {
        FinalEffect = finalEffect;
        WasCanceled = wasCanceled;
        ScreenPosition = screenPosition;
    }
}
