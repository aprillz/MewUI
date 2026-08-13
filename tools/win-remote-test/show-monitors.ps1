# Prints the display matrix as the test suite sees it: resolution, scale, and the scale transitions the
# suite will generate from it. Run it through the broker, not over SSH: the sshd service session reports
# one placeholder display, so running it directly would report a machine that does not exist.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class MonitorList
{
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public int rcMonitorLeft, rcMonitorTop, rcMonitorRight, rcMonitorBottom;
        public int rcWorkLeft, rcWorkTop, rcWorkRight, rcWorkBottom;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public static List<string> All()
    {
        // Per-monitor v2, or every monitor reports the primary's scale.
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        var results = new List<string>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data)
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfoW(monitor, ref info))
            {
                uint dpiX, dpiY;
                uint dpi = GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 ? dpiX : 96;
                int width = info.rcMonitorRight - info.rcMonitorLeft;
                int height = info.rcMonitorBottom - info.rcMonitorTop;
                bool primary = (info.dwFlags & 1) != 0;
                results.Add(string.Format("{0}x{1}@{2}% {3} [{4}]",
                    width, height, (int)Math.Round(dpi * 100.0 / 96.0),
                    primary ? "primary" : "secondary", info.szDevice));
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }
}
'@

$monitors = [MonitorList]::All()

Write-Output "session : $env:SESSIONNAME"
if ($env:SESSIONNAME -ne 'Console') {
    Write-Output 'WARNING: not the console session, so these are not the physical displays.'
}

Write-Output "displays: $($monitors.Count)"
foreach ($monitor in $monitors) { Write-Output "  $monitor" }

$scales = $monitors | ForEach-Object { ($_ -split '@')[1].Split('%')[0] } | Sort-Object -Unique
Write-Output "scales  : $($scales -join ', ') percent"

# The suite pairs every two differing scales in both directions, which is what the run is worth.
$count = $scales.Count * ($scales.Count - 1)
Write-Output "the suite will generate $count scale transitions"
