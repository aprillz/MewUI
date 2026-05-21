using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Arguments raised when a drag gesture has been detected on an element with <see cref="Controls.UIElement.CanDrag"/>.
/// Handlers populate <see cref="Data"/> and <see cref="AllowedEffects"/> to start a drag session,
/// or set <see cref="Cancel"/> to suppress it.
/// </summary>
public sealed class DragStartingEventArgs
{
    /// <summary>
    /// Gets the position (in element-local DIPs) where the drag candidate began.
    /// </summary>
    public Point StartPositionInElement { get; }

    /// <summary>
    /// Gets the position (in window DIPs) where the drag candidate began.
    /// </summary>
    public Point StartPositionInWindow { get; }

    /// <summary>
    /// Gets or sets the data payload for the drag.
    /// Leave <see langword="null"/> (or set <see cref="Cancel"/>) to skip starting a session.
    /// </summary>
    public IDataObject? Data { get; set; }

    /// <summary>
    /// Gets or sets which effects this source allows. Defaults to <see cref="DragDropEffects.Copy"/>.
    /// </summary>
    public DragDropEffects AllowedEffects { get; set; } = DragDropEffects.Copy;

    /// <summary>
    /// Gets or sets the preview visual to follow the cursor during the drag.
    /// </summary>
    public DragPreviewContent? Preview { get; set; }

    /// <summary>
    /// Set to <see langword="true"/> to cancel starting the drag session.
    /// </summary>
    public bool Cancel { get; set; }

    public DragStartingEventArgs(Point startPositionInElement, Point startPositionInWindow)
    {
        StartPositionInElement = startPositionInElement;
        StartPositionInWindow = startPositionInWindow;
    }
}
