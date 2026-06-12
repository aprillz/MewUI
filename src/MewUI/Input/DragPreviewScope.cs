namespace Aprillz.MewUI;

/// <summary>
/// Declares whether a drag preview is confined to the source window or follows the cursor across windows.
/// Drives how the framework hosts the preview (see <see cref="DragPreviewContent.Scope"/>).
/// </summary>
public enum DragPreviewScope
{
    /// <summary>
    /// The preview stays inside the window it is over. Hosted as a per-window overlay that blends into the
    /// window surface (real transparency, no compositor required), but vanishes over the desktop or gaps.
    /// </summary>
    WithinWindow,

    /// <summary>
    /// The preview follows the cursor across windows and the desktop. Hosted as a top-level overlay window:
    /// transparent when the platform composites it, otherwise opaque so it stays continuous.
    /// </summary>
    CrossWindow,
}
