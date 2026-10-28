namespace Aprillz.MewUI;

/// <summary>
/// The clip a visual draws a child under: a rectangle in window coordinates, rounded when a radius is positive.
/// </summary>
public readonly record struct ChildClip(Rect Rect, double RadiusX = 0, double RadiusY = 0)
{
    /// <summary>Creates a clip with the same radius on both axes.</summary>
    public static ChildClip Rounded(Rect rect, double radius) => new(rect, radius, radius);

    /// <summary>True when a radius is positive, so the clip has rounded corners.</summary>
    public bool IsRounded => RadiusX > 0 || RadiusY > 0;
}
