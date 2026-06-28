namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies the display state of a window.
/// </summary>
public enum WindowState
{
    /// <summary>The window is in its normal size and position.</summary>
    Normal,

    /// <summary>The window is minimized to the taskbar.</summary>
    Minimized,

    /// <summary>The window is maximized to fill the screen work area.</summary>
    Maximized,

    /// <summary>The window covers the entire monitor with no chrome, over the taskbar/panel/menu bar.</summary>
    FullScreen,
}
