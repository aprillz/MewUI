using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static partial class Dwmapi
{
    private const string LibraryName = "dwmapi.dll";

    /// <summary>
    /// DWMWINDOWATTRIBUTE enumeration for DwmSetWindowAttribute.
    /// </summary>
    internal enum DwmWindowAttribute : uint
    {
        /// <summary>
        /// Use immersive dark mode (Windows 10 build 17763-18985).
        /// </summary>
        DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19,

        /// <summary>
        /// Use immersive dark mode (Windows 10 build 18985+).
        /// </summary>
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
    }

    /// <summary>
    /// Sets the value of Desktop Window Manager (DWM) attributes for a window.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial int DwmSetWindowAttribute(
        nint hwnd,
        DwmWindowAttribute dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}

