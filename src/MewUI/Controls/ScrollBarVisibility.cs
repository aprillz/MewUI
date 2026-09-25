namespace Aprillz.MewUI.Controls;

/// <summary>
/// When a control that always scrolls its content shows a scroll bar.
/// </summary>
public enum ScrollBarVisibility
{
    /// <summary>Shown while the content is longer than the viewport.</summary>
    Auto,

    /// <summary>Always shown; with nothing to scroll, the bar is disabled.</summary>
    Visible,
}
