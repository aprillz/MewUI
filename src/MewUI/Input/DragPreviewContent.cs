using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Specifies a visual to follow the cursor during a drag session.
/// </summary>
/// <remarks>
/// One of <see cref="Element"/> or <see cref="Image"/> should be set. If both are present,
/// <see cref="Image"/> wins. If both are <see langword="null"/>, the framework renders a
/// minimal placeholder rectangle sized to <see cref="Size"/>.
/// </remarks>
public sealed class DragPreviewContent
{
    /// <summary>
    /// Gets the element the preview is made from; a detached element is hosted and laid out as the
    /// preview, an attached one is represented by its current bounds.
    /// </summary>
    public UIElement? Element { get; init; }

    /// <summary>
    /// Gets the pre-rendered image used as the drag preview.
    /// </summary>
    public IImage? Image { get; init; }

    /// <summary>
    /// Gets the size in device pixels of the placeholder preview used when neither <see cref="Element"/>
    /// nor <see cref="Image"/> is set; the default is 64 by 64.
    /// </summary>
    public Size Size { get; init; } = new(64, 64);

    /// <summary>
    /// Gets the cursor offset from the preview's top-left corner in DIPs, or <see langword="null"/> to use
    /// <see cref="DragStartingEventArgs.StartPositionInElement"/> so the preview stays where the user
    /// grabbed the source element.
    /// </summary>
    public Point? Hotspot { get; init; }

    /// <summary>
    /// Gets the drag-time opacity in the range 0 to 1; the default is 0.75.
    /// </summary>
    public double Opacity { get; init; } = 0.75;

    /// <summary>
    /// Gets the optional maximum width in DIPs of a hosted custom preview; ignored for snapshot and image
    /// previews.
    /// </summary>
    /// <remarks>
    /// When <see cref="Element"/> is a detached element (no parent), the framework hosts and lays it out as
    /// the preview, fitting its content and capping the width here (the element should trim or wrap its own
    /// text).
    /// </remarks>
    public double? MaxWidth { get; init; }

    /// <summary>
    /// Gets whether the preview is confined to the source window or follows the cursor across windows; the
    /// default is <see cref="DragPreviewScope.WithinWindow"/>.
    /// </summary>
    /// <remarks>
    /// Set <see cref="DragPreviewScope.CrossWindow"/> for drags that pop out (e.g. dockable panes). The
    /// framework picks the host and, when transparency is unavailable, degrades a cross-window preview to an
    /// opaque overlay window.
    /// </remarks>
    public DragPreviewScope Scope { get; init; } = DragPreviewScope.WithinWindow;
}
