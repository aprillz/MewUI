using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Core;

/// <summary>
/// Helper class for DPI-aware operations.
/// </summary>
public static class DpiHelper
{
    /// <summary>
    /// Per-Monitor DPI Awareness V2 context.
    /// </summary>
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    private const double DefaultDpi = 96.0;

    /// <summary>
    /// Enables Per-Monitor DPI V2 awareness for the current process.
    /// Call this at the start of the application.
    /// </summary>
    public static bool EnablePerMonitorDpiAwareness()
        => OperatingSystem.IsWindows() && User32.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    /// <summary>
    /// Gets the DPI for a specific window.
    /// </summary>
    public static uint GetDpiForWindow(nint hwnd)
    {
        if (!OperatingSystem.IsWindows())
            return GetSystemDpi();

        if (hwnd == 0)
            return GetSystemDpi();

        return User32.GetDpiForWindow(hwnd);
    }

    /// <summary>
    /// Gets the system DPI.
    /// </summary>
    public static uint GetSystemDpi() => OperatingSystem.IsWindows() ? User32.GetDpiForSystem() : 96u;

    /// <summary>
    /// Gets the scale factor for a specific window (DPI / 96).
    /// </summary>
    public static double GetScaleFactor(nint hwnd) => GetDpiForWindow(hwnd) / DefaultDpi;

    /// <summary>
    /// Gets the scale factor for the system.
    /// </summary>
    public static double GetSystemScaleFactor() => GetSystemDpi() / DefaultDpi;

    /// <summary>
    /// Scales a value for the given DPI.
    /// </summary>
    public static int Scale(int value, uint dpi) => (int)(value * dpi / DefaultDpi);

    /// <summary>
    /// Scales a value for the given DPI.
    /// </summary>
    public static double Scale(double value, uint dpi) => value * dpi / DefaultDpi;

    /// <summary>
    /// Scales a value for the given window's DPI.
    /// </summary>
    public static int ScaleForWindow(int value, nint hwnd) => Scale(value, GetDpiForWindow(hwnd));

    /// <summary>
    /// Scales a value for the given window's DPI.
    /// </summary>
    public static double ScaleForWindow(double value, nint hwnd) => Scale(value, GetDpiForWindow(hwnd));

    /// <summary>
    /// Unscales a value from the given DPI back to 96 DPI.
    /// </summary>
    public static int Unscale(int value, uint dpi) => (int)(value * DefaultDpi / dpi);

    /// <summary>
    /// Unscales a value from the given DPI back to 96 DPI.
    /// </summary>
    public static double Unscale(double value, uint dpi) => value * DefaultDpi / dpi;

    /// <summary>
    /// Gets a system metric scaled for the given DPI.
    /// </summary>
    public static int GetSystemMetricsForDpi(int nIndex, uint dpi)
        => OperatingSystem.IsWindows() ? User32.GetSystemMetricsForDpi(nIndex, dpi) : 0;
}
