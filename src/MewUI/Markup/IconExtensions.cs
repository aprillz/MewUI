using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

public static class IconExtensions
{
    public static Icon Data(this Icon icon, PathGeometry geometry)
    {
        icon.Data = geometry;
        return icon;
    }

    public static Icon Data(this Icon icon, string svgPathData)
    {
        icon.Data = PathGeometry.Parse(svgPathData);
        return icon;
    }

    public static Icon Foreground(this Icon icon, Color color)
    {
        icon.Foreground = color;
        return icon;
    }

    public static Icon FontSize(this Icon icon, double fontSize)
    {
        icon.FontSize = fontSize;
        return icon;
    }
}
