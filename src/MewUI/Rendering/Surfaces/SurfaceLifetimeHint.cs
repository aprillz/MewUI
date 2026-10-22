namespace Aprillz.MewUI.Rendering;

/// <summary>How long the creator expects to keep a surface; a backend may choose its allocation strategy by it.</summary>
public enum SurfaceLifetimeHint
{
    /// <summary>Lives for the current frame.</summary>
    Frame = 0,
    /// <summary>Short-lived, but may outlast a frame.</summary>
    Transient,
    /// <summary>Returned to the scratch pool for reuse.</summary>
    Pooled,
    /// <summary>Retained across frames.</summary>
    Cached,
    /// <summary>Owned and managed outside the backend.</summary>
    External,
}
