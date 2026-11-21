using Aprillz.MewUI.Controls;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Asks the compositor to blur what is behind a window through the window property KDE's compositor reads. Other
/// X11 compositors offer no such property, so the window there gets no material.
/// </summary>
internal static unsafe class X11WindowBackdrop
{
    private const string BLUR_BEHIND_REGION = "_KDE_NET_WM_BLUR_BEHIND_REGION";
    private const nint XA_CARDINAL = 6;
    private const int PROP_MODE_REPLACE = 0;

    /// <summary>
    /// Shows <paramref name="material"/> behind <paramref name="window"/>, on <paramref name="area"/> or the whole window, as
    /// far as the compositor can, and returns what it shows.
    /// </summary>
    public static SystemBackdrop Apply(nint display, nint window, SystemBackdrop material, SystemBackdropArea? area, double dpiScale)
    {
        // The compositor creates the atom when its blur effect loads; without it nobody would read the property.
        nint atom = NativeX11.XInternAtom(display, BLUR_BEHIND_REGION, true);
        if (atom == 0)
        {
            return SystemBackdrop.None;
        }

        if (material == SystemBackdrop.None || !IsAnnounced(display, atom))
        {
            NativeX11.XDeleteProperty(display, window, atom);
            NativeX11.XFlush(display);
            return SystemBackdrop.None;
        }

        // No rectangles means the whole window. A blur is all this compositor offers, so every material comes down to it.
        if (area is SystemBackdropArea shape)
        {
            var rectangles = RegionOf(shape, dpiScale);
            fixed (nint* data = rectangles)
            {
                NativeX11.XChangeProperty(display, window, atom, XA_CARDINAL, 32, PROP_MODE_REPLACE, (nint)data, rectangles.Length);
            }
        }
        else
        {
            NativeX11.XChangeProperty(display, window, atom, XA_CARDINAL, 32, PROP_MODE_REPLACE, 0, 0);
        }

        NativeX11.XFlush(display);
        return SystemBackdrop.Blur;
    }

    /// <summary>
    /// The area as x, y, width, height rectangles in device pixels. The property has no corner radius, so each row of the
    /// rounded corners is a rectangle of its own.
    /// </summary>
    private static nint[] RegionOf(SystemBackdropArea area, double dpiScale)
    {
        int left = (int)Math.Round(area.Bounds.X * dpiScale);
        int top = (int)Math.Round(area.Bounds.Y * dpiScale);
        int width = Math.Max(0, (int)Math.Round(area.Bounds.Right * dpiScale) - left);
        int height = Math.Max(0, (int)Math.Round(area.Bounds.Bottom * dpiScale) - top);
        int radius = Math.Min((int)Math.Ceiling(area.CornerRadius * dpiScale), Math.Min(width, height) / 2);

        const int FIELDS = 4;
        var region = new nint[(radius * 2 + 1) * FIELDS];
        int next = 0;
        void Add(int rectX, int rectY, int rectWidth, int rectHeight)
        {
            region[next++] = rectX;
            region[next++] = rectY;
            region[next++] = rectWidth;
            region[next++] = rectHeight;
        }

        for (int row = 0; row < radius; row++)
        {
            double fromCenter = radius - row - 0.5;
            int inset = (int)Math.Ceiling(radius - Math.Sqrt(radius * radius - fromCenter * fromCenter));
            Add(left + inset, top + row, width - inset * 2, 1);
            Add(left + inset, top + height - row - 1, width - inset * 2, 1);
        }

        Add(left, top + radius, width, height - radius * 2);
        return region;
    }

    /// <summary>Whether a running compositor announces the blur, which it does with the same property on the root window.</summary>
    private static bool IsAnnounced(nint display, nint atom)
    {
        const nint ANY_PROPERTY_TYPE = 0;
        nint root = NativeX11.XRootWindow(display, NativeX11.XDefaultScreen(display));
        int status = NativeX11.XGetWindowProperty(display, root, atom, 0, 1, false, ANY_PROPERTY_TYPE,
            out nint actualType, out _, out _, out _, out nint data);
        if (data != 0)
        {
            NativeX11.XFree(data);
        }

        return status == 0 && actualType != 0;
    }
}
