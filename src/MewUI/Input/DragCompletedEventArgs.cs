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
    /// </summary>
    public bool WasCanceled { get; }

    public DragCompletedEventArgs(DragDropEffects finalEffect, bool wasCanceled)
    {
        FinalEffect = finalEffect;
        WasCanceled = wasCanceled;
    }
}
