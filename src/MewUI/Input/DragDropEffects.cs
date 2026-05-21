namespace Aprillz.MewUI;

/// <summary>
/// Describes the effects that a drag-and-drop operation can produce.
/// Source advertises allowed effects; target selects one (or None to reject).
/// </summary>
[Flags]
public enum DragDropEffects
{
    /// <summary>No effect; target rejects the drop or source cancels.</summary>
    None = 0,

    /// <summary>The data is copied to the target.</summary>
    Copy = 1,

    /// <summary>The data is moved from the source to the target.</summary>
    Move = 2,

    /// <summary>A link to the data is established at the target.</summary>
    Link = 4,
}
