namespace Aprillz.MewUI.Controls;

/// <summary>
/// Minimal internal contract for scroll-driven content.
/// The scroll owner (e.g., <see cref="ScrollViewer"/>) provides viewport and offset,
/// while the content reports its logical extent and renders only what is necessary.
/// </summary>
internal interface IScrollContent
{
    /// <summary>
    /// Gets the logical content extent in DIPs.
    /// </summary>
    Size Extent { get; }

    /// <summary>
    /// Updates the current viewport size in DIPs.
    /// </summary>
    void SetViewport(Size viewport);

    /// <summary>
    /// Updates the current scroll offset in DIPs.
    /// </summary>
    void SetOffset(Point offset);
}

