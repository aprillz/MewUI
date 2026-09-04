namespace Aprillz.MewUI.Platform.Linux.X11;

internal static class X11MonitorGeometry
{
    internal static Rect SelectMonitor(IReadOnlyList<Rect> monitors, Point point, Rect fallback)
    {
        var selected = fallback;
        double nearestDistance = double.PositiveInfinity;
        foreach (var monitor in monitors)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0)
                continue;
            if (point.X >= monitor.X && point.X < monitor.Right && point.Y >= monitor.Y && point.Y < monitor.Bottom)
                return monitor;
            double horizontalDistance = Math.Max(monitor.X - point.X, Math.Max(0, point.X - monitor.Right));
            double verticalDistance = Math.Max(monitor.Y - point.Y, Math.Max(0, point.Y - monitor.Bottom));
            double distance = horizontalDistance * horizontalDistance + verticalDistance * verticalDistance;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                selected = monitor;
            }
        }
        return selected;
    }

    internal static Rect ApplyStruts(Rect monitor, Rect root, IReadOnlyList<long[]> struts)
    {
        double left = monitor.X;
        double top = monitor.Y;
        double right = monitor.Right;
        double bottom = monitor.Bottom;
        foreach (var strut in struts)
        {
            if (strut.Length < 4)
                continue;
            bool partial = strut.Length >= 12;
            // EWMH ranges have inclusive end coordinates and depths measured from root edges.
            if (strut[0] > 0 && (!partial || Overlaps(strut[4], strut[5], monitor.Y, monitor.Bottom)))
                left = Math.Max(left, Math.Clamp(root.X + strut[0], monitor.X, monitor.Right));
            if (strut[1] > 0 && (!partial || Overlaps(strut[6], strut[7], monitor.Y, monitor.Bottom)))
                right = Math.Min(right, Math.Clamp(root.Right - strut[1], monitor.X, monitor.Right));
            if (strut[2] > 0 && (!partial || Overlaps(strut[8], strut[9], monitor.X, monitor.Right)))
                top = Math.Max(top, Math.Clamp(root.Y + strut[2], monitor.Y, monitor.Bottom));
            if (strut[3] > 0 && (!partial || Overlaps(strut[10], strut[11], monitor.X, monitor.Right)))
                bottom = Math.Min(bottom, Math.Clamp(root.Bottom - strut[3], monitor.Y, monitor.Bottom));
        }
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    internal static Rect IntersectWorkArea(Rect monitor, Rect desktopWorkArea)
    {
        double left = Math.Max(monitor.X, desktopWorkArea.X);
        double top = Math.Max(monitor.Y, desktopWorkArea.Y);
        double right = Math.Min(monitor.Right, desktopWorkArea.Right);
        double bottom = Math.Min(monitor.Bottom, desktopWorkArea.Bottom);
        if (right <= left || bottom <= top)
            return monitor;
        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool Overlaps(long start, long end, double minimum, double maximum)
        => end >= start && start < maximum && (double)end + 1 > minimum;
}
