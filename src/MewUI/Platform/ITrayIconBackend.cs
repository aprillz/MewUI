namespace Aprillz.MewUI.Platform;

/// <summary>
/// The platform side of a shown <see cref="Controls.TrayIcon"/>; disposing it removes the icon. Used on the UI thread only.
/// </summary>
internal interface ITrayIconBackend : IDisposable
{
    /// <summary>Applies the icon's current image, monochrome flag, tooltip text and whether it has a menu.</summary>
    void Update();
}
