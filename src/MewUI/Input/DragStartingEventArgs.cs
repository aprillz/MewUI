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

    /// <summary>
    /// Whether this platform can host a transparent top-level overlay for a <see cref="DragPreviewScope.CrossWindow"/>
    /// preview. Optional hint: the framework already degrades to an opaque overlay when this is <see langword="false"/>,
    /// so handlers only need to read this if they want to author a different visual for the opaque case.
    /// </summary>
    public bool SupportsTransparentOverlay { get; }

    public DragStartingEventArgs(Point startPositionInElement, Point startPositionInWindow)
    {
        StartPositionInElement = startPositionInElement;
        StartPositionInWindow = startPositionInWindow;
        SupportsTransparentOverlay = Application.IsRunning && Application.Current.PlatformHost.SupportsTransparentOverlay;
    }
}
