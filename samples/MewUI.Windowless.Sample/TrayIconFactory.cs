namespace Aprillz.MewUI.Windowless.Sample;

internal static class TrayIconFactory
{
    /// <summary>The tray icon for this platform, or null where the sample has none (Linux).</summary>
    public static ITrayIconBackend? Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Win32TrayIcon();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSTrayIcon();
        }

        return null;
    }
}
