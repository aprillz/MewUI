using System.Runtime.InteropServices;

namespace MewUI.WindowAutomationTest;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + (Width / 2);
    public int CenterY => Top + (Height / 2);
}

/// <summary>
/// One physical monitor as the OS reports it: where it sits in the virtual desktop, and at what
/// scale. Scale is what these tests care about, so it leads the label.
/// </summary>
public sealed record MonitorProbe(string DeviceName, PixelRect PixelBounds, uint Dpi, bool IsPrimary)
{
    public int ScalePercent => (int)Math.Round(Dpi * 100.0 / 96.0);

    /// <summary>Resolution, scale and role, which is how a display is described when reporting a failure.</summary>
    public string Label
        => $"{ResolutionClass}{PixelBounds.Width}x{PixelBounds.Height}@{ScalePercent}%{(IsPrimary ? " primary" : " secondary")}";

    private string ResolutionClass => PixelBounds.Width switch
    {
        >= 3840 => "UHD ",
        >= 2560 => "QHD ",
        >= 1920 => "FHD ",
        _ => string.Empty,
    };

    public override string ToString() => Label;

    public static IReadOnlyList<MonitorProbe> All()
    {
        var monitors = new List<MonitorProbe>();
        if (!OperatingSystem.IsWindows())
        {
            return monitors;
        }

        EnablePerMonitorAwareness();

        EnumDisplayMonitors(0, 0, (nint monitor, nint _, nint _, nint _) =>
        {
            var info = new MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = new string('\0', 32),
            };
            if (GetMonitorInfo(monitor, ref info))
            {
                uint dpi = 96;
                // MDT_EFFECTIVE_DPI = 0
                if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0)
                {
                    dpi = dpiX;
                }

                const uint MONITORINFOF_PRIMARY = 0x00000001;
                monitors.Add(new MonitorProbe(
                    info.szDevice.TrimEnd('\0'),
                    new PixelRect(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom),
                    dpi,
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }

            return true;
        }, 0);

        return monitors;
    }

    /// <summary>
    /// The test host starts DPI-unaware, and an unaware process is told every monitor runs at 96
    /// DPI, which would hide the very transitions these tests exist to drive. Awareness has to be
    /// set before the first query and before any window exists, so it happens here rather than in
    /// the application startup that runs later.
    /// </summary>
    private static void EnablePerMonitorAwareness()
    {
        if (_awarenessRequested)
        {
            return;
        }

        _awarenessRequested = true;
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            SetProcessDpiAwarenessContext(-4);
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows: the platform layer's own fallback chain still applies at startup.
        }
    }

    private static bool _awarenessRequested;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint lprcMonitor, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
    internal const uint MOVE_ONLY = 0x0001 | 0x0004 | 0x0010;

    // SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
    internal const uint RESIZE_ONLY = 0x0002 | 0x0004 | 0x0010;
}
