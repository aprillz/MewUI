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
    /// A UI element to snapshot for the drag preview.
    /// In Phase 1, rendering uses the element's current bounds as a translucent placeholder;
    /// proper offscreen snapshot is deferred to a follow-up phase.
    /// </summary>
    public UIElement? Element { get; init; }

    /// <summary>
    /// A pre-rendered image used as the drag preview.
    /// </summary>
    public IImage? Image { get; init; }

    /// <summary>
    /// Pixel size of the preview when only <see cref="Size"/> is meaningful (no Element/Image).
    /// </summary>
    public Size Size { get; init; } = new(64, 64);

    /// <summary>
    /// Offset of the cursor relative to the preview's top-left corner (DIPs).
    /// When <see langword="null"/>, the framework uses <see cref="DragStartingEventArgs.StartPositionInElement"/>
    /// so the preview lines up exactly where the user grabbed the source element.
    /// </summary>
    public Point? Hotspot { get; init; }

    /// <summary>
    /// Drag-time opacity in the range [0..1]. Defaults to 0.75.
    /// </summary>
    public double Opacity { get; init; } = 0.75;

    /// <summary>
    /// Optional max-width limit (DIPs) for a hosted custom preview. When <see cref="Element"/> is a detached
    /// element (no parent), the framework hosts and lays it out as the preview, fitting its content and capping
    /// the width here (the element should trim/wrap its own text). Ignored for snapshot/image previews.
    /// </summary>
    public double? MaxWidth { get; init; }

    /// <summary>
    /// Whether the preview is confined to the source window or follows the cursor across windows.
    /// Defaults to <see cref="DragPreviewScope.WithinWindow"/>; set <see cref="DragPreviewScope.CrossWindow"/>
    /// for drags that pop out (e.g. dockable panes). The framework picks the host and, when transparency is
    /// unavailable, transparently degrades a cross-window preview to an opaque overlay window.
    /// </summary>
    public DragPreviewScope Scope { get; init; } = DragPreviewScope.WithinWindow;
}
