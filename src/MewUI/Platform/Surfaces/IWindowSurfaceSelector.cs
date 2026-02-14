namespace Aprillz.MewUI.Platform;

/// <summary>
/// Optional hint implemented by a graphics backend to request a specific native window surface kind.
/// </summary>
public interface IWindowSurfaceSelector
{
    /// <summary>
    /// Preferred native surface kind for window rendering.
    /// </summary>
    WindowSurfaceKind PreferredSurfaceKind { get; }
}
