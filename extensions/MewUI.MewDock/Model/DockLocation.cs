namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// A dock edge a node can be dropped onto (port of FlexLayout model/DockLocation.ts).
/// Geometry and serialization helpers live in <see cref="DockLocationExtensions"/>.
/// </summary>
public enum DockLocation
{
    Top,
    Bottom,
    Left,
    Right,
    Center,
}

/// <summary>Geometry and (de)serialization helpers for <see cref="DockLocation"/>.</summary>
public static class DockLocationExtensions
{
    public static Orientation GetOrientation(this DockLocation location) => location switch
    {
        DockLocation.Left or DockLocation.Right => Orientation.Horizontal,
        _ => Orientation.Vertical, // Top, Bottom, Center
    };

    /// <summary>0 for the leading edge (top/left/center), 1 for the trailing edge (bottom/right).</summary>
    internal static int IndexPlus(this DockLocation location) =>
        location is DockLocation.Bottom or DockLocation.Right ? 1 : 0;

    public static string GetName(this DockLocation location) => location switch
    {
        DockLocation.Top => "top",
        DockLocation.Bottom => "bottom",
        DockLocation.Left => "left",
        DockLocation.Right => "right",
        DockLocation.Center => "center",
        _ => throw new ArgumentOutOfRangeException(nameof(location)),
    };

    internal static DockLocation GetByName(string name) => name switch
    {
        "top" => DockLocation.Top,
        "bottom" => DockLocation.Bottom,
        "left" => DockLocation.Left,
        "right" => DockLocation.Right,
        "center" => DockLocation.Center,
        _ => throw new ArgumentException($"Unknown dock location '{name}'.", nameof(name)),
    };

    /// <summary>Classifies a point within a rect into an edge/center zone (original getLocation).</summary>
    internal static DockLocation GetLocation(Rect rect, double x, double y)
    {
        x = (x - rect.X) / rect.Width;
        y = (y - rect.Y) / rect.Height;

        if (x >= 0.25 && x < 0.75 && y >= 0.25 && y < 0.75)
        {
            return DockLocation.Center;
        }

        bool bottomLeftHalf = y >= x;
        bool bottomRightHalf = y >= 1 - x;

        if (bottomLeftHalf)
        {
            return bottomRightHalf ? DockLocation.Bottom : DockLocation.Left;
        }
        else
        {
            return bottomRightHalf ? DockLocation.Right : DockLocation.Top;
        }
    }

    /// <summary>The half-rect of <paramref name="r"/> corresponding to this location (center returns the whole rect).</summary>
    internal static Rect GetDockRect(this DockLocation location, Rect r) => location switch
    {
        DockLocation.Top => new Rect(r.X, r.Y, r.Width, r.Height / 2),
        DockLocation.Bottom => new Rect(r.X, r.Bottom - r.Height / 2, r.Width, r.Height / 2),
        DockLocation.Left => new Rect(r.X, r.Y, r.Width / 2, r.Height),
        DockLocation.Right => new Rect(r.Right - r.Width / 2, r.Y, r.Width / 2, r.Height),
        _ => r, // Center
    };

    /// <summary>Splits <paramref name="rect"/> at this location by <paramref name="size"/> into (start, end).</summary>
    internal static (Rect Start, Rect End) Split(this DockLocation location, Rect rect, double size) => location switch
    {
        DockLocation.Top => (
            new Rect(rect.X, rect.Y, rect.Width, size),
            new Rect(rect.X, rect.Y + size, rect.Width, rect.Height - size)),
        DockLocation.Left => (
            new Rect(rect.X, rect.Y, size, rect.Height),
            new Rect(rect.X + size, rect.Y, rect.Width - size, rect.Height)),
        DockLocation.Right => (
            new Rect(rect.Right - size, rect.Y, size, rect.Height),
            new Rect(rect.X, rect.Y, rect.Width - size, rect.Height)),
        _ => ( // Bottom
            new Rect(rect.X, rect.Bottom - size, rect.Width, size),
            new Rect(rect.X, rect.Y, rect.Width, rect.Height - size)),
    };

    /// <summary>The opposite edge (top/bottom and left/right swap).</summary>
    internal static DockLocation Reflect(this DockLocation location) => location switch
    {
        DockLocation.Top => DockLocation.Bottom,
        DockLocation.Bottom => DockLocation.Top,
        DockLocation.Left => DockLocation.Right,
        DockLocation.Right => DockLocation.Left,
        _ => DockLocation.Center,
    };
}
