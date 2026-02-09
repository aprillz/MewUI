namespace Aprillz.MewUI;

/// <summary>
/// Defines how the render loop schedules frames.
/// </summary>
public enum RenderLoopMode
{
    /// <summary>
    /// Renders only when requested via invalidation.
    /// </summary>
    OnRequest,
    /// <summary>
    /// Renders continuously (best effort), subject to <see cref="RenderLoopSettings.TargetFps"/> and VSync.
    /// </summary>
    Continuous
}
