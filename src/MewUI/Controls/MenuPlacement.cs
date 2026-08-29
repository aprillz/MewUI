namespace Aprillz.MewUI.Controls;

/// <summary>
/// Where a <see cref="ContextMenu"/> opens relative to its placement target. Side placements flip
/// to the opposite side when the preferred side has no room.
/// </summary>
public enum MenuPlacement
{
    /// <summary>At the pointer position. The context-menu default.</summary>
    Pointer,

    /// <summary>Under the target's bottom edge, flipping above the top edge when out of room.</summary>
    Below,

    /// <summary>Over the target's top edge, flipping below when out of room.</summary>
    Above,

    /// <summary>Beside the target's right edge, flipping to the left when out of room.</summary>
    Right,

    /// <summary>Beside the target's left edge, flipping to the right when out of room.</summary>
    Left,
}
