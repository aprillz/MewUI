namespace Aprillz.MewUI;

/// <summary>
/// A framework-internal, click-through, non-activating top-level overlay that floats above other windows
/// (used for the drag-preview overlay). Mouse events pass through to whatever is behind it and showing it never
/// steals focus. Transparent by default; construct with <c>transparent: false</c> on platforms that cannot
/// composite a transparent overlay, giving an opaque window with the theme window background. 
/// </summary>
internal sealed class OverlayWindow : Window
{
    public OverlayWindow(bool transparent = true)
    {
        Topmost = true;
        ShowInTaskbar = false;
        IsOverlayWindow = true;
        StartupLocation = WindowStartupLocation.Manual;
        AllowsTransparency = transparent;
        Padding = new(0);
        Background = transparent ? Color.Transparent : Theme.Palette.WindowBackground;
    }
}
