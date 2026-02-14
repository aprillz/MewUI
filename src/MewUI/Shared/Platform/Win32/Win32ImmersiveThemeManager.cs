using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;

using static Aprillz.MewUI.Native.Dwmapi;

namespace Aprillz.MewUI.Platform.Win32;

internal sealed class Win32ImmersiveThemeManager
{
    private static readonly bool ImmersiveThemeSupportedByVersion = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
    private static readonly bool Is20H1OrGreater = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985);
    private static readonly bool Is11OrGreater = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private static readonly DwmWindowAttribute ImmersiveAttribute = Is20H1OrGreater
        ? DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE
        : DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;

    /// <summary>
    /// Checks whether immersive themes are supported based on the operating system version.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the Windows version meets the minimum requirement for immersive themes; <br/> 
    /// otherwise <c>false</c>.
    /// </returns>
    public static bool IsImmersiveThemeSupportedByVersion() => ImmersiveThemeSupportedByVersion;

    /// <summary>
    /// Enables the window to use the immersive theme mode colors for the title bar.
    /// </summary>
    /// <param name="hwnd">Handle to the window.</param>
    /// <param name="isDarkMode">True to use dark immersive colors, false to use light immersive colors.</param>
    /// <returns>True if the attribute was set successfully, false if unsupported or failed.</returns>
    public static bool SetWindowImmersiveTheme(nint hwnd, bool isDarkMode)
    {
        if (hwnd == 0 || !ImmersiveThemeSupportedByVersion)
        {
            return false;
        }

        int value = isDarkMode ? 1 : 0;
        int result = DwmSetWindowAttribute(hwnd, ImmersiveAttribute, ref value, sizeof(int));
        bool success = result >= 0;

        if (success && !Is11OrGreater)
        {
            bool isActive = User32.GetForegroundWindow() == hwnd;
            int targetState = isActive ? 1 : 0;
            int toggleState = isActive ? 0 : 1;

            //Workaround for Windows 10
            // Force the non-client area to refresh by toggling activation and restoring the actual state
            User32.SendMessage(hwnd, WindowMessages.WM_NCACTIVATE, toggleState, 0);
            User32.SendMessage(hwnd, WindowMessages.WM_NCACTIVATE, targetState, 0);
        }

        return success;
    }
}
