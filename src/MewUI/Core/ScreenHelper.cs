namespace Aprillz.MewUI;

/// <summary>
/// Queries about the screens the application runs on.
/// </summary>
public static class ScreenHelper
{
    /// <summary>
    /// Gets the work area of the monitor that contains the given screen point, in device pixels (origin top-left,
    /// Y down), or <see cref="Rect.Empty"/> when no application is running or the platform cannot report it.
    /// </summary>
    public static Rect GetWorkAreaForPoint(Point screenPositionPx)
        => Application.IsRunning ? Application.Current.PlatformHost.GetWorkAreaForPoint(screenPositionPx) : Rect.Empty;
}
