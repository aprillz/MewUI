namespace Aprillz.MewUI.Controls;

/// <summary>
/// Selects the edge that item content anchors to inside the viewport. Item order is always
/// top to bottom; this only decides where the content block sits and which edge it follows.
/// </summary>
public enum ItemsAnchor
{
    /// <summary>Content starts at the top edge. Content shorter than the viewport leaves empty space below.</summary>
    Top = 0,

    /// <summary>
    /// Content sits against the bottom edge, and scrolling stays pinned to the end when new
    /// items arrive while the view is already at the end.
    /// </summary>
    Bottom = 1,
}
