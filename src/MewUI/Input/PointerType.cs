namespace Aprillz.MewUI;

/// <summary>Device that produced a pointer event.</summary>
internal enum PointerType
{
    /// <summary>An indirect device with a hover state and a persistent cursor.</summary>
    Mouse,

    /// <summary>A finger on the surface, with no hover state.</summary>
    Touch,

    /// <summary>A stylus, which hovers but is otherwise direct like touch.</summary>
    Pen,
}
