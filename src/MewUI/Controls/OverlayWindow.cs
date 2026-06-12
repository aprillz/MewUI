namespace Aprillz.MewUI;

/// <summary>
/// A framework-internal, click-through, non-activating, transparent top-level overlay that floats above other
/// windows (used for the drag-preview overlay). Mouse events pass through to whatever is behind it and showing
/// it never steals focus. Position it with <see cref="Window.MoveTo"/> and size it via <see cref="Window.WindowSize"/>.
/// </summary>
internal sealed class OverlayWindow : Window
{
    public OverlayWindow()
    {
        AllowsTransparency = true;
        Topmost = true;
        ShowInTaskbar = false;
        IsOverlayWindow = true;
        StartupLocation = WindowStartupLocation.Manual;
        Background = Color.Transparent;
    }
}
