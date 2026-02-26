namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Implemented by objects that can be frozen (made immutable) to enable
/// backend-level caching of native handles or compiled resources.
/// </summary>
public interface IFreezable
{
    /// <summary>Gets a value indicating whether the object has been frozen.</summary>
    bool IsFrozen { get; }

    /// <summary>
    /// Freezes the object, preventing further mutation.
    /// After freezing, the object may be cached by backends (e.g. a compiled
    /// <see cref="PathGeometry"/> native handle).
    /// Calling <see cref="Freeze"/> on an already-frozen object is a no-op.
    /// </summary>
    void Freeze();
}

internal static class FreezableHelper
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="obj"/> is frozen.
    /// Call this at the start of every mutating method.
    /// </summary>
    public static void ThrowIfFrozen(IFreezable obj)
    {
        if (obj.IsFrozen)
            throw new InvalidOperationException("Cannot modify a frozen object.");
    }
}
