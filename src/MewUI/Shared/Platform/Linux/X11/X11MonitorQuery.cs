using System.Runtime.InteropServices;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal static class X11MonitorQuery
{
    internal static List<Rect> Read(nint display, nint root)
    {
        var monitors = new List<Rect>();
        try
        {
            if (XRandrExt.XRRQueryVersion(display, out int major, out int minor) != 0 &&
                (major > 1 || (major == 1 && minor >= 5)))
            {
                nint data = XRandrExt.XRRGetMonitors(display, root, 1, out int count);
                if (data != 0)
                {
                    try
                    {
                        int stride = Marshal.SizeOf<XRandrExt.MonitorInfo>();
                        for (int index = 0; index < count; index++)
                        {
                            var monitor = Marshal.PtrToStructure<XRandrExt.MonitorInfo>(data + index * stride);
                            if (monitor.Width > 0 && monitor.Height > 0)
                                monitors.Add(new Rect(monitor.X, monitor.Y, monitor.Width, monitor.Height));
                        }
                    }
                    finally { XRandrExt.XRRFreeMonitors(data); }
                }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        if (monitors.Count != 0)
            return monitors;
        try
        {
            if (XRandrExt.XineramaIsActive(display) != 0)
            {
                nint data = XRandrExt.XineramaQueryScreens(display, out int count);
                if (data != 0)
                {
                    try
                    {
                        int stride = Marshal.SizeOf<XRandrExt.XineramaScreenInfo>();
                        for (int index = 0; index < count; index++)
                        {
                            var monitor = Marshal.PtrToStructure<XRandrExt.XineramaScreenInfo>(data + index * stride);
                            if (monitor.Width > 0 && monitor.Height > 0)
                                monitors.Add(new Rect(monitor.X, monitor.Y, monitor.Width, monitor.Height));
                        }
                    }
                    finally { Aprillz.MewUI.Native.X11.XFree(data); }
                }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return monitors;
    }
}
