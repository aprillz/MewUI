namespace Aprillz.MewUI.Rendering;

internal interface IReusableScratchSurface
{
    bool CanReturnToPool { get; }

    /// <summary>Whether the calling thread may render into this surface. Backends that bind an
    /// offscreen surface to the thread that created it return false on every other thread, and the
    /// scratch pool passes such a surface over instead of handing it to a renter that cannot use it.</summary>
    bool CanRenderFromCurrentThread => true;
}
