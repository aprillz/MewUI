namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies what the platform shows behind the transparent parts of a window.
/// </summary>
public enum SystemBackdrop
{
    /// <summary>Nothing: transparent parts show what is behind the window as it is.</summary>
    None,

    /// <summary>What is behind the window, blurred.</summary>
    Blur,

    /// <summary>What is behind the window, blurred more heavily and saturated, as a frosted sheet.</summary>
    Acrylic,

    /// <summary>The desktop wallpaper, blurred and tinted, which stays still when the window moves over other windows.</summary>
    Mica,
}
