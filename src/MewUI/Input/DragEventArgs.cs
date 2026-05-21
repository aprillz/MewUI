using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Arguments for drag-and-drop events.
/// </summary>
public sealed class DragEventArgs
{
    /// <summary>
    /// Gets the dropped or dragged data payload.
    /// </summary>
    public IDataObject Data { get; }

    /// <summary>
    /// Gets the position relative to the window in DIPs.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the position in screen coordinates in device pixels.
    /// </summary>
    public Point ScreenPosition { get; }

    /// <summary>
    /// Gets or sets whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets or sets whether the current target accepts the drop.
    /// Setting this to <see langword="true"/> implicitly handles the event.
    /// </summary>
    public bool Accepted { get; set; }

    /// <summary>
    /// Gets the effects allowed by the drag source.
    /// </summary>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>
    /// Gets or sets the effect chosen by the target.
    /// Must be a subset of <see cref="AllowedEffects"/>; values outside are coerced to <see cref="DragDropEffects.None"/>.
    /// </summary>
    public DragDropEffects Effect { get; set; }

    public DragEventArgs(IDataObject data, Point position, Point screenPosition)
        : this(data, position, screenPosition, DragDropEffects.Copy)
    {
    }

    public DragEventArgs(IDataObject data, Point position, Point screenPosition, DragDropEffects allowedEffects)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Position = position;
        ScreenPosition = screenPosition;
        AllowedEffects = allowedEffects;
        Effect = DragDropEffects.None;
    }
}
