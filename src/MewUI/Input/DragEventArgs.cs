using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Arguments for drag-and-drop events.
/// </summary>
public sealed class DragEventArgs
{
    private bool _accepted;
    private DragDropEffects _effect;

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
    public bool Accepted
    {
        get => _accepted;
        set
        {
            _accepted = value;
            IsDecided = true;
        }
    }

    /// <summary>
    /// Gets the effects allowed by the drag source.
    /// </summary>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>
    /// Gets or sets the effect chosen by the target.
    /// Must be a subset of <see cref="AllowedEffects"/>; values outside are coerced to <see cref="DragDropEffects.None"/>.
    /// </summary>
    public DragDropEffects Effect
    {
        get => _effect;
        set
        {
            _effect = value;
            IsDecided = true;
        }
    }

    /// <summary>Whether a handler set <see cref="Accepted"/> or <see cref="Effect"/>; the router applies its default otherwise.</summary>
    internal bool IsDecided { get; private set; }

    /// <summary>Sets the router's default answer without counting it as a handler's decision.</summary>
    internal void ApplyDefault(bool accepted, DragDropEffects effect)
    {
        _accepted = accepted;
        _effect = effect;
    }

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
    }
}
